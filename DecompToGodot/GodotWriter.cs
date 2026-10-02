using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DecompToGodot
{
    /// <summary>
    /// Writes Godot 4.3+ compatible files:
    ///   .tscn  — Scene with TileMapLayer nodes using the shared world TileSet
    ///   .json  — Per-map event data (NPCs, warps, triggers, signs, metatile attributes)
    /// </summary>
    public static class GodotWriter
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        // ═══════════ Map Scene .tscn ═══════════

        /// <summary>
        /// Maps a metatile id to its tile in the shared TileSet.
        /// Ground and Overlay use the same atlas coordinates in their own source.
        /// </summary>
        public delegate bool MetatileResolver(
            int metatileId, out int groundSource, out int overlaySource, out int atlasX, out int atlasY);

        /// <summary>
        /// Write a Godot scene (.tscn) with Ground, Overlay and Collisions TileMapLayer nodes,
        /// all using the shared world TileSet.
        /// </summary>
        /// <param name="tscnPath">Output file path</param>
        /// <param name="tilesetRelPath">Relative path to the shared .tres from the scenes/ dir</param>
        /// <param name="mapName">Display name for root node</param>
        /// <param name="blockData">Block data (uint16 per cell: metatile ID in bits 0-9)</param>
        /// <param name="mapWidth">Map width in blocks</param>
        /// <param name="mapHeight">Map height in blocks</param>
        /// <param name="resolve">Finds the tile for a metatile id</param>
        /// <returns>Number of cells whose metatile could not be resolved</returns>
        public static int WriteMapScene(
            string tscnPath, string tilesetRelPath,
            string mapName, ushort[] blockData,
            int mapWidth, int mapHeight,
            MetatileResolver resolve)
        {
            string label = SanitizeNodeName(mapName);

            var ground = new List<int[]>(mapWidth * mapHeight);
            var overlay = new List<int[]>(mapWidth * mapHeight);
            var collisions = new List<int[]>(mapWidth * mapHeight);
            int missing = 0;

            for (int y = 0; y < mapHeight; y++)
            {
                for (int x = 0; x < mapWidth; x++)
                {
                    int block = blockData[y * mapWidth + x];
                    int collision = (block >> 10) & 0x03;
                    int elevation = (block >> 12) & 0x0F;

                    // Collision overlay: atlas X = collision column, atlas Y = elevation row
                    collisions.Add(new[] { x, y, WorldTileset.COLLISION_SOURCE_ID, collision, elevation });

                    int groundSource, overlaySource, ax, ay;
                    if (!resolve(block & 0x03FF, out groundSource, out overlaySource, out ax, out ay))
                    {
                        missing++;
                        continue;
                    }
                    ground.Add(new[] { x, y, groundSource, ax, ay });
                    overlay.Add(new[] { x, y, overlaySource, ax, ay });
                }
            }

            var sb = new StringBuilder(mapWidth * mapHeight * 150);

            L(sb, "[gd_scene load_steps=2 format=3]");
            L(sb, "");
            L(sb, $"[ext_resource type=\"TileSet\" path=\"{tilesetRelPath}\" id=\"1\"]");
            L(sb, "");

            // Root node
            L(sb, $"[node name=\"{label}\" type=\"Node2D\"]");
            L(sb, "");

            L(sb, "[node name=\"Ground\" type=\"TileMapLayer\" parent=\".\"]");
            L(sb, "tile_set = ExtResource(\"1\")");
            L(sb, "texture_filter = 1");
            sb.Append("tile_map_data = ");
            WriteTileMapData(sb, ground);
            sb.Append('\n');
            L(sb, "");

            L(sb, "[node name=\"Overlay\" type=\"TileMapLayer\" parent=\".\"]");
            L(sb, "tile_set = ExtResource(\"1\")");
            L(sb, "texture_filter = 1");
            sb.Append("tile_map_data = ");
            WriteTileMapData(sb, overlay);
            sb.Append('\n');
            L(sb, "");

            L(sb, "[node name=\"Collisions\" type=\"TileMapLayer\" parent=\".\"]");
            L(sb, "tile_set = ExtResource(\"1\")");
            L(sb, "modulate = Color(1, 1, 1, 0.3)");
            L(sb, "texture_filter = 1");
            L(sb, "visible = false");
            sb.Append("tile_map_data = ");
            WriteTileMapData(sb, collisions);
            sb.Append('\n');

            File.WriteAllText(tscnPath, sb.ToString(), Utf8NoBom);
            return missing;
        }

        /// <summary>
        /// Godot 4.3+ TileMapLayer tile_map_data as a PackedByteArray.
        /// Format: 2-byte header (version=0) + 12 bytes per cell.
        /// Each cell: x(2), y(2), sourceId(2), atlasX(2), atlasY(2), altTile(2).
        /// </summary>
        private static void WriteTileMapData(StringBuilder sb, List<int[]> cells)
        {
            const ushort TILE_MAP_DATA_FORMAT = 0;

            var buf = new byte[2 + cells.Count * 12];
            Put16(buf, 0, TILE_MAP_DATA_FORMAT);

            int off = 2;
            foreach (var c in cells)
            {
                Put16(buf, off + 0, (ushort)c[0]);
                Put16(buf, off + 2, (ushort)c[1]);
                Put16(buf, off + 4, (ushort)c[2]);
                Put16(buf, off + 6, (ushort)c[3]);
                Put16(buf, off + 8, (ushort)c[4]);
                Put16(buf, off + 10, 0); // alt_tile
                off += 12;
            }

            sb.Append("PackedByteArray(");
            for (int i = 0; i < buf.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(buf[i]);
            }
            sb.Append(')');
        }

        // ═══════════ Map Data JSON ═══════════

        /// <summary>
        /// Write per-map JSON containing events, connections, and metatile metadata.
        /// Sources event data directly from the decomp map.json.
        /// </summary>
        public static void WriteMapDataJson(
            string jsonPath,
            string mapName,
            Dictionary<string, object> mapJson,
            ushort[] blockData,
            int mapWidth, int mapHeight,
            uint[] primaryAttributes,
            uint[] secondaryAttributes,
            TilesetUnit primaryUnit,
            TilesetUnit secondaryUnit)
        {
            var sb = new StringBuilder(8192);
            sb.Append("{\n");

            // ── Map header info ──
            sb.Append("  \"name\": "); JStr(sb, mapName); sb.Append(",\n");
            sb.Append($"  \"width\": {mapWidth},\n");
            sb.Append($"  \"height\": {mapHeight},\n");

            WriteJsonField(sb, "  ", "id", mapJson);
            WriteJsonField(sb, "  ", "music", mapJson);
            WriteJsonField(sb, "  ", "weather", mapJson);
            WriteJsonField(sb, "  ", "map_type", mapJson);
            WriteJsonField(sb, "  ", "show_map_name", mapJson);
            WriteJsonField(sb, "  ", "battle_scene", mapJson);

            // ── Shared tileset sources used by this map ──
            sb.Append("  \"tileset\": {\n");
            sb.Append("    \"primary\": "); JStr(sb, primaryUnit.Own.Label); sb.Append(",\n");
            sb.Append("    \"secondary\": "); JStr(sb, secondaryUnit.Own.Label); sb.Append(",\n");
            sb.Append("    \"primary_unit\": "); JStr(sb, primaryUnit.Name); sb.Append(",\n");
            sb.Append("    \"secondary_unit\": "); JStr(sb, secondaryUnit.Name); sb.Append(",\n");
            sb.Append($"    \"primary_sources\": [{primaryUnit.GroundSourceId}, {primaryUnit.OverlaySourceId}],\n");
            sb.Append($"    \"secondary_sources\": [{secondaryUnit.GroundSourceId}, {secondaryUnit.OverlaySourceId}]\n");
            sb.Append("  },\n");

            // ── Connections ──
            sb.Append("  \"connections\": ");
            if (mapJson.ContainsKey("connections"))
                WriteJsonValue(sb, mapJson["connections"], "    ");
            else
                sb.Append("[]");
            sb.Append(",\n");

            // ── NPC / Object events ──
            sb.Append("  \"object_events\": ");
            WriteEventArray(sb, mapJson, "object_events");
            sb.Append(",\n");

            // ── Warp events ──
            sb.Append("  \"warp_events\": ");
            WriteEventArray(sb, mapJson, "warp_events");
            sb.Append(",\n");

            // ── Trigger / Coord events ──
            sb.Append("  \"coord_events\": ");
            WriteEventArray(sb, mapJson, "coord_events");
            sb.Append(",\n");

            // ── Sign / BG events ──
            sb.Append("  \"bg_events\": ");
            WriteEventArray(sb, mapJson, "bg_events");
            sb.Append(",\n");

            // ── Per-cell metatile attributes (behavior, terrain, encounter, layer) ──
            sb.Append("  \"metatile_behaviors\": [\n");
            for (int y = 0; y < mapHeight; y++)
            {
                sb.Append("    [");
                for (int x = 0; x < mapWidth; x++)
                {
                    int idx = y * mapWidth + x;
                    int metatileId = blockData[idx] & 0x03FF;
                    int collision = (blockData[idx] >> 10) & 0x03;
                    int elevation = (blockData[idx] >> 12) & 0x0F;

                    uint attr = GetMetatileAttribute(metatileId, primaryAttributes, secondaryAttributes);
                    int behavior = (int)(attr & 0x1FF);

                    if (x > 0) sb.Append(", ");
                    sb.Append(behavior);
                }
                sb.Append(y < mapHeight - 1 ? "],\n" : "]\n");
            }
            sb.Append("  ],\n");

            // ── Per-cell collision data ──
            sb.Append("  \"collision\": [\n");
            for (int y = 0; y < mapHeight; y++)
            {
                sb.Append("    [");
                for (int x = 0; x < mapWidth; x++)
                {
                    int idx = y * mapWidth + x;
                    int collision = (blockData[idx] >> 10) & 0x03;
                    if (x > 0) sb.Append(", ");
                    sb.Append(collision);
                }
                sb.Append(y < mapHeight - 1 ? "],\n" : "]\n");
            }
            sb.Append("  ],\n");

            // ── Per-cell elevation data ──
            sb.Append("  \"elevation\": [\n");
            for (int y = 0; y < mapHeight; y++)
            {
                sb.Append("    [");
                for (int x = 0; x < mapWidth; x++)
                {
                    int idx = y * mapWidth + x;
                    int elevation = (blockData[idx] >> 12) & 0x0F;
                    if (x > 0) sb.Append(", ");
                    sb.Append(elevation);
                }
                sb.Append(y < mapHeight - 1 ? "],\n" : "]\n");
            }
            sb.Append("  ]\n");

            sb.Append("}\n");
            File.WriteAllText(jsonPath, sb.ToString(), Utf8NoBom);
        }

        private static uint GetMetatileAttribute(
            int metatileId,
            uint[] primaryAttributes,
            uint[] secondaryAttributes)
        {
            if (metatileId < TilesetRenderer.NUM_METATILES_IN_PRIMARY)
            {
                return (metatileId < primaryAttributes.Length)
                    ? primaryAttributes[metatileId] : 0;
            }
            else
            {
                int secIdx = metatileId - TilesetRenderer.NUM_METATILES_IN_PRIMARY;
                return (secIdx >= 0 && secIdx < secondaryAttributes.Length)
                    ? secondaryAttributes[secIdx] : 0;
            }
        }

        // ─────────── JSON Helpers ───────────

        private static void WriteJsonField(StringBuilder sb, string indent, string key, Dictionary<string, object> obj)
        {
            if (!obj.ContainsKey(key)) return;
            sb.Append(indent);
            sb.Append('"'); sb.Append(key); sb.Append("\": ");
            WriteJsonValue(sb, obj[key], indent);
            sb.Append(",\n");
        }

        private static void WriteEventArray(StringBuilder sb, Dictionary<string, object> mapJson, string key)
        {
            if (mapJson.ContainsKey(key))
                WriteJsonValue(sb, mapJson[key], "  ");
            else
                sb.Append("[]");
        }

        private static void WriteJsonValue(StringBuilder sb, object val, string indent)
        {
            if (val == null)
            {
                sb.Append("null");
            }
            else if (val is string s)
            {
                JStr(sb, s);
            }
            else if (val is bool b)
            {
                sb.Append(b ? "true" : "false");
            }
            else if (val is int || val is long || val is decimal || val is double || val is float)
            {
                sb.Append(val);
            }
            else if (val is ArrayList list)
            {
                if (list.Count == 0)
                {
                    sb.Append("[]");
                    return;
                }
                sb.Append("[\n");
                string childIndent = indent + "  ";
                for (int i = 0; i < list.Count; i++)
                {
                    sb.Append(childIndent);
                    WriteJsonValue(sb, list[i], childIndent);
                    sb.Append(i < list.Count - 1 ? ",\n" : "\n");
                }
                sb.Append(indent); sb.Append(']');
            }
            else if (val is Dictionary<string, object> dict)
            {
                if (dict.Count == 0)
                {
                    sb.Append("{}");
                    return;
                }
                sb.Append("{\n");
                string childIndent = indent + "  ";
                int idx = 0;
                foreach (var kv in dict)
                {
                    sb.Append(childIndent);
                    sb.Append('"'); sb.Append(kv.Key); sb.Append("\": ");
                    WriteJsonValue(sb, kv.Value, childIndent);
                    sb.Append(idx < dict.Count - 1 ? ",\n" : "\n");
                    idx++;
                }
                sb.Append(indent); sb.Append('}');
            }
            else
            {
                // fallback: toString
                JStr(sb, val.ToString());
            }
        }

        // ─────────── Tiny Helpers ───────────

        private static void Put16(byte[] b, int o, ushort v)
        {
            b[o] = (byte)(v & 0xFF);
            b[o + 1] = (byte)(v >> 8);
        }

        /// <summary>Append a line using LF only (no CR).</summary>
        private static void L(StringBuilder sb, string line)
        {
            sb.Append(line);
            sb.Append('\n');
        }

        /// <summary>Append a JSON-escaped string value (with quotes).</summary>
        private static void JStr(StringBuilder sb, string value)
        {
            if (value == null) { sb.Append("null"); return; }
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
        }

        private static string SanitizeNodeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Map";
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
                else if (c == ' ' || c == '-') sb.Append('_');
            }
            var result = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(result) ? "Map" : result;
        }
    }
}
