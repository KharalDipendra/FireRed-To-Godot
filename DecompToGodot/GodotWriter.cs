using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DecompToGodot
{
    /// <summary>
    /// Writes Godot 4.3+ compatible files:
    ///   .tres  — TileSet resource with Ground and Overlay atlas sources
    ///   .tscn  — Scene with TileMapLayer nodes
    ///   .json  — Per-map event data (NPCs, warps, triggers, signs, metatile attributes)
    /// </summary>
    public static class GodotWriter
    {
        private const int ATLAS_COLUMNS = TilesetRenderer.ATLAS_COLUMNS;
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        // ═══════════ TileSet .tres ═══════════

        /// <summary>
        /// Write a Godot TileSet resource (.tres) referencing ground, overlay, and collision atlas PNGs.
        /// </summary>
        public static void WriteTileSetResource(
            string tresPath,
            string groundTexRelPath, string overlayTexRelPath,
            string collisionTexRelPath,
            int totalMetatilePositions, bool hasOverlay)
        {
            var sb = new StringBuilder(totalMetatilePositions * 40);

            // Count: ground + [overlay] + collision textures & sources
            int extRes = 2 + (hasOverlay ? 1 : 0);
            int subRes = 2 + (hasOverlay ? 1 : 0);
            int loadSteps = extRes + subRes + 1;

            int nextId = 1;
            int groundTexId = nextId++;
            int overlayTexId = hasOverlay ? nextId++ : -1;
            int collisionTexId = nextId++;

            L(sb, $"[gd_resource type=\"TileSet\" load_steps={loadSteps} format=3]");
            L(sb, "");

            // External resources — textures
            L(sb, $"[ext_resource type=\"Texture2D\" path=\"{groundTexRelPath}\" id=\"{groundTexId}\"]");
            if (hasOverlay)
                L(sb, $"[ext_resource type=\"Texture2D\" path=\"{overlayTexRelPath}\" id=\"{overlayTexId}\"]");
            L(sb, $"[ext_resource type=\"Texture2D\" path=\"{collisionTexRelPath}\" id=\"{collisionTexId}\"]");
            L(sb, "");

            // Sub-resource — ground atlas source
            L(sb, "[sub_resource type=\"TileSetAtlasSource\" id=\"TileSetAtlasSource_0\"]");
            L(sb, "resource_name = \"Ground\"");
            L(sb, $"texture = ExtResource(\"{groundTexId}\")");
            L(sb, "texture_region_size = Vector2i(16, 16)");
            WriteTileEntries(sb, totalMetatilePositions, ATLAS_COLUMNS);
            L(sb, "");

            // Sub-resource — overlay atlas source
            if (hasOverlay)
            {
                L(sb, "[sub_resource type=\"TileSetAtlasSource\" id=\"TileSetAtlasSource_1\"]");
                L(sb, "resource_name = \"Overlay\"");
                L(sb, $"texture = ExtResource(\"{overlayTexId}\")");
                L(sb, "texture_region_size = Vector2i(16, 16)");
                WriteTileEntries(sb, totalMetatilePositions, ATLAS_COLUMNS);
                L(sb, "");
            }

            // Sub-resource — collision overlay atlas source
            int collisionSrcIdx = hasOverlay ? 2 : 1;
            L(sb, $"[sub_resource type=\"TileSetAtlasSource\" id=\"TileSetAtlasSource_{collisionSrcIdx}\"]");
            L(sb, "resource_name = \"Collisions\"");
            L(sb, $"texture = ExtResource(\"{collisionTexId}\")");
            L(sb, "texture_region_size = Vector2i(16, 16)");
            int collisionTileCount = TilesetRenderer.COLLISION_COLS * TilesetRenderer.COLLISION_ROWS;
            WriteTileEntries(sb, collisionTileCount, TilesetRenderer.COLLISION_COLS);
            L(sb, "");

            // Main [resource]
            L(sb, "[resource]");
            L(sb, "tile_size = Vector2i(16, 16)");
            L(sb, "sources/0 = SubResource(\"TileSetAtlasSource_0\")");
            if (hasOverlay)
                L(sb, "sources/1 = SubResource(\"TileSetAtlasSource_1\")");
            L(sb, $"sources/{collisionSrcIdx} = SubResource(\"TileSetAtlasSource_{collisionSrcIdx}\")");

            File.WriteAllText(tresPath, sb.ToString(), Utf8NoBom);
        }

        private static void WriteTileEntries(StringBuilder sb, int count, int columns)
        {
            for (int id = 0; id < count; id++)
            {
                int col = id % columns;
                int row = id / columns;
                L(sb, $"{col}:{row}/0 = 0");
            }
        }

        // ═══════════ Map Scene .tscn ═══════════

        /// <summary>
        /// Write a Godot scene (.tscn) with Ground and Overlay TileMapLayer nodes.
        /// </summary>
        /// <param name="tscnPath">Output file path</param>
        /// <param name="tilesetRelPath">Relative path to .tres from scenes/ dir</param>
        /// <param name="mapName">Display name for root node</param>
        /// <param name="blockData">Block data (uint16 per cell: metatile ID in bits 0-9)</param>
        /// <param name="mapWidth">Map width in blocks</param>
        /// <param name="mapHeight">Map height in blocks</param>
        /// <param name="hasOverlay">Whether to include overlay layer</param>
        public static void WriteMapScene(
            string tscnPath, string tilesetRelPath,
            string mapName, ushort[] blockData,
            int mapWidth, int mapHeight, bool hasOverlay)
        {
            string label = SanitizeNodeName(mapName);

            var sb = new StringBuilder(mapWidth * mapHeight * 50);

            L(sb, "[gd_scene load_steps=2 format=3]");
            L(sb, "");
            L(sb, $"[ext_resource type=\"TileSet\" path=\"{tilesetRelPath}\" id=\"1\"]");
            L(sb, "");

            // Root node
            L(sb, $"[node name=\"{label}\" type=\"Node2D\"]");
            L(sb, "");

            // Ground layer — source 0
            L(sb, "[node name=\"Ground\" type=\"TileMapLayer\" parent=\".\"]");
            L(sb, "tile_set = ExtResource(\"1\")");
            L(sb, "texture_filter = 1");
            sb.Append("tile_map_data = ");
            WriteTileMapData(sb, blockData, mapWidth, mapHeight, sourceId: 0);
            sb.Append('\n');
            L(sb, "");

            // Overlay layer — source 1
            if (hasOverlay)
            {
                L(sb, "[node name=\"Overlay\" type=\"TileMapLayer\" parent=\".\"]");
                L(sb, "tile_set = ExtResource(\"1\")");
                L(sb, "texture_filter = 1");
                sb.Append("tile_map_data = ");
                WriteTileMapData(sb, blockData, mapWidth, mapHeight, sourceId: 1);
                sb.Append('\n');
                L(sb, "");
            }

            // Collisions overlay layer
            int collisionSourceId = hasOverlay ? 2 : 1;
            L(sb, "[node name=\"Collisions\" type=\"TileMapLayer\" parent=\".\"]");
            L(sb, "tile_set = ExtResource(\"1\")");
            L(sb, "modulate = Color(1, 1, 1, 0.3)");
            L(sb, "texture_filter = 1");
            L(sb, "visible = false");
            sb.Append("tile_map_data = ");
            WriteCollisionTileMapData(sb, blockData, mapWidth, mapHeight, collisionSourceId);
            sb.Append('\n');

            File.WriteAllText(tscnPath, sb.ToString(), Utf8NoBom);
        }

        /// <summary>
        /// Godot 4.3+ TileMapLayer tile_map_data — PackedByteArray.
        /// Format: 2-byte header (version=0) + 12 bytes per cell.
        /// Each cell: x(2), y(2), sourceId(2), atlasX(2), atlasY(2), altTile(2).
        /// </summary>
        private static void WriteTileMapData(
            StringBuilder sb, ushort[] blockData,
            int w, int h, int sourceId)
        {
            const ushort TILE_MAP_DATA_FORMAT = 0;

            int total = w * h;
            var buf = new byte[2 + total * 12];
            int off = 0;

            Put16(buf, off, TILE_MAP_DATA_FORMAT);
            off += 2;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    // Metatile ID from block data (bits 0-9)
                    int metatileId = blockData[idx] & 0x03FF;
                    int ax = metatileId % ATLAS_COLUMNS;
                    int ay = metatileId / ATLAS_COLUMNS;

                    Put16(buf, off + 0, (ushort)x);
                    Put16(buf, off + 2, (ushort)y);
                    Put16(buf, off + 4, (ushort)sourceId);
                    Put16(buf, off + 6, (ushort)ax);
                    Put16(buf, off + 8, (ushort)ay);
                    Put16(buf, off + 10, 0); // alt_tile
                    off += 12;
                }
            }

            sb.Append("PackedByteArray(");
            for (int i = 0; i < buf.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(buf[i]);
            }
            sb.Append(')');
        }

        /// <summary>
        /// Godot tile_map_data for the Collisions layer.
        /// Maps each cell to (collision, elevation) in the collision atlas.
        /// Atlas X = collision value (0-3), Atlas Y = elevation value (0-15).
        /// </summary>
        private static void WriteCollisionTileMapData(
            StringBuilder sb, ushort[] blockData,
            int w, int h, int sourceId)
        {
            const ushort TILE_MAP_DATA_FORMAT = 0;

            int total = w * h;
            var buf = new byte[2 + total * 12];
            int off = 0;

            Put16(buf, off, TILE_MAP_DATA_FORMAT);
            off += 2;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    int collision = (blockData[idx] >> 10) & 0x03;
                    int elevation = (blockData[idx] >> 12) & 0x0F;

                    Put16(buf, off + 0, (ushort)x);
                    Put16(buf, off + 2, (ushort)y);
                    Put16(buf, off + 4, (ushort)sourceId);
                    Put16(buf, off + 6, (ushort)collision);  // atlas X = collision column
                    Put16(buf, off + 8, (ushort)elevation);   // atlas Y = elevation row
                    Put16(buf, off + 10, 0);
                    off += 12;
                }
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
            uint[] secondaryAttributes)
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
