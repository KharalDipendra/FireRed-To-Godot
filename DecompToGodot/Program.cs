using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DecompToGodot
{
    /// <summary>
    /// Converts a pokefirered (or similar Gen 3) decomp project's map data
    /// into Godot 4.3+ compatible scenes, tilesets, and metadata JSON files.
    ///
    /// Usage:
    ///   DecompToGodot.exe &lt;decomp-path&gt; &lt;output-path&gt; [map-filter]
    ///
    /// Arguments:
    ///   decomp-path   Root of the decomp project (e.g. pokefirered-master/)
    ///   output-path   Output directory (a Godot project folder or subfolder)
    ///   map-filter    Optional: comma-separated map names (e.g. "PalletTown,Route1")
    ///                 If omitted, all maps are exported.
    /// </summary>
    /// <summary>
    /// Entry point kept for standalone usage.
    /// </summary>
    public class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("DecompToGodot — Convert decomp project maps to Godot 4 scenes");
                Console.WriteLine();
                Console.WriteLine("Usage: DecompToGodot.exe <decomp-path> <output-path> [map-filter]");
                Console.WriteLine();
                Console.WriteLine("  <decomp-path>   Path to pokefirered-master (or similar)");
                Console.WriteLine("  <output-path>    Godot project output folder");
                Console.WriteLine("  [map-filter]     Optional: comma-separated map names");
                Console.WriteLine();
                Console.WriteLine("Example:");
                Console.WriteLine("  DecompToGodot.exe C:\\pokefirered-master C:\\MyGodotProject\\maps");
                return 1;
            }

            string decompPath = Path.GetFullPath(args[0]);
            string outputPath = Path.GetFullPath(args[1]);
            HashSet<string> mapFilter = null;

            if (args.Length >= 3 && !string.IsNullOrWhiteSpace(args[2]))
            {
                mapFilter = new HashSet<string>(
                    args[2].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(s => s.Trim()),
                    StringComparer.OrdinalIgnoreCase);
            }

            if (!Directory.Exists(decompPath))
            {
                Console.Error.WriteLine($"ERROR: Decomp project not found: {decompPath}");
                return 1;
            }

            try
            {
                var converter = new Converter(decompPath, outputPath, mapFilter);
                converter.Run();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FATAL: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Data classes
    // ──────────────────────────────────────────────────────────────────

    class LayoutInfo
    {
        public string Id;
        public string Name;
        public int Width, Height;
        public string PrimaryTileset;
        public string SecondaryTileset;
        public string BlockdataPath;
    }

    class TilesetCacheEntry
    {
        public string GroundAtlasPng;   // filename only
        public string OverlayAtlasPng;  // filename only
        public int TotalMetatilePositions;
        public uint[] PrimaryAttributes;
        public uint[] SecondaryAttributes;
    }

    // ──────────────────────────────────────────────────────────────────
    //  Converter
    // ──────────────────────────────────────────────────────────────────

    public class Converter
    {
        private readonly string _decompPath;
        private readonly string _outputPath;
        private readonly HashSet<string> _mapFilter;
        private readonly JavaScriptSerializer _json;

        /// <summary>When true, parse wild_encounters.json and write per-map spawn JSONs.</summary>
        public bool SaveSpawns { get; set; }

        // Tileset label → isSecondary (from headers.h)
        private Dictionary<string, bool> _tilesetIsSecondary;

        // Tileset label → label of tileset it borrows tiles from (cross-references)
        private Dictionary<string, string> _tilesetTilesFrom;
        // Tileset label → label of tileset it borrows palettes from
        private Dictionary<string, string> _tilesetPalettesFrom;

        // "primaryLabel|secondaryLabel" → cache entry
        private readonly Dictionary<string, TilesetCacheEntry> _tilesetCache
            = new Dictionary<string, TilesetCacheEntry>();

        // layout ID → LayoutInfo
        private Dictionary<string, LayoutInfo> _layouts;

        // MAP_NAME → encounter data (parsed from wild_encounters.json)
        private Dictionary<string, List<Dictionary<string, object>>> _wildEncounters;

        public Converter(string decompPath, string outputPath, HashSet<string> mapFilter)
        {
            _decompPath = decompPath;
            _outputPath = outputPath;
            _mapFilter = mapFilter;
            _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        }

        public void Run()
        {
            Console.WriteLine($"Decomp project: {_decompPath}");
            Console.WriteLine($"Output path:    {_outputPath}");

            var tilesDir = Path.Combine(_outputPath, "tiles");
            var tsDir = Path.Combine(_outputPath, "tilesets");
            var sceneDir = Path.Combine(_outputPath, "scenes");
            var dataDir = Path.Combine(_outputPath, "data");

            Directory.CreateDirectory(tilesDir);
            Directory.CreateDirectory(tsDir);
            Directory.CreateDirectory(sceneDir);
            Directory.CreateDirectory(dataDir);

            // Generate collision overlay atlas (shared by all maps)
            string collisionAtlasPath = Path.Combine(tilesDir, "collision_overlay.png");
            try
            {
                TilesetRenderer.GenerateCollisionAtlas(collisionAtlasPath);

                if (File.Exists(collisionAtlasPath))
                {
                    var fi = new FileInfo(collisionAtlasPath);
                    Console.WriteLine($"Collision overlay: {collisionAtlasPath} ({fi.Length} bytes)");
                }
                else
                {
                    Console.Error.WriteLine($"WARNING: Collision overlay was not created at {collisionAtlasPath}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"WARNING: Failed to generate collision overlay: {ex.Message}");
            }

            WriteProjectGodot(_outputPath);

            // ── Load decomp metadata ──
            _tilesetIsSecondary = ParseTilesetHeaders();
            _layouts = ParseLayouts();
            var allMapNames = CollectMapNames();

            // ── Parse wild encounters if requested ──
            if (SaveSpawns)
            {
                _wildEncounters = ParseWildEncounters();
                Console.WriteLine($"Parsed {_wildEncounters.Count} maps with wild encounters");
            }
            else
            {
                _wildEncounters = new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.OrdinalIgnoreCase);
            }

            Console.WriteLine($"Found {allMapNames.Count} maps, {_layouts.Count} layouts");

            if (_mapFilter != null)
            {
                allMapNames = allMapNames
                    .Where(n => _mapFilter.Contains(n))
                    .ToList();
                Console.WriteLine($"Filter applied: {allMapNames.Count} maps selected");
            }

            // ── Export each map ──
            int exported = 0;
            int failed = 0;
            string spawnsDir = null;

            if (SaveSpawns)
            {
                spawnsDir = Path.Combine(_outputPath, "spawns");
                Directory.CreateDirectory(spawnsDir);
            }

            foreach (var mapName in allMapNames)
            {
                try
                {
                    if (ExportMap(mapName, tilesDir, tsDir, sceneDir, dataDir))
                    {
                        exported++;

                        // Write per-map spawn JSON
                        if (SaveSpawns)
                            WriteSpawnJson(mapName, spawnsDir);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  WARNING: {mapName} — {ex.Message}");
                    failed++;
                }
            }

            // Write master spawns summary
            if (SaveSpawns && spawnsDir != null)
                WriteAllSpawnsSummary(spawnsDir);

            Console.WriteLine();
            Console.WriteLine($"Done! Exported {exported} maps ({failed} failed) → {_outputPath}");
            if (SaveSpawns)
                Console.WriteLine($"Spawn data saved to: {spawnsDir}");
            Console.WriteLine("Open this folder as a Godot 4.3+ project.");
        }

        // ─────────── Map export ───────────

        private bool ExportMap(
            string mapName,
            string tilesDir, string tsDir,
            string sceneDir, string dataDir)
        {
            // Read map.json
            string mapJsonPath = Path.Combine(_decompPath, "data", "maps", mapName, "map.json");
            if (!File.Exists(mapJsonPath))
            {
                Console.WriteLine($"  SKIP: {mapName} — no map.json");
                return false;
            }

            var mapJson = _json.Deserialize<Dictionary<string, object>>(
                File.ReadAllText(mapJsonPath));

            // Resolve layout
            string layoutId = GetString(mapJson, "layout");
            if (layoutId == null || !_layouts.ContainsKey(layoutId))
            {
                Console.WriteLine($"  SKIP: {mapName} — layout '{layoutId}' not found");
                return false;
            }

            var layout = _layouts[layoutId];

            if (layout.PrimaryTileset == "NULL" || layout.SecondaryTileset == "NULL")
            {
                Console.WriteLine($"  SKIP: {mapName} — NULL tileset");
                return false;
            }

            // ── Read block data ──
            string blockDataPath = Path.Combine(_decompPath, layout.BlockdataPath);
            if (!File.Exists(blockDataPath))
            {
                Console.WriteLine($"  SKIP: {mapName} — blockdata not found: {layout.BlockdataPath}");
                return false;
            }

            var blockData = ReadBlockData(blockDataPath);
            int expectedBlocks = layout.Width * layout.Height;

            if (blockData.Length < expectedBlocks)
            {
                Console.WriteLine($"  SKIP: {mapName} — blockdata too small ({blockData.Length} < {expectedBlocks})");
                return false;
            }

            // ── Ensure tileset atlas is rendered ──
            string cacheKey = layout.PrimaryTileset + "|" + layout.SecondaryTileset;
            TilesetCacheEntry tsCache;

            if (!_tilesetCache.TryGetValue(cacheKey, out tsCache))
            {
                tsCache = RenderTilesetAtlas(
                    layout.PrimaryTileset, layout.SecondaryTileset, tilesDir);

                if (tsCache == null)
                {
                    Console.WriteLine($"  SKIP: {mapName} — failed to render tileset atlas");
                    return false;
                }

                _tilesetCache[cacheKey] = tsCache;
            }

            // ── Write .tres ──
            string safeName = mapName;
            string tresFile = $"{safeName}_tileset.tres";
            string groundRelPath = "../tiles/" + tsCache.GroundAtlasPng;
            string overlayRelPath = "../tiles/" + tsCache.OverlayAtlasPng;
            string collisionRelPath = "../tiles/collision_overlay.png";

            GodotWriter.WriteTileSetResource(
                Path.Combine(tsDir, tresFile),
                groundRelPath, overlayRelPath, collisionRelPath,
                tsCache.TotalMetatilePositions, true);

            // ── Write .tscn ──
            string tilesetRelPath = "../tilesets/" + tresFile;
            GodotWriter.WriteMapScene(
                Path.Combine(sceneDir, $"{safeName}.tscn"),
                tilesetRelPath, mapName, blockData,
                layout.Width, layout.Height, true);

            // ── Write .json ──
            GodotWriter.WriteMapDataJson(
                Path.Combine(dataDir, $"{safeName}.json"),
                mapName, mapJson, blockData,
                layout.Width, layout.Height,
                tsCache.PrimaryAttributes,
                tsCache.SecondaryAttributes);

            Console.WriteLine($"  OK: {mapName} ({layout.Width}×{layout.Height})");
            return true;
        }

        // ─────────── Tileset rendering ───────────

        private TilesetCacheEntry RenderTilesetAtlas(
            string primaryLabel, string secondaryLabel,
            string tilesDir)
        {
            string primaryDir = ResolveTilesetPath(primaryLabel);
            string secondaryDir = ResolveTilesetPath(secondaryLabel);

            if (primaryDir == null || secondaryDir == null)
            {
                Console.WriteLine($"  ERROR: Cannot resolve tileset paths: {primaryLabel} / {secondaryLabel}");
                return null;
            }

            // ── Resolve cross-referenced tiles/palettes directories ──
            // Some tilesets (e.g. SilphCo) borrow tiles.png and/or palettes from
            // another tileset (e.g. Condominiums).  headers.h defines these via
            //   .tiles = gTilesetTiles_Condominiums
            //   .palettes = gTilesetPalettes_Condominiums
            string secondaryTilesDir = null;
            string secondaryPalettesDir = null;

            if (_tilesetTilesFrom.ContainsKey(secondaryLabel))
            {
                string tilesFromLabel = _tilesetTilesFrom[secondaryLabel];
                secondaryTilesDir = ResolveTilesetPath(tilesFromLabel);
                if (secondaryTilesDir != null)
                    Console.WriteLine($"  Using tiles from {tilesFromLabel} → {secondaryTilesDir}");
                else
                    Console.WriteLine($"  WARNING: Cannot resolve tiles cross-ref: {tilesFromLabel}");
            }

            if (_tilesetPalettesFrom.ContainsKey(secondaryLabel))
            {
                string palFromLabel = _tilesetPalettesFrom[secondaryLabel];
                secondaryPalettesDir = ResolveTilesetPath(palFromLabel);
                if (secondaryPalettesDir != null)
                    Console.WriteLine($"  Using palettes from {palFromLabel} → {secondaryPalettesDir}");
                else
                    Console.WriteLine($"  WARNING: Cannot resolve palette cross-ref: {palFromLabel}");
            }

            // Build a safe filename from the tileset combo
            string comboName = MakeSafe(StripPrefix(primaryLabel)) + "_" + MakeSafe(StripPrefix(secondaryLabel));

            string groundPng = comboName + "_ground.png";
            string overlayPng = comboName + "_overlay.png";

            Console.WriteLine($"  Rendering tileset: {comboName}");

            int totalPositions = TilesetRenderer.RenderAtlases(
                primaryDir, secondaryDir,
                Path.Combine(tilesDir, groundPng),
                Path.Combine(tilesDir, overlayPng),
                secondaryTilesDir, secondaryPalettesDir);

            // Load metatile attributes for JSON export
            var primaryAttrs = TilesetRenderer.ReadMetatileAttributes(
                Path.Combine(primaryDir, "metatile_attributes.bin"));
            var secondaryAttrs = TilesetRenderer.ReadMetatileAttributes(
                Path.Combine(secondaryDir, "metatile_attributes.bin"));

            return new TilesetCacheEntry
            {
                GroundAtlasPng = groundPng,
                OverlayAtlasPng = overlayPng,
                TotalMetatilePositions = totalPositions,
                PrimaryAttributes = primaryAttrs,
                SecondaryAttributes = secondaryAttrs
            };
        }

        // ─────────── Decomp data parsing ───────────

        /// <summary>
        /// Parse data/layouts/layouts.json → dictionary of layout ID → LayoutInfo
        /// </summary>
        private Dictionary<string, LayoutInfo> ParseLayouts()
        {
            var result = new Dictionary<string, LayoutInfo>();
            string path = Path.Combine(_decompPath, "data", "layouts", "layouts.json");

            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"WARNING: layouts.json not found: {path}");
                return result;
            }

            var root = _json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            if (!root.ContainsKey("layouts")) return result;

            var layouts = root["layouts"] as ArrayList;
            if (layouts == null) return result;

            foreach (Dictionary<string, object> lo in layouts)
            {
                string id = GetString(lo, "id");
                if (id == null) continue;

                result[id] = new LayoutInfo
                {
                    Id = id,
                    Name = GetString(lo, "name") ?? id,
                    Width = GetInt(lo, "width"),
                    Height = GetInt(lo, "height"),
                    PrimaryTileset = GetString(lo, "primary_tileset") ?? "NULL",
                    SecondaryTileset = GetString(lo, "secondary_tileset") ?? "NULL",
                    BlockdataPath = GetString(lo, "blockdata_filepath") ?? ""
                };
            }

            return result;
        }

        /// <summary>
        /// Parse data/maps/map_groups.json → flat list of all map directory names.
        /// </summary>
        private List<string> CollectMapNames()
        {
            var names = new List<string>();
            string path = Path.Combine(_decompPath, "data", "maps", "map_groups.json");

            if (!File.Exists(path))
            {
                // Fallback: scan data/maps/ for directories containing map.json
                Console.WriteLine("WARNING: map_groups.json not found — scanning directories");
                var mapsDir = Path.Combine(_decompPath, "data", "maps");
                if (Directory.Exists(mapsDir))
                {
                    foreach (var dir in Directory.GetDirectories(mapsDir))
                    {
                        if (File.Exists(Path.Combine(dir, "map.json")))
                            names.Add(Path.GetFileName(dir));
                    }
                }
                return names;
            }

            var root = _json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));

            // Get group_order to iterate groups in order
            var groupOrder = root.ContainsKey("group_order") ? root["group_order"] as ArrayList : null;

            if (groupOrder != null)
            {
                foreach (string groupName in groupOrder)
                {
                    if (!root.ContainsKey(groupName)) continue;
                    var mapList = root[groupName] as ArrayList;
                    if (mapList == null) continue;

                    foreach (string mapName in mapList)
                    {
                        names.Add(mapName);
                    }
                }
            }
            else
            {
                // Fallback: iterate all keys that are arrays
                foreach (var kv in root)
                {
                    if (kv.Key == "group_order") continue;
                    var list = kv.Value as ArrayList;
                    if (list == null) continue;
                    foreach (string mapName in list)
                        names.Add(mapName);
                }
            }

            return names;
        }

        /// <summary>
        /// Parse src/data/tilesets/headers.h to determine isSecondary for each tileset.
        /// </summary>
        private Dictionary<string, bool> ParseTilesetHeaders()
        {
            var result = new Dictionary<string, bool>();
            _tilesetTilesFrom = new Dictionary<string, string>();
            _tilesetPalettesFrom = new Dictionary<string, string>();

            string headersPath = Path.Combine(_decompPath, "src", "data", "tilesets", "headers.h");
            if (!File.Exists(headersPath))
            {
                Console.WriteLine("WARNING: headers.h not found — using directory scanning for tileset resolution");
                return result;
            }

            var lines = File.ReadAllLines(headersPath);
            string currentLabel = null;
            string currentSuffix = null; // suffix after "gTileset_"

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();

                // Match: const struct Tileset gTileset_XXX =
                var labelMatch = Regex.Match(line, @"const\s+struct\s+Tileset\s+(gTileset_(\w+))");
                if (labelMatch.Success)
                {
                    currentLabel = labelMatch.Groups[1].Value;
                    currentSuffix = labelMatch.Groups[2].Value;
                    continue;
                }

                if (currentLabel != null)
                {
                    // Match: .isSecondary = TRUE/FALSE
                    var secMatch = Regex.Match(line, @"\.isSecondary\s*=\s*(TRUE|FALSE)");
                    if (secMatch.Success)
                    {
                        result[currentLabel] = secMatch.Groups[1].Value == "TRUE";
                    }

                    // Match: .tiles = gTilesetTiles_XXX  (detect cross-references)
                    var tilesMatch = Regex.Match(line, @"\.tiles\s*=\s*gTilesetTiles_(\w+)");
                    if (tilesMatch.Success)
                    {
                        string tilesFrom = tilesMatch.Groups[1].Value;
                        if (!string.Equals(tilesFrom, currentSuffix, StringComparison.OrdinalIgnoreCase))
                        {
                            _tilesetTilesFrom[currentLabel] = "gTileset_" + tilesFrom;
                        }
                    }

                    // Match: .palettes = gTilesetPalettes_XXX  (detect cross-references)
                    var palMatch = Regex.Match(line, @"\.palettes\s*=\s*gTilesetPalettes_(\w+)");
                    if (palMatch.Success)
                    {
                        string palFrom = palMatch.Groups[1].Value;
                        if (!string.Equals(palFrom, currentSuffix, StringComparison.OrdinalIgnoreCase))
                        {
                            _tilesetPalettesFrom[currentLabel] = "gTileset_" + palFrom;
                        }
                    }

                    // End of struct
                    if (line.StartsWith("}"))
                    {
                        currentLabel = null;
                        currentSuffix = null;
                    }
                }
            }

            // Log any detected cross-references
            foreach (var kvp in _tilesetTilesFrom)
                Console.WriteLine($"  Tileset cross-ref: {kvp.Key} borrows tiles from {kvp.Value}");
            foreach (var kvp in _tilesetPalettesFrom)
                Console.WriteLine($"  Tileset cross-ref: {kvp.Key} borrows palettes from {kvp.Value}");

            return result;
        }

        /// <summary>
        /// Resolve a tileset label (e.g. "gTileset_PalletTown") to its filesystem
        /// directory (e.g. "data/tilesets/secondary/pallet_town").
        /// </summary>
        private string ResolveTilesetPath(string label)
        {
            if (string.IsNullOrEmpty(label) || label == "NULL") return null;

            // Strip gTileset_ prefix → "PalletTown"
            string suffix = StripPrefix(label);

            // CamelCase → snake_case: "PalletTown" → "pallet_town"
            string dirName = CamelToSnake(suffix);

            // Determine primary/secondary
            bool isSecondary = true; // default assumption
            if (_tilesetIsSecondary.ContainsKey(label))
                isSecondary = _tilesetIsSecondary[label];

            string category = isSecondary ? "secondary" : "primary";
            string fullPath = Path.Combine(_decompPath, "data", "tilesets", category, dirName);

            if (Directory.Exists(fullPath))
                return fullPath;

            // Fallback: try the other category
            string altCategory = isSecondary ? "primary" : "secondary";
            string altPath = Path.Combine(_decompPath, "data", "tilesets", altCategory, dirName);
            if (Directory.Exists(altPath))
                return altPath;

            // Fallback: scan both directories for a matching name
            foreach (var cat in new[] { "primary", "secondary" })
            {
                string searchDir = Path.Combine(_decompPath, "data", "tilesets", cat);
                if (!Directory.Exists(searchDir)) continue;

                foreach (var dir in Directory.GetDirectories(searchDir))
                {
                    string name = Path.GetFileName(dir);
                    if (string.Equals(name, dirName, StringComparison.OrdinalIgnoreCase))
                        return dir;
                }
            }

            Console.WriteLine($"  WARNING: Cannot find tileset directory for '{label}' (tried '{dirName}')");
            return null;
        }

        // ─────────── Block data ───────────

        /// <summary>
        /// Read a map.bin file → ushort[] (uint16 LE per block).
        /// Each block: metatile ID (bits 0-9), collision (bits 10-11), elevation (bits 12-15).
        /// </summary>
        private static ushort[] ReadBlockData(string path)
        {
            var raw = File.ReadAllBytes(path);
            int count = raw.Length / 2;
            var blocks = new ushort[count];

            for (int i = 0; i < count; i++)
                blocks[i] = (ushort)(raw[i * 2] | (raw[i * 2 + 1] << 8));

            return blocks;
        }

        // ─────────── Wild Encounter / Spawn Parsing ───────────

        /// <summary>
        /// Parse decomp's wild_encounters.json → map name → list of encounter groups.
        /// Map names are converted from "MAP_PALLET_TOWN" → "PalletTown" to match map.json names.
        /// </summary>
        private Dictionary<string, List<Dictionary<string, object>>> ParseWildEncounters()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.OrdinalIgnoreCase);

            string path = Path.Combine(_decompPath, "src", "data", "wild_encounters.json");
            if (!File.Exists(path))
            {
                Console.WriteLine("WARNING: wild_encounters.json not found — spawn export skipped");
                return result;
            }

            var root = _json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            if (!root.ContainsKey("wild_encounter_groups")) return result;

            var groups = root["wild_encounter_groups"] as ArrayList;
            if (groups == null) return result;

            // Parse the encounter rate fields (for slot probabilities)
            var encounterRates = new Dictionary<string, ArrayList>();

            foreach (Dictionary<string, object> group in groups)
            {
                if (group.ContainsKey("fields"))
                {
                    var fields = group["fields"] as ArrayList;
                    if (fields != null)
                    {
                        foreach (Dictionary<string, object> field in fields)
                        {
                            string type = GetString(field, "type");
                            if (type != null && field.ContainsKey("encounter_rates"))
                                encounterRates[type] = field["encounter_rates"] as ArrayList;
                        }
                    }
                }

                if (!group.ContainsKey("encounters")) continue;
                var encounters = group["encounters"] as ArrayList;
                if (encounters == null) continue;

                foreach (Dictionary<string, object> enc in encounters)
                {
                    string mapConstant = GetString(enc, "map");
                    if (mapConstant == null) continue;

                    string mapName = MapConstantToName(mapConstant);

                    if (!result.ContainsKey(mapName))
                        result[mapName] = new List<Dictionary<string, object>>();

                    result[mapName].Add(enc);
                }
            }

            return result;
        }

        /// <summary>
        /// Convert "MAP_PALLET_TOWN" → "PalletTown" (matching decomp directory names).
        /// </summary>
        private static string MapConstantToName(string mapConstant)
        {
            if (mapConstant.StartsWith("MAP_"))
                mapConstant = mapConstant.Substring(4);

            // Convert UPPER_SNAKE to PascalCase: "PALLET_TOWN" → "PalletTown"
            var parts = mapConstant.Split('_');
            var sb = new StringBuilder();
            foreach (var part in parts)
            {
                if (part.Length == 0) continue;
                sb.Append(char.ToUpper(part[0]));
                if (part.Length > 1) sb.Append(part.Substring(1).ToLower());
            }
            return sb.ToString();
        }

        /// <summary>
        /// Write a per-map spawns JSON listing every Pokemon that can appear.
        /// </summary>
        private void WriteSpawnJson(string mapName, string spawnsDir)
        {
            List<Dictionary<string, object>> encounters;
            if (!_wildEncounters.TryGetValue(mapName, out encounters) || encounters.Count == 0)
                return; // No encounters for this map

            var sb = new StringBuilder(2048);
            sb.Append("{\n");
            sb.Append("  \"map\": \""); sb.Append(mapName); sb.Append("\",\n");

            var encounterTypes = new[] { "land_mons", "water_mons", "rock_smash_mons", "fishing_mons" };
            var friendlyNames = new[] { "grass", "water", "rock_smash", "fishing" };

            bool first = true;

            for (int t = 0; t < encounterTypes.Length; t++)
            {
                foreach (var enc in encounters)
                {
                    if (!enc.ContainsKey(encounterTypes[t])) continue;

                    var section = enc[encounterTypes[t]] as Dictionary<string, object>;
                    if (section == null) continue;

                    if (!first) sb.Append(",\n");
                    first = false;

                    int rate = GetInt(section, "encounter_rate");
                    sb.Append($"  \"{friendlyNames[t]}\": {{\n");
                    sb.Append($"    \"encounter_rate\": {rate},\n");
                    sb.Append("    \"pokemon\": [\n");

                    var mons = section.ContainsKey("mons") ? section["mons"] as ArrayList : null;
                    if (mons != null)
                    {
                        for (int i = 0; i < mons.Count; i++)
                        {
                            var mon = mons[i] as Dictionary<string, object>;
                            if (mon == null) continue;

                            string species = GetString(mon, "species") ?? "UNKNOWN";
                            if (species.StartsWith("SPECIES_"))
                                species = species.Substring(8);

                            int minLv = GetInt(mon, "min_level");
                            int maxLv = GetInt(mon, "max_level");

                            sb.Append($"      {{ \"species\": \"{species}\", \"minLevel\": {minLv}, \"maxLevel\": {maxLv}, \"slot\": {i} }}");
                            sb.Append(i < mons.Count - 1 ? ",\n" : "\n");
                        }
                    }

                    sb.Append("    ]\n");
                    sb.Append("  }");
                }
            }

            sb.Append("\n}\n");
            File.WriteAllText(
                Path.Combine(spawnsDir, $"{mapName}.json"),
                sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// Write an all_spawns.json summary listing every map and the distinct species found there.
        /// </summary>
        private void WriteAllSpawnsSummary(string spawnsDir)
        {
            var sb = new StringBuilder(16384);
            sb.Append("[\n");
            bool firstMap = true;

            foreach (var kvp in _wildEncounters)
            {
                string mapName = kvp.Key;
                var encounters = kvp.Value;

                // Collect unique species across all encounter types
                var speciesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var encounterTypes = new[] { "land_mons", "water_mons", "rock_smash_mons", "fishing_mons" };

                foreach (var enc in encounters)
                {
                    foreach (var etype in encounterTypes)
                    {
                        if (!enc.ContainsKey(etype)) continue;
                        var section = enc[etype] as Dictionary<string, object>;
                        if (section == null) continue;
                        var mons = section.ContainsKey("mons") ? section["mons"] as ArrayList : null;
                        if (mons == null) continue;

                        foreach (Dictionary<string, object> mon in mons)
                        {
                            string species = GetString(mon, "species") ?? "";
                            if (species.StartsWith("SPECIES_"))
                                species = species.Substring(8);
                            if (species.Length > 0)
                                speciesSet.Add(species);
                        }
                    }
                }

                if (speciesSet.Count == 0) continue;

                if (!firstMap) sb.Append(",\n");
                firstMap = false;

                sb.Append("  { \"map\": \""); sb.Append(mapName); sb.Append("\", \"pokemon\": [");
                bool firstSpecies = true;
                foreach (var sp in speciesSet)
                {
                    if (!firstSpecies) sb.Append(", ");
                    firstSpecies = false;
                    sb.Append('"'); sb.Append(sp); sb.Append('"');
                }
                sb.Append("] }");
            }

            sb.Append("\n]\n");
            File.WriteAllText(
                Path.Combine(spawnsDir, "all_spawns.json"),
                sb.ToString(), new UTF8Encoding(false));

            Console.WriteLine($"  Spawn summary: {spawnsDir}\\all_spawns.json");
        }

        // ─────────── project.godot ───────────

        private static void WriteProjectGodot(string outputRoot)
        {
            // If an ancestor directory has project.godot, skip
            var dir = Directory.GetParent(outputRoot);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "project.godot")))
                    return;
                dir = dir.Parent;
            }

            string projFile = Path.Combine(outputRoot, "project.godot");
            if (File.Exists(projFile)) return;

            var sb = new StringBuilder();
            sb.Append("; Godot project generated by DecompToGodot\n");
            sb.Append("\n");
            sb.Append("config_version=5\n");
            sb.Append("\n");
            sb.Append("[application]\n");
            sb.Append("\n");
            sb.Append("config/name=\"Decomp Maps\"\n");
            sb.Append("config/features=PackedStringArray(\"4.3\")\n");
            sb.Append("\n");
            sb.Append("[rendering]\n");
            sb.Append("\n");
            sb.Append("textures/canvas_textures/default_texture_filter=0\n");

            File.WriteAllText(projFile, sb.ToString(), new UTF8Encoding(false));
        }

        // ─────────── String helpers ───────────

        private static string StripPrefix(string label)
        {
            if (label.StartsWith("gTileset_"))
                return label.Substring("gTileset_".Length);
            return label;
        }

        /// <summary>Convert CamelCase to snake_case: "PalletTown" → "pallet_town", "GenericBuilding1" → "generic_building_1"</summary>
        private static string CamelToSnake(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var sb = new StringBuilder();
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (char.IsUpper(c))
                {
                    // Insert underscore before uppercase if not at start
                    // and previous char is lowercase or digit
                    if (i > 0 && (char.IsLower(input[i - 1]) || char.IsDigit(input[i - 1]) ||
                        (i + 1 < input.Length && char.IsLower(input[i + 1]))))
                    {
                        sb.Append('_');
                    }
                    sb.Append(char.ToLower(c));
                }
                else if (char.IsDigit(c))
                {
                    // Insert underscore before digits if previous char is a letter
                    if (i > 0 && char.IsLetter(input[i - 1]))
                    {
                        sb.Append('_');
                    }
                    sb.Append(c);
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string MakeSafe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unknown";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
            }
            return sb.Length > 0 ? sb.ToString() : "unknown";
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            if (dict.ContainsKey(key))
                return dict[key]?.ToString();
            return null;
        }

        private static int GetInt(Dictionary<string, object> dict, string key, int defaultVal = 0)
        {
            if (dict.ContainsKey(key))
            {
                var val = dict[key];
                if (val is int i) return i;
                if (val is long l) return (int)l;
                if (val is decimal d) return (int)d;
                int parsed;
                if (int.TryParse(val?.ToString(), out parsed)) return parsed;
            }
            return defaultVal;
        }
    }
}
