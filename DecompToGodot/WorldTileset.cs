using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DecompToGodot
{
    /// <summary>
    /// Everything loaded from one data/tilesets/&lt;primary|secondary&gt;/&lt;name&gt; folder.
    /// </summary>
    public class TilesetData
    {
        public string Label;          // e.g. gTileset_PalletTown
        public bool IsSecondary;
        public byte[,] TilePixels;    // 4bpp indices of tiles.png (may be null)
        public Color[][] Palettes;    // all 16 palettes of the folder
        public ushort[][] Metatiles;
        public uint[] Attributes;
        public List<TileAnimation> Animations = new List<TileAnimation>();

        public string Name => Label.StartsWith("gTileset_") ? Label.Substring("gTileset_".Length) : Label;
        public int FirstMetatileId => IsSecondary ? TilesetRenderer.NUM_METATILES_IN_PRIMARY : 0;

        public uint GetAttribute(int localIndex)
            => localIndex >= 0 && localIndex < Attributes.Length ? Attributes[localIndex] : 0;
    }

    /// <summary>
    /// A registered tile inside a <see cref="TilesetUnit"/>. The same atlas coordinates
    /// are used in the unit's Ground and Overlay atlas.
    /// </summary>
    public class TileSlot
    {
        public int MetatileId;        // full metatile id as stored in map.bin (0..1023)
        public int X, Y;              // atlas coordinates
        public uint Attributes;
        public bool IsBorrowed;       // a primary metatile drawn with this secondary's tiles
        public int AnimationColumns;  // 0 = all frames on one row
        public double[] FrameDurations; // null for static tiles
    }

    /// <summary>
    /// One tileset rendered once into a Ground and an Overlay atlas.
    ///
    /// Primary units hold every primary metatile that only reads primary tiles and
    /// palettes. Secondary units hold their own metatiles, rendered with the primary
    /// they are paired with, plus the few primary metatiles that read secondary
    /// tiles or palettes (Building 576..639 for example). Those "borrowed" metatiles
    /// look different for every secondary, so each secondary carries its own copy.
    ///
    /// Atlas layout (8 columns of 16×16 cells):
    ///   rows 0..N       the tileset's own metatiles, cell = local id % 8, local id / 8
    ///                   (same layout as Porymap). Animated metatiles only show their
    ///                   first frame here as a picture and are not registered.
    ///   next rows       borrowed primary metatiles (secondary units only)
    ///   last rows       animated metatiles, each one followed by its frames
    /// </summary>
    public class TilesetUnit
    {
        public string Name;
        public string FileBase;
        public TilesetData Own;
        public TilesetData Primary;    // render context
        public TilesetData Secondary;  // render context (null for primary units)
        public int GroundSourceId, OverlaySourceId;
        public int Rows;
        public readonly List<TileSlot> Slots = new List<TileSlot>();
        public readonly Dictionary<int, TileSlot> SlotsById = new Dictionary<int, TileSlot>();

        public string GroundPng => FileBase + "_ground.png";
        public string OverlayPng => FileBase + "_overlay.png";
        public bool IsPrimaryUnit => Secondary == null;
    }

    /// <summary>
    /// The single TileSet every exported map uses. Built from every primary/secondary
    /// pair found in layouts.json, so the source ids do not depend on the map filter.
    /// </summary>
    public class WorldTileset
    {
        public const int COLLISION_SOURCE_ID = 0;
        public const string TRES_NAME = "world_tileset.tres";
        public const string INDEX_NAME = "world_tileset.json";
        public const string ATLAS_DIR = "atlases";
        public const string COLLISION_PNG = "collision.png";

        private const int COLS = TilesetRenderer.ATLAS_COLUMNS;
        private const int MAX_ANIMATION_FRAMES = 256;
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public readonly List<TilesetUnit> Units = new List<TilesetUnit>();
        private readonly Dictionary<string, TilesetUnit> _primaryUnits = new Dictionary<string, TilesetUnit>();
        private readonly Dictionary<string, TilesetUnit> _secondaryUnits = new Dictionary<string, TilesetUnit>();

        // ─────────── Building ───────────

        /// <param name="pairs">Every (primary, secondary) label pair used by a layout</param>
        /// <param name="headerOrder">Tileset labels in headers.h order (keeps source ids stable)</param>
        /// <param name="load">Loads a tileset by label (null if missing)</param>
        public static WorldTileset Build(
            IEnumerable<KeyValuePair<string, string>> pairs,
            IList<string> headerOrder,
            Func<string, TilesetData> load)
        {
            var world = new WorldTileset();
            var pairList = pairs.ToList();

            Func<string, int> order = label =>
            {
                int i = headerOrder.IndexOf(label);
                return i < 0 ? int.MaxValue : i;
            };

            // Primary units
            foreach (var label in pairList.Select(p => p.Key).Distinct().OrderBy(order).ThenBy(l => l, StringComparer.Ordinal))
            {
                var data = load(label);
                if (data == null) continue;
                world.AddUnit(new TilesetUnit
                {
                    Name = data.Name,
                    FileBase = Converter.CamelToSnake(data.Name),
                    Own = data,
                    Primary = data
                }, label);
            }

            // Secondary units. A secondary used with several primaries gets one unit
            // per primary. The most used pairing keeps the plain name.
            var bySecondary = pairList
                .GroupBy(p => p.Value)
                .OrderBy(g => order(g.Key)).ThenBy(g => g.Key, StringComparer.Ordinal);

            foreach (var group in bySecondary)
            {
                var data = load(group.Key);
                if (data == null) continue;

                var partners = group
                    .GroupBy(p => p.Key)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => order(g.Key))
                    .Select(g => g.Key)
                    .ToList();

                for (int i = 0; i < partners.Count; i++)
                {
                    var primary = load(partners[i]);
                    if (primary == null) continue;

                    var unit = new TilesetUnit
                    {
                        Name = i == 0 ? data.Name : data.Name + "_" + primary.Name,
                        FileBase = i == 0
                            ? Converter.CamelToSnake(data.Name)
                            : Converter.CamelToSnake(data.Name) + "_" + Converter.CamelToSnake(primary.Name),
                        Own = data,
                        Primary = primary,
                        Secondary = data
                    };
                    world.AddUnit(unit, group.Key + "|" + partners[i]);
                }
            }

            return world;
        }

        private void AddUnit(TilesetUnit unit, string key)
        {
            int index = Units.Count;
            unit.GroundSourceId = 1 + index * 2;
            unit.OverlaySourceId = 2 + index * 2;
            Units.Add(unit);

            if (unit.IsPrimaryUnit) _primaryUnits[key] = unit;
            else _secondaryUnits[key] = unit;
        }

        /// <summary>Find the unit and tile a map cell should use.</summary>
        public bool TryResolve(string primaryLabel, string secondaryLabel, int metatileId,
            out TilesetUnit unit, out TileSlot slot)
        {
            slot = null;
            TilesetUnit secondary;
            _secondaryUnits.TryGetValue(secondaryLabel + "|" + primaryLabel, out secondary);

            // Borrowed copies win: those primary metatiles only look right with this secondary
            if (secondary != null && secondary.SlotsById.TryGetValue(metatileId, out slot))
            {
                unit = secondary;
                return true;
            }

            if (_primaryUnits.TryGetValue(primaryLabel, out unit) && unit.SlotsById.TryGetValue(metatileId, out slot))
                return true;

            unit = null;
            return false;
        }

        public TilesetUnit GetPrimaryUnit(string primaryLabel)
        {
            TilesetUnit unit;
            return _primaryUnits.TryGetValue(primaryLabel, out unit) ? unit : null;
        }

        public TilesetUnit GetSecondaryUnit(string primaryLabel, string secondaryLabel)
        {
            TilesetUnit unit;
            return _secondaryUnits.TryGetValue(secondaryLabel + "|" + primaryLabel, out unit) ? unit : null;
        }

        // ─────────── Rendering ───────────

        /// <summary>A metatile waiting to be placed in an atlas.</summary>
        private class Item
        {
            public int MetatileId;
            public ushort[] Metatile;
            public uint Attributes;
            public bool IsBorrowed;
            public int GridX = -1, GridY = -1;     // picture cell in the own grid
            public List<TileAnimation> Anims;      // animations this metatile reads
            public List<int[]> States;             // frame index per anim, one per Godot frame
            public List<int> Ticks;                // duration per state in GBA frames
            public bool IsAnimated => States.Count > 1;
        }

        /// <summary>Lay out and render every unit's Ground and Overlay atlas into atlasDir.</summary>
        public void Render(string atlasDir)
        {
            Directory.CreateDirectory(atlasDir);
            foreach (var unit in Units)
                RenderUnit(unit, atlasDir);
        }

        private void RenderUnit(TilesetUnit unit, string atlasDir)
        {
            var anims = new List<TileAnimation>(unit.Primary.Animations);
            if (unit.Secondary != null)
                anims.AddRange(unit.Secondary.Animations);

            var palettes = TilesetRenderer.MergePalettes(unit.Primary, unit.Secondary);

            // ── Collect items ──
            var own = new List<Item>();
            int ownCount = Math.Min(unit.Own.Metatiles.Length,
                unit.Own.IsSecondary ? 1024 - TilesetRenderer.NUM_METATILES_IN_PRIMARY : TilesetRenderer.NUM_METATILES_IN_PRIMARY);

            for (int i = 0; i < ownCount; i++)
            {
                var mt = unit.Own.Metatiles[i];
                if (unit.IsPrimaryUnit && TilesetRenderer.DependsOnSecondary(mt))
                    continue; // drawn by each secondary unit instead

                var item = MakeItem(unit.Own.FirstMetatileId + i, mt, unit.Own.GetAttribute(i), anims);
                item.GridX = i % COLS;
                item.GridY = i / COLS;
                own.Add(item);
            }

            var borrowed = new List<Item>();
            if (!unit.IsPrimaryUnit)
            {
                var pm = unit.Primary.Metatiles;
                for (int i = 0; i < pm.Length && i < TilesetRenderer.NUM_METATILES_IN_PRIMARY; i++)
                {
                    if (!TilesetRenderer.DependsOnSecondary(pm[i])) continue;
                    var item = MakeItem(i, pm[i], unit.Primary.GetAttribute(i), anims);
                    item.IsBorrowed = true;
                    borrowed.Add(item);
                }
            }

            // ── Place items ──
            int gridRows = (ownCount + COLS - 1) / COLS;
            var placements = new List<KeyValuePair<Item, Point>>();

            foreach (var item in own.Where(i => !i.IsAnimated))
                placements.Add(new KeyValuePair<Item, Point>(item, new Point(item.GridX, item.GridY)));

            int row = gridRows;
            var staticBorrowed = borrowed.Where(i => !i.IsAnimated).ToList();
            for (int k = 0; k < staticBorrowed.Count; k++)
                placements.Add(new KeyValuePair<Item, Point>(staticBorrowed[k], new Point(k % COLS, row + k / COLS)));
            row += (staticBorrowed.Count + COLS - 1) / COLS;

            // Animated tiles: frames run left to right; more than 8 frames wrap onto extra rows
            int col = 0;
            foreach (var item in own.Concat(borrowed).Where(i => i.IsAnimated))
            {
                int frames = item.States.Count;
                if (frames > COLS)
                {
                    if (col > 0) { row++; col = 0; }
                    placements.Add(new KeyValuePair<Item, Point>(item, new Point(0, row)));
                    row += (frames + COLS - 1) / COLS;
                }
                else
                {
                    if (col + frames > COLS) { row++; col = 0; }
                    placements.Add(new KeyValuePair<Item, Point>(item, new Point(col, row)));
                    col += frames;
                }
            }
            if (col > 0) row++;

            unit.Rows = Math.Max(row, 1);

            // ── Draw ──
            int width = COLS * 16;
            int height = unit.Rows * 16;
            var ground = new byte[width * height * 4];
            var overlay = new byte[width * height * 4];

            // Picture of each animated metatile in its usual grid cell (not a tile)
            foreach (var item in own.Where(i => i.IsAnimated))
                DrawState(ground, overlay, width, item.GridX * 16, item.GridY * 16, item, 0, unit, palettes);

            foreach (var p in placements)
            {
                var item = p.Key;
                int frames = item.States.Count;
                int columns = frames > COLS ? COLS : 0;

                for (int f = 0; f < frames; f++)
                {
                    int fx = p.Value.X + (columns > 0 ? f % columns : f);
                    int fy = p.Value.Y + (columns > 0 ? f / columns : 0);
                    DrawState(ground, overlay, width, fx * 16, fy * 16, item, f, unit, palettes);
                }

                var slot = new TileSlot
                {
                    MetatileId = item.MetatileId,
                    X = p.Value.X,
                    Y = p.Value.Y,
                    Attributes = item.Attributes,
                    IsBorrowed = item.IsBorrowed,
                    AnimationColumns = columns,
                    FrameDurations = item.IsAnimated
                        ? item.Ticks.Select(t => Math.Round(t / TilesetAnimationParser.GBA_FPS, 4)).ToArray()
                        : null
                };
                unit.Slots.Add(slot);
                unit.SlotsById[slot.MetatileId] = slot;
            }

            TilesetRenderer.WritePngRgba(ground, width, height, Path.Combine(atlasDir, unit.GroundPng));
            TilesetRenderer.WritePngRgba(overlay, width, height, Path.Combine(atlasDir, unit.OverlayPng));

            int animated = unit.Slots.Count(s => s.FrameDurations != null);
            int borrowedCount = unit.Slots.Count(s => s.IsBorrowed);
            Console.WriteLine($"  Tileset {unit.Name}: {unit.Slots.Count} tiles" +
                (animated > 0 ? $", {animated} animated" : "") +
                (borrowedCount > 0 ? $", {borrowedCount} borrowed from {unit.Primary.Name}" : ""));
        }

        private static void DrawState(byte[] ground, byte[] overlay, int width, int x, int y,
            Item item, int state, TilesetUnit unit, Color[][] palettes)
        {
            var frames = item.States[state];
            TilesetRenderer.DrawMetatileLayer(ground, width, x, y, item.Metatile, false,
                unit.Primary, unit.Secondary, palettes, item.Anims, frames);
            TilesetRenderer.DrawMetatileLayer(overlay, width, x, y, item.Metatile, true,
                unit.Primary, unit.Secondary, palettes, item.Anims, frames);
        }

        /// <summary>
        /// Work out which animations a metatile reads and build its frame timeline by
        /// stepping through the game's animation timers tick by tick.
        /// </summary>
        private static Item MakeItem(int metatileId, ushort[] metatile, uint attributes, List<TileAnimation> anims)
        {
            var used = anims.Where(a => a.FramePixels != null && a.FramePixels.All(f => f != null) &&
                                        metatile.Any(e => a.Covers(e & 0x3FF))).ToList();
            var item = new Item
            {
                MetatileId = metatileId,
                Metatile = metatile,
                Attributes = attributes,
                Anims = used,
                States = new List<int[]>(),
                Ticks = new List<int>()
            };

            if (used.Count == 0)
            {
                item.States.Add(new int[0]);
                return item;
            }

            int period = used.Select(a => a.CounterMax).Aggregate(1, Lcm);
            int[] previous = null;
            for (int tick = 0; tick < period; tick++)
            {
                var state = used.Select(a => a.FrameAt(tick)).ToArray();
                if (previous != null && state.SequenceEqual(previous))
                {
                    item.Ticks[item.Ticks.Count - 1]++;
                    continue;
                }
                item.States.Add(state);
                item.Ticks.Add(1);
                previous = state;
            }

            // The counter usually runs several loops of the animation (water: 640 ticks
            // for an 8 frame loop of 128 ticks). Keep only the shortest repeating loop.
            int n = item.States.Count;
            for (int p = 1; p < n; p++)
            {
                if (n % p != 0) continue;
                bool repeats = true;
                for (int i = p; i < n && repeats; i++)
                    repeats = item.Ticks[i] == item.Ticks[i - p] && item.States[i].SequenceEqual(item.States[i - p]);
                if (!repeats) continue;

                item.States.RemoveRange(p, n - p);
                item.Ticks.RemoveRange(p, n - p);
                break;
            }

            if (item.States.Count > MAX_ANIMATION_FRAMES)
            {
                Console.WriteLine($"  WARNING: metatile {metatileId} needs {item.States.Count} frames, keeping the first {MAX_ANIMATION_FRAMES}");
                item.States.RemoveRange(MAX_ANIMATION_FRAMES, item.States.Count - MAX_ANIMATION_FRAMES);
                item.Ticks.RemoveRange(MAX_ANIMATION_FRAMES, item.Ticks.Count - MAX_ANIMATION_FRAMES);
            }

            return item;
        }

        private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
        private static int Lcm(int a, int b) => a / Gcd(a, b) * b;

        // ─────────── TileSet .tres ───────────

        private static readonly string[] CustomDataLayers =
        {
            "metatile_id", "behavior", "terrain_type", "encounter_type", "layer_type"
        };

        /// <summary>
        /// Write the one TileSet resource. Source 0 is the collision overlay, then
        /// every unit has a Ground source and an Overlay source.
        /// Ground tiles carry the metatile attributes as custom data.
        /// </summary>
        public void WriteTres(string tresPath)
        {
            var sb = new StringBuilder(1 << 20);
            int extCount = 1 + Units.Count * 2;
            int subCount = 1 + Units.Count * 2;

            L(sb, $"[gd_resource type=\"TileSet\" load_steps={extCount + subCount + 1} format=3]");
            L(sb, "");

            L(sb, $"[ext_resource type=\"Texture2D\" path=\"{ATLAS_DIR}/{COLLISION_PNG}\" id=\"collision\"]");
            foreach (var unit in Units)
            {
                L(sb, $"[ext_resource type=\"Texture2D\" path=\"{ATLAS_DIR}/{unit.GroundPng}\" id=\"{unit.FileBase}_ground\"]");
                L(sb, $"[ext_resource type=\"Texture2D\" path=\"{ATLAS_DIR}/{unit.OverlayPng}\" id=\"{unit.FileBase}_overlay\"]");
            }
            L(sb, "");

            // Collision overlay: atlas X = collision (0..3), atlas Y = elevation (0..15)
            L(sb, $"[sub_resource type=\"TileSetAtlasSource\" id=\"TileSetAtlasSource_{COLLISION_SOURCE_ID}\"]");
            L(sb, "resource_name = \"Collisions\"");
            L(sb, "texture = ExtResource(\"collision\")");
            L(sb, "texture_region_size = Vector2i(16, 16)");
            for (int y = 0; y < TilesetRenderer.COLLISION_ROWS; y++)
                for (int x = 0; x < TilesetRenderer.COLLISION_COLS; x++)
                    L(sb, $"{x}:{y}/0 = 0");
            L(sb, "");

            foreach (var unit in Units)
            {
                WriteAtlasSource(sb, unit, unit.GroundSourceId, $"{unit.Name} Ground", $"{unit.FileBase}_ground", true);
                WriteAtlasSource(sb, unit, unit.OverlaySourceId, $"{unit.Name} Overlay", $"{unit.FileBase}_overlay", false);
            }

            L(sb, "[resource]");
            L(sb, "tile_size = Vector2i(16, 16)");
            for (int i = 0; i < CustomDataLayers.Length; i++)
            {
                L(sb, $"custom_data_layer_{i}/name = \"{CustomDataLayers[i]}\"");
                L(sb, $"custom_data_layer_{i}/type = 2"); // Variant.Type.INT
            }
            L(sb, $"sources/{COLLISION_SOURCE_ID} = SubResource(\"TileSetAtlasSource_{COLLISION_SOURCE_ID}\")");
            foreach (var unit in Units)
            {
                L(sb, $"sources/{unit.GroundSourceId} = SubResource(\"TileSetAtlasSource_{unit.GroundSourceId}\")");
                L(sb, $"sources/{unit.OverlaySourceId} = SubResource(\"TileSetAtlasSource_{unit.OverlaySourceId}\")");
            }

            File.WriteAllText(tresPath, sb.ToString(), Utf8NoBom);
        }

        private static void WriteAtlasSource(StringBuilder sb, TilesetUnit unit, int sourceId,
            string name, string textureId, bool withCustomData)
        {
            L(sb, $"[sub_resource type=\"TileSetAtlasSource\" id=\"TileSetAtlasSource_{sourceId}\"]");
            L(sb, $"resource_name = \"{name}\"");
            L(sb, $"texture = ExtResource(\"{textureId}\")");
            L(sb, "texture_region_size = Vector2i(16, 16)");

            foreach (var slot in unit.Slots)
            {
                string c = $"{slot.X}:{slot.Y}";
                if (slot.FrameDurations != null)
                {
                    if (slot.AnimationColumns > 0)
                        L(sb, $"{c}/animation_columns = {slot.AnimationColumns}");
                    L(sb, $"{c}/animation_frames_count = {slot.FrameDurations.Length}");
                    for (int f = 0; f < slot.FrameDurations.Length; f++)
                        L(sb, $"{c}/animation_frame_{f}/duration = {slot.FrameDurations[f].ToString("0.####", CultureInfo.InvariantCulture)}");
                }
                L(sb, $"{c}/0 = 0");

                if (!withCustomData) continue;

                uint a = slot.Attributes;
                int[] values =
                {
                    slot.MetatileId,
                    (int)(a & 0x1FF),
                    (int)((a >> 9) & 0x1F),
                    (int)((a >> 24) & 0x7),
                    (int)((a >> 29) & 0x3)
                };
                for (int i = 0; i < values.Length; i++)
                    if (values[i] != 0)
                        L(sb, $"{c}/0/custom_data_{i} = {values[i]}");
            }
            L(sb, "");
        }

        // ─────────── Index JSON ───────────

        /// <summary>
        /// Describe every source so game code can go from a metatile id to a tile,
        /// for example to swap a door or cut tree metatile at runtime.
        /// </summary>
        public void WriteIndexJson(string jsonPath)
        {
            var sb = new StringBuilder(1 << 18);
            sb.Append("{\n");
            sb.Append($"  \"tileset\": \"{TRES_NAME}\",\n");
            sb.Append("  \"tile_size\": 16,\n");
            sb.Append($"  \"collision_source\": {COLLISION_SOURCE_ID},\n");
            sb.Append("  \"custom_data_layers\": [");
            sb.Append(string.Join(", ", CustomDataLayers.Select(n => "\"" + n + "\"")));
            sb.Append("],\n");
            sb.Append("  \"units\": [\n");

            for (int u = 0; u < Units.Count; u++)
            {
                var unit = Units[u];
                sb.Append("    {\n");
                sb.Append($"      \"name\": \"{unit.Name}\",\n");
                sb.Append($"      \"label\": \"{unit.Own.Label}\",\n");
                sb.Append($"      \"kind\": \"{(unit.IsPrimaryUnit ? "primary" : "secondary")}\",\n");
                sb.Append($"      \"rendered_with\": {(unit.IsPrimaryUnit ? "null" : "\"" + unit.Primary.Label + "\"")},\n");
                sb.Append($"      \"ground_source\": {unit.GroundSourceId},\n");
                sb.Append($"      \"overlay_source\": {unit.OverlaySourceId},\n");
                sb.Append($"      \"ground_texture\": \"{ATLAS_DIR}/{unit.GroundPng}\",\n");
                sb.Append($"      \"overlay_texture\": \"{ATLAS_DIR}/{unit.OverlayPng}\",\n");
                sb.Append("      \"metatiles\": {");
                var ordered = unit.Slots.OrderBy(s => s.MetatileId).ToList();
                for (int i = 0; i < ordered.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append($"\"{ordered[i].MetatileId}\": [{ordered[i].X}, {ordered[i].Y}]");
                }
                sb.Append("},\n");
                sb.Append("      \"animated\": [");
                sb.Append(string.Join(", ", ordered.Where(s => s.FrameDurations != null).Select(s => s.MetatileId)));
                sb.Append("]\n");
                sb.Append(u < Units.Count - 1 ? "    },\n" : "    }\n");
            }

            sb.Append("  ]\n");
            sb.Append("}\n");
            File.WriteAllText(jsonPath, sb.ToString(), Utf8NoBom);
        }

        private static void L(StringBuilder sb, string line)
        {
            sb.Append(line);
            sb.Append('\n');
        }
    }
}
