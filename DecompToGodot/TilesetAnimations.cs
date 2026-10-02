using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DecompToGodot
{
    /// <summary>
    /// One animated tile range, e.g. the General water (VRAM tiles 416..463, 8 frames,
    /// advanced every 16 ticks). Parsed from src/tileset_anims.c.
    /// </summary>
    public class TileAnimation
    {
        public string Name;          // queue function name, e.g. QueueAnimTiles_General_Flower
        public string OwnerTileset;  // e.g. gTileset_General
        public int DestTile;         // first VRAM tile index that is overwritten (0..1023)
        public int TileCount;        // number of 8×8 tiles copied per frame
        public int Interval;         // the frame is advanced when timer % Interval == phase
        public int ArgDivisor;       // the queue function receives timer / ArgDivisor
        public int CounterMax;       // the tileset anim counter wraps at this value
        public string[] FramePaths;  // frame PNGs in playback order (may repeat, e.g. 0,1,2,1)

        /// <summary>Loaded 4bpp pixel indices per entry of <see cref="FramePaths"/>.</summary>
        public byte[][,] FramePixels;

        public bool Covers(int tileId) => tileId >= DestTile && tileId < DestTile + TileCount;

        /// <summary>
        /// Frame shown at a given tick. The real game queues frames on slightly offset
        /// ticks (timer % 16 == 1, == 2, ...) to spread DMA work. That one tick offset
        /// is not visible, so frames are aligned to timer % Interval == 0 here. That
        /// keeps every animation in sync and avoids one tick long Godot frames.
        /// </summary>
        public int FrameAt(int tick)
        {
            int timer = tick % CounterMax;
            int step = timer - (timer % Interval);
            return (step / ArgDivisor) % FramePaths.Length;
        }
    }

    /// <summary>
    /// Parses the tileset animation code of a pokefirered style decomp:
    ///   src/tileset_anims.c   frame INCBINs, frame tables, QueueAnimTiles_* and TilesetAnim_* functions
    ///   src/data/tilesets/headers.h   .callback = InitTilesetAnim_*  (passed in by the caller)
    /// </summary>
    public static class TilesetAnimationParser
    {
        public const double GBA_FPS = 59.7275;

        public static Dictionary<string, List<TileAnimation>> Parse(
            string decompPath, Dictionary<string, string> tilesetCallbacks)
        {
            var result = new Dictionary<string, List<TileAnimation>>();
            string path = Path.Combine(decompPath, "src", "tileset_anims.c");
            if (!File.Exists(path))
            {
                Console.WriteLine("WARNING: src/tileset_anims.c not found. Tile animations are skipped.");
                return result;
            }

            string src = StripComments(File.ReadAllText(path));

            // static const u16 sFoo_Frame0[] = INCBIN_U16("data/.../0.4bpp");
            var frameFiles = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(src,
                @"const\s+u16\s+(\w+)\s*\[[^\]]*\]\s*=\s*INCBIN_U16\(\s*""([^""]+)""\s*\)"))
            {
                frameFiles[m.Groups[1].Value] = m.Groups[2].Value;
            }

            // static const u16 *const sFoo[] = { sFoo_Frame0, sFoo_Frame1 };
            var frameTables = new Dictionary<string, string[]>();
            foreach (Match m in Regex.Matches(src,
                @"const\s+u16\s*\*\s*const\s+(\w+)\s*\[[^\]]*\]\s*=\s*\{([^}]*)\}"))
            {
                frameTables[m.Groups[1].Value] = m.Groups[2].Value
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToArray();
            }

            var functions = ParseFunctions(src);

            // QueueAnimTiles_*: which frame table goes to which VRAM tiles
            var queues = new Dictionary<string, List<Tuple<string, int, int>>>();
            foreach (var fn in functions)
            {
                var copies = new List<Tuple<string, int, int>>();
                foreach (Match m in Regex.Matches(fn.Value,
                    @"AppendTilesetAnimToBuffer\s*\((.*?)\)\s*;", RegexOptions.Singleline))
                {
                    string args = m.Groups[1].Value;
                    string table = frameTables.Keys.FirstOrDefault(t => Regex.IsMatch(args, @"\b" + t + @"\b"));
                    var dest = Regex.Match(args, @"TILE_OFFSET_4BPP\s*\(([^()]*)\)");
                    var size = Regex.Match(args, @"([\w\s+*()-]+?)\s*\*\s*TILE_SIZE_4BPP");
                    if (table == null || !dest.Success || !size.Success) continue;

                    int destTile, count;
                    if (!TryEval(dest.Groups[1].Value, out destTile) || !TryEval(size.Groups[1].Value, out count))
                        continue;
                    copies.Add(Tuple.Create(table, destTile, count));
                }
                if (copies.Count > 0)
                    queues[fn.Key] = copies;
            }

            // InitTilesetAnim_*: counter max and per tick callback
            foreach (var kv in tilesetCallbacks)
            {
                string tileset = kv.Key;
                string init = kv.Value;
                string initBody;
                if (!functions.TryGetValue(init, out initBody)) continue;

                var maxMatch = Regex.Match(initBody, @"CounterMax\s*=\s*(\d+)");
                var cbMatch = Regex.Match(initBody, @"AnimCallback\s*=\s*(\w+)");
                if (!maxMatch.Success || !cbMatch.Success) continue;

                int counterMax = int.Parse(maxMatch.Groups[1].Value);
                string cbBody;
                if (!functions.TryGetValue(cbMatch.Groups[1].Value, out cbBody)) continue;

                var list = new List<TileAnimation>();

                // if (timer % 16 == 1) QueueAnimTiles_Foo(timer / 16);
                foreach (Match m in Regex.Matches(cbBody,
                    @"timer\s*%\s*(\d+)\s*==\s*(\d+)\s*\)\s*(\w+)\s*\(\s*timer\s*(?:/\s*(\d+)\s*)?\)"))
                {
                    int interval = int.Parse(m.Groups[1].Value);
                    string queue = m.Groups[3].Value;
                    int argDiv = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 1;

                    List<Tuple<string, int, int>> copies;
                    if (!queues.TryGetValue(queue, out copies)) continue;

                    foreach (var copy in copies)
                    {
                        var paths = frameTables[copy.Item1]
                            .Select(f => frameFiles.ContainsKey(f) ? frameFiles[f] : null)
                            .ToArray();
                        if (paths.Length == 0 || paths.Any(p => p == null)) continue;

                        list.Add(new TileAnimation
                        {
                            Name = queue,
                            OwnerTileset = tileset,
                            DestTile = copy.Item2,
                            TileCount = copy.Item3,
                            Interval = interval,
                            ArgDivisor = argDiv,
                            CounterMax = counterMax,
                            FramePaths = paths.Select(p => Path.ChangeExtension(p, ".png")).ToArray()
                        });
                    }
                }

                if (list.Count > 0)
                    result[tileset] = list;
            }

            // Load frame pixels (cache by path, frame tables often repeat images)
            var cache = new Dictionary<string, byte[,]>();
            foreach (var anims in result.Values)
            {
                foreach (var a in anims)
                {
                    a.FramePixels = new byte[a.FramePaths.Length][,];
                    for (int i = 0; i < a.FramePaths.Length; i++)
                    {
                        string full = Path.Combine(decompPath, a.FramePaths[i]);
                        byte[,] px;
                        if (!cache.TryGetValue(full, out px))
                        {
                            px = File.Exists(full) ? TilesetRenderer.LoadTilePixelIndices(full) : null;
                            if (px == null)
                                Console.WriteLine($"  WARNING: animation frame missing: {a.FramePaths[i]}");
                            cache[full] = px;
                        }
                        a.FramePixels[i] = px;
                    }
                }
            }

            foreach (var kv in result)
                foreach (var a in kv.Value)
                    Console.WriteLine($"  Tile animation: {StripTilesetPrefix(kv.Key)} {a.Name} tiles {a.DestTile}..{a.DestTile + a.TileCount - 1}, {a.FramePaths.Length} frames every {a.Interval} ticks");

            return result;
        }

        private static string StripTilesetPrefix(string label)
            => label.StartsWith("gTileset_") ? label.Substring("gTileset_".Length) : label;

        /// <summary>Find every top level function: name → body (without the outer braces).</summary>
        private static Dictionary<string, string> ParseFunctions(string src)
        {
            var result = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(src, @"\bvoid\s+(\w+)\s*\([^)]*\)\s*\{"))
            {
                int start = m.Index + m.Length;
                int depth = 1;
                int i = start;
                while (i < src.Length && depth > 0)
                {
                    if (src[i] == '{') depth++;
                    else if (src[i] == '}') depth--;
                    i++;
                }
                result[m.Groups[1].Value] = src.Substring(start, Math.Max(0, i - start - 1));
            }
            return result;
        }

        private static string StripComments(string src)
        {
            src = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            return Regex.Replace(src, @"//[^\n]*", " ");
        }

        /// <summary>Evaluate small constant expressions like "NUM_TILES_IN_PRIMARY + 16" or "(4)".</summary>
        private static bool TryEval(string expr, out int value)
        {
            value = 0;
            expr = expr.Replace("NUM_TILES_IN_PRIMARY", TilesetRenderer.NUM_TILES_IN_PRIMARY.ToString())
                       .Replace("(", " ").Replace(")", " ").Trim();
            if (expr.Length == 0) return false;

            int sum = 0;
            foreach (var term in Regex.Split(expr, @"(?=[+-])"))
            {
                string t = term.Trim();
                if (t.Length == 0) continue;
                int sign = 1;
                if (t[0] == '+' || t[0] == '-') { sign = t[0] == '-' ? -1 : 1; t = t.Substring(1).Trim(); }

                int product = 1;
                foreach (var factor in t.Split('*'))
                {
                    int f;
                    if (!int.TryParse(factor.Trim(), out f)) return false;
                    product *= f;
                }
                sum += sign * product;
            }
            value = sum;
            return true;
        }
    }
}
