using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace DecompToGodot
{
    /// <summary>
    /// Loads decomp tileset assets (4bpp indexed tiles.png, JASC-PAL palettes,
    /// metatiles.bin) and draws metatiles for the shared Godot TileSet atlases
    /// (see <see cref="WorldTileset"/>).
    ///
    /// GBA metatile structure (16 bytes = 8 tile entries × 2 bytes):
    ///   [0..3] = bottom layer (TL, TR, BL, BR)
    ///   [4..7] = top    layer (TL, TR, BL, BR)
    ///
    /// Each tile entry (uint16 LE):
    ///   bits  0-9:  tile number (0-1023)
    ///   bit  10:    horizontal flip
    ///   bit  11:    vertical flip
    ///   bits 12-15: palette number (0-15)
    /// </summary>
    public static class TilesetRenderer
    {
        // FireRed/LeafGreen constants
        public const int NUM_TILES_IN_PRIMARY = 640;
        public const int NUM_METATILES_IN_PRIMARY = 640;
        public const int NUM_PALS_IN_PRIMARY = 7;
        public const int NUM_PALS_TOTAL = 13;
        public const int ATLAS_COLUMNS = 8;

        /// <summary>
        /// Build the BG palette RAM the overworld uses: palettes 0..6 come from the primary
        /// tileset, 7..12 from the secondary. Palettes 13..15 are reserved for weather and
        /// effects and are never part of a tileset, so they stay black.
        /// </summary>
        public static Color[][] MergePalettes(TilesetData primary, TilesetData secondary)
        {
            var palettes = new Color[16][];
            for (int i = 0; i < 16; i++)
                palettes[i] = new Color[16];

            if (primary != null)
                for (int i = 0; i < NUM_PALS_IN_PRIMARY; i++)
                    palettes[i] = primary.Palettes[i];

            if (secondary != null)
                for (int i = NUM_PALS_IN_PRIMARY; i < NUM_PALS_TOTAL; i++)
                    palettes[i] = secondary.Palettes[i];

            return palettes;
        }

        /// <summary>
        /// True if any of the 8 tile entries of a primary metatile reads from the
        /// secondary half of VRAM (tile id 640+) or from a secondary palette (7+).
        /// Such a metatile looks different for every secondary tileset it is paired with.
        /// </summary>
        public static bool DependsOnSecondary(ushort[] metatile)
        {
            for (int e = 0; e < 8; e++)
            {
                int tileId = metatile[e] & 0x3FF;
                int palNum = (metatile[e] >> 12) & 0xF;
                if (tileId >= NUM_TILES_IN_PRIMARY || palNum >= NUM_PALS_IN_PRIMARY)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Load a tileset folder (tiles.png, palettes/, metatiles.bin, metatile_attributes.bin).
        /// Tiles and palettes can come from another folder when headers.h borrows them.
        /// </summary>
        public static TilesetData LoadTileset(
            string label, bool isSecondary,
            string dir, string tilesDir, string palettesDir)
        {
            var data = new TilesetData
            {
                Label = label,
                IsSecondary = isSecondary,
                Metatiles = ReadMetatilesBin(Path.Combine(dir, "metatiles.bin")),
                Attributes = ReadMetatileAttributes(Path.Combine(dir, "metatile_attributes.bin")),
                Palettes = new Color[16][]
            };

            string tilesPng = Path.Combine(tilesDir ?? dir, "tiles.png");
            if (File.Exists(tilesPng))
                data.TilePixels = LoadTilePixelIndices(tilesPng);
            else
                Console.WriteLine($"  WARNING: {label} has no tiles.png ({tilesPng})");

            for (int i = 0; i < 16; i++)
            {
                string palFile = Path.Combine(palettesDir ?? dir, "palettes", $"{i:D2}.pal");
                data.Palettes[i] = File.Exists(palFile) ? LoadJascPalette(palFile) : new Color[16];
            }

            return data;
        }
        /// <summary>
        /// Read the raw 4-bit palette indices from a 4bpp indexed PNG.
        /// Returns byte[width, height] of indices 0-15.
        /// </summary>
        public static byte[,] LoadTilePixelIndices(string tilesPngPath)
        {
            using (var bmp = new Bitmap(tilesPngPath))
            {
                int w = bmp.Width;
                int h = bmp.Height;
                var result = new byte[w, h];

                if (bmp.PixelFormat == PixelFormat.Format4bppIndexed)
                {
                    var lockData = bmp.LockBits(
                        new Rectangle(0, 0, w, h),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format4bppIndexed);

                    int stride = Math.Abs(lockData.Stride);
                    var raw = new byte[stride * h];
                    Marshal.Copy(lockData.Scan0, raw, 0, raw.Length);
                    bmp.UnlockBits(lockData);

                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            int byteOff = y * stride + x / 2;
                            byte val = raw[byteOff];
                            result[x, y] = (x % 2 == 0)
                                ? (byte)(val >> 4)
                                : (byte)(val & 0x0F);
                        }
                    }
                }
                else if (bmp.PixelFormat == PixelFormat.Format8bppIndexed)
                {
                    // Fallback: 8bpp indexed → lower 4 bits as index
                    var lockData = bmp.LockBits(
                        new Rectangle(0, 0, w, h),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format8bppIndexed);

                    int stride = Math.Abs(lockData.Stride);
                    var raw = new byte[stride * h];
                    Marshal.Copy(lockData.Scan0, raw, 0, raw.Length);
                    bmp.UnlockBits(lockData);

                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            result[x, y] = (byte)(raw[y * stride + x] & 0x0F);
                }
                else
                {
                    // Non-indexed PNG: try to use the color index from the
                    // image's built-in palette. If truly RGB, we can't remap
                    // per palette, so we just use it as-is with index 1 for
                    // any non-transparent pixel. This is a degraded fallback.
                    Console.WriteLine($"  WARNING: {tilesPngPath} is not 4bpp indexed ({bmp.PixelFormat}). Palette remapping may be incorrect.");
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            var c = bmp.GetPixel(x, y);
                            result[x, y] = (c.A < 128) ? (byte)0 : (byte)1;
                        }
                }

                return result;
            }
        }

        /// <summary>
        /// Load a JASC-PAL palette file (16 entries of R G B).
        /// </summary>
        public static Color[] LoadJascPalette(string palPath)
        {
            var colors = new Color[16];
            var lines = File.ReadAllLines(palPath);

            // JASC-PAL format:
            //   JASC-PAL
            //   0100
            //   16
            //   R G B
            //   R G B
            //   ...

            int colorIndex = 0;
            bool pastHeader = false;

            for (int i = 0; i < lines.Length && colorIndex < 16; i++)
            {
                string line = lines[i].Trim();
                if (line == "JASC-PAL" || line == "0100") continue;

                // The "16" line (color count)
                if (!pastHeader)
                {
                    int count;
                    if (int.TryParse(line, out count))
                    {
                        pastHeader = true;
                        continue;
                    }
                }

                // R G B line
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    int r, g, b;
                    if (int.TryParse(parts[0], out r) &&
                        int.TryParse(parts[1], out g) &&
                        int.TryParse(parts[2], out b))
                    {
                        colors[colorIndex] = Color.FromArgb(255, r, g, b);
                        colorIndex++;
                    }
                }
            }

            return colors;
        }

        /// <summary>
        /// Read metatiles.bin → array of metatile data.
        /// Each metatile = ushort[8] (8 tile entries).
        /// </summary>
        public static ushort[][] ReadMetatilesBin(string path)
        {
            if (!File.Exists(path))
                return new ushort[0][];

            var raw = File.ReadAllBytes(path);
            int count = raw.Length / 16; // 16 bytes per metatile
            var metatiles = new ushort[count][];

            for (int i = 0; i < count; i++)
            {
                metatiles[i] = new ushort[8];
                for (int j = 0; j < 8; j++)
                {
                    int offset = i * 16 + j * 2;
                    metatiles[i][j] = (ushort)(raw[offset] | (raw[offset + 1] << 8));
                }
            }

            return metatiles;
        }

        /// <summary>
        /// Read metatile_attributes.bin → uint32 per metatile.
        /// For FRLG, each attribute (32 bits):
        ///   bits  0-8:  behavior
        ///   bits  9-13: terrain type
        ///   bits 24-26: encounter type
        ///   bits 29-30: layer type (0=normal, 1=covered, 2=split)
        /// </summary>
        public static uint[] ReadMetatileAttributes(string path)
        {
            if (!File.Exists(path))
                return new uint[0];

            var raw = File.ReadAllBytes(path);
            int count = raw.Length / 4;
            var attrs = new uint[count];

            for (int i = 0; i < count; i++)
            {
                attrs[i] = (uint)(raw[i * 4]
                    | (raw[i * 4 + 1] << 8)
                    | (raw[i * 4 + 2] << 16)
                    | (raw[i * 4 + 3] << 24));
            }

            return attrs;
        }

        /// <summary>
        /// Draw one layer of a metatile into an RGBA pixel buffer.
        /// Tiles covered by an active animation are taken from that animation's
        /// current frame instead of tiles.png, exactly like the game's VRAM DMA.
        /// </summary>
        /// <param name="buf">RGBA buffer (4 bytes per pixel: R, G, B, A)</param>
        /// <param name="bufWidth">Buffer width in pixels</param>
        /// <param name="destX">Destination X in buffer (top left of the 16×16 area)</param>
        /// <param name="destY">Destination Y in buffer</param>
        /// <param name="metatile">8 tile entries for this metatile</param>
        /// <param name="isTopLayer">true = entries 4..7 (overlay), false = entries 0..3 (ground)</param>
        /// <param name="animations">Animations active for this metatile (may be null)</param>
        /// <param name="animFrames">Frame index per entry of <paramref name="animations"/></param>
        public static void DrawMetatileLayer(
            byte[] buf, int bufWidth,
            int destX, int destY,
            ushort[] metatile, bool isTopLayer,
            TilesetData primary, TilesetData secondary, Color[][] palettes,
            IList<TileAnimation> animations = null, int[] animFrames = null)
        {
            int startEntry = isTopLayer ? 4 : 0;

            for (int e = 0; e < 4; e++)
            {
                ushort entry = metatile[startEntry + e];
                int tileId = entry & 0x3FF;
                bool hFlip = (entry & 0x400) != 0;
                bool vFlip = (entry & 0x800) != 0;
                int palNum = (entry >> 12) & 0xF;

                // Position within 16×16 metatile: TL(0), TR(1), BL(2), BR(3)
                int tileOffX = (e % 2) * 8;
                int tileOffY = (e / 2) * 8;

                // Select tile source: animation frame, primary tiles or secondary tiles
                byte[,] pixels = null;
                int adjustedId = 0;

                if (animations != null)
                {
                    for (int a = 0; a < animations.Count; a++)
                    {
                        if (!animations[a].Covers(tileId)) continue;
                        pixels = animations[a].FramePixels[animFrames[a]];
                        adjustedId = tileId - animations[a].DestTile;
                        break;
                    }
                }

                if (pixels == null)
                {
                    if (tileId < NUM_TILES_IN_PRIMARY)
                    {
                        pixels = primary?.TilePixels;
                        adjustedId = tileId;
                    }
                    else
                    {
                        pixels = secondary?.TilePixels;
                        adjustedId = tileId - NUM_TILES_IN_PRIMARY;
                    }
                }

                if (pixels == null) continue;

                int pngWidth = pixels.GetLength(0);
                int tilesPerRow = pngWidth / 8;
                if (tilesPerRow == 0) continue;
                int maxTile = tilesPerRow * (pixels.GetLength(1) / 8);
                if (adjustedId < 0 || adjustedId >= maxTile) continue;

                int tileCol = adjustedId % tilesPerRow;
                int tileRow = adjustedId / tilesPerRow;

                Color[] pal = palettes[palNum];

                // Render 8×8 tile
                for (int py = 0; py < 8; py++)
                {
                    for (int px = 0; px < 8; px++)
                    {
                        int srcPx = hFlip ? (7 - px) : px;
                        int srcPy = vFlip ? (7 - py) : py;

                        byte idx = pixels[tileCol * 8 + srcPx, tileRow * 8 + srcPy];
                        if (idx == 0) continue; // palette index 0 = transparent

                        Color c = pal[idx];
                        int dx = destX + tileOffX + px;
                        int dy = destY + tileOffY + py;
                        int off = (dy * bufWidth + dx) * 4;

                        buf[off + 0] = c.R;
                        buf[off + 1] = c.G;
                        buf[off + 2] = c.B;
                        buf[off + 3] = 255; // fully opaque
                    }
                }
            }
        }

        // ═══════════ Collision Overlay Atlas ═══════════

        /// <summary>
        /// Number of columns in the collision atlas (one per collision value 0-3).
        /// </summary>
        public const int COLLISION_COLS = 4;

        /// <summary>
        /// Number of rows in the collision atlas (one per elevation value 0-15).
        /// </summary>
        public const int COLLISION_ROWS = 16;

        /// <summary>
        /// Generate a shared collision overlay atlas PNG.
        /// Layout: 4 columns (collision 0-3) × 16 rows (elevation 0-15) of 16×16 tiles.
        /// Passable tiles (collision 0) are semi-transparent; impassable tiles have an X.
        /// Each tile shows its elevation as a hex digit.
        ///
        /// Uses a pure-C# PNG writer (no System.Drawing dependency) so the file
        /// is generated reliably on every platform.
        /// </summary>
        public static void GenerateCollisionAtlas(string outputPath)
        {
            const int T = 16;
            int w = COLLISION_COLS * T;  // 64
            int h = COLLISION_ROWS * T; // 256

            // Build RGBA pixel buffer (PNG byte order: R, G, B, A)
            var rgba = new byte[w * h * 4];

            for (int elev = 0; elev < COLLISION_ROWS; elev++)
            {
                byte cr, cg, cb;
                HsvToRgb(elev * 22.5, 0.75, 1.0, out cr, out cg, out cb);

                for (int coll = 0; coll < COLLISION_COLS; coll++)
                {
                    int ox = coll * T;
                    int oy = elev * T;
                    byte alpha = (byte)(coll == 0 ? 128 : 192);

                    // Fill background
                    for (int py = 0; py < T; py++)
                        for (int px = 0; px < T; px++)
                        {
                            int i = ((oy + py) * w + (ox + px)) * 4;
                            rgba[i] = cr; rgba[i + 1] = cg; rgba[i + 2] = cb; rgba[i + 3] = alpha;
                        }

                    // 1px darker border
                    byte dr = (byte)(cr / 2), dg = (byte)(cg / 2), db = (byte)(cb / 2);
                    for (int d = 0; d < T; d++)
                    {
                        SetPxRgba(rgba, w, ox + d, oy, dr, dg, db, alpha);
                        SetPxRgba(rgba, w, ox + d, oy + T - 1, dr, dg, db, alpha);
                        SetPxRgba(rgba, w, ox, oy + d, dr, dg, db, alpha);
                        SetPxRgba(rgba, w, ox + T - 1, oy + d, dr, dg, db, alpha);
                    }

                    // Draw X for impassable (collision > 0)
                    if (coll > 0)
                    {
                        for (int d = 2; d < T - 2; d++)
                        {
                            SetPxRgba(rgba, w, ox + d, oy + d, 255, 255, 255, 220);
                            SetPxRgba(rgba, w, ox + T - 1 - d, oy + d, 255, 255, 255, 220);
                        }
                    }

                    // Draw hex elevation digit with black outline + white fill
                    DrawHexDigitRgba(rgba, w, ox, oy, elev);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            WritePngRgba(rgba, w, h, outputPath);
        }

        // ─────────── Collision atlas helpers ───────────

        /// <summary>Set a pixel in a BGRA buffer (used by SaveArgbBitmap/System.Drawing path).</summary>
        private static void SetPx(byte[] buf, int bufW, int x, int y, byte r, byte g, byte b, byte a)
        {
            int i = (y * bufW + x) * 4;
            if (i < 0 || i + 3 >= buf.Length) return;
            buf[i] = b; buf[i + 1] = g; buf[i + 2] = r; buf[i + 3] = a;
        }

        /// <summary>Set a pixel in an RGBA buffer (used by the pure-C# PNG writer).</summary>
        private static void SetPxRgba(byte[] buf, int bufW, int x, int y, byte r, byte g, byte b, byte a)
        {
            int i = (y * bufW + x) * 4;
            if (i < 0 || i + 3 >= buf.Length) return;
            buf[i] = r; buf[i + 1] = g; buf[i + 2] = b; buf[i + 3] = a;
        }

        /// <summary>Convert HSV (h: 0-360, s/v: 0-1) to RGB bytes.</summary>
        private static void HsvToRgb(double h, double s, double v, out byte r, out byte g, out byte b)
        {
            h = h % 360;
            if (h < 0) h += 360;
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;
            double r1, g1, b1;

            if (h < 60) { r1 = c; g1 = x; b1 = 0; }
            else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
            else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
            else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
            else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
            else { r1 = c; g1 = 0; b1 = x; }

            r = (byte)Math.Round((r1 + m) * 255);
            g = (byte)Math.Round((g1 + m) * 255);
            b = (byte)Math.Round((b1 + m) * 255);
        }

        /// <summary>Draw a hex digit (0-15) centered on a 16×16 tile with black outline (BGRA buffer).</summary>
        private static void DrawHexDigit(byte[] buf, int bufW, int tileX, int tileY, int digit)
        {
            int[] glyph = HexFont[digit & 0xF];
            int cx = tileX + 7;
            int cy = tileY + 6;

            for (int gy = 0; gy < 5; gy++)
                for (int gx = 0; gx < 3; gx++)
                    if ((glyph[gy] & (4 >> gx)) != 0)
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                                SetPx(buf, bufW, cx + gx + dx, cy + gy + dy, 0, 0, 0, 255);

            for (int gy = 0; gy < 5; gy++)
                for (int gx = 0; gx < 3; gx++)
                    if ((glyph[gy] & (4 >> gx)) != 0)
                        SetPx(buf, bufW, cx + gx, cy + gy, 255, 255, 255, 255);
        }

        /// <summary>Draw a hex digit (0-15) centered on a 16×16 tile with black outline (RGBA buffer).</summary>
        private static void DrawHexDigitRgba(byte[] buf, int bufW, int tileX, int tileY, int digit)
        {
            int[] glyph = HexFont[digit & 0xF];
            int cx = tileX + 7;
            int cy = tileY + 6;

            for (int gy = 0; gy < 5; gy++)
                for (int gx = 0; gx < 3; gx++)
                    if ((glyph[gy] & (4 >> gx)) != 0)
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                                SetPxRgba(buf, bufW, cx + gx + dx, cy + gy + dy, 0, 0, 0, 255);

            for (int gy = 0; gy < 5; gy++)
                for (int gx = 0; gx < 3; gx++)
                    if ((glyph[gy] & (4 >> gx)) != 0)
                        SetPxRgba(buf, bufW, cx + gx, cy + gy, 255, 255, 255, 255);
        }

        /// <summary>3×5 pixel font for hex digits 0-F. Each int[] = 5 rows; each int = 3-bit pattern.</summary>
        private static readonly int[][] HexFont = {
            new[]{7,5,5,5,7}, // 0
            new[]{6,2,2,2,7}, // 1
            new[]{7,1,7,4,7}, // 2
            new[]{7,1,7,1,7}, // 3
            new[]{5,5,7,1,1}, // 4
            new[]{7,4,7,1,7}, // 5
            new[]{7,4,7,5,7}, // 6
            new[]{7,1,2,2,2}, // 7
            new[]{7,5,7,5,7}, // 8
            new[]{7,5,7,1,7}, // 9
            new[]{7,5,7,5,5}, // A
            new[]{6,5,6,5,6}, // B
            new[]{7,4,4,4,7}, // C
            new[]{6,5,5,5,6}, // D
            new[]{7,4,7,4,7}, // E
            new[]{7,4,7,4,4}, // F
        };

        // ═══════════ Pure-C# PNG Writer ═══════════

        /// <summary>
        /// Write an RGBA pixel buffer as a 32-bit PNG without any System.Drawing dependency.
        /// This ensures the collision overlay is always generated reliably.
        /// </summary>
        public static void WritePngRgba(byte[] rgbaPixels, int width, int height, string path)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                // PNG signature
                fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);

                // IHDR
                var ihdr = new byte[13];
                WriteBE32(ihdr, 0, width);
                WriteBE32(ihdr, 4, height);
                ihdr[8] = 8;  // bit depth
                ihdr[9] = 6;  // color type: RGBA
                ihdr[10] = 0; // compression
                ihdr[11] = 0; // filter
                ihdr[12] = 0; // interlace
                WritePngChunk(fs, 0x49484452 /* IHDR */, ihdr);

                // IDAT — filtered rows compressed with zlib
                int rowBytes = 1 + width * 4; // filter byte + RGBA pixels
                var filtered = new byte[rowBytes * height];
                for (int y = 0; y < height; y++)
                {
                    int dstOff = y * rowBytes;
                    filtered[dstOff] = 0; // filter type: None
                    Buffer.BlockCopy(rgbaPixels, y * width * 4, filtered, dstOff + 1, width * 4);
                }

                byte[] zlibData;
                using (var ms = new MemoryStream())
                {
                    // Zlib header: CMF=0x78 (deflate, 32K window), FLG=0x9C
                    ms.WriteByte(0x78);
                    ms.WriteByte(0x9C);

                    using (var deflate = new DeflateStream(ms, CompressionMode.Compress, true))
                    {
                        deflate.Write(filtered, 0, filtered.Length);
                    }

                    // Zlib trailer: Adler-32 of uncompressed data (big-endian)
                    uint adler = ComputeAdler32(filtered);
                    var adlerBytes = new byte[4];
                    WriteBE32(adlerBytes, 0, (int)adler);
                    ms.Write(adlerBytes, 0, 4);

                    zlibData = ms.ToArray();
                }

                WritePngChunk(fs, 0x49444154 /* IDAT */, zlibData);

                // IEND
                WritePngChunk(fs, 0x49454E44 /* IEND */, new byte[0]);
            }
        }

        private static void WritePngChunk(Stream s, int chunkType, byte[] data)
        {
            var lenBytes = new byte[4];
            WriteBE32(lenBytes, 0, data.Length);
            s.Write(lenBytes, 0, 4);

            var typeBytes = new byte[4];
            WriteBE32(typeBytes, 0, chunkType);
            s.Write(typeBytes, 0, 4);

            if (data.Length > 0)
                s.Write(data, 0, data.Length);

            // CRC32 over type + data
            uint crc = Crc32Png(typeBytes, data);
            var crcBytes = new byte[4];
            WriteBE32(crcBytes, 0, (int)crc);
            s.Write(crcBytes, 0, 4);
        }

        private static void WriteBE32(byte[] buf, int off, int val)
        {
            buf[off] = (byte)((val >> 24) & 0xFF);
            buf[off + 1] = (byte)((val >> 16) & 0xFF);
            buf[off + 2] = (byte)((val >> 8) & 0xFF);
            buf[off + 3] = (byte)(val & 0xFF);
        }

        /// <summary>CRC32 used by PNG (polynomial 0xEDB88320).</summary>
        private static uint Crc32Png(byte[] typeBytes, byte[] data)
        {
            uint c = 0xFFFFFFFF;
            for (int i = 0; i < typeBytes.Length; i++)
                c = _crc32Table[(c ^ typeBytes[i]) & 0xFF] ^ (c >> 8);
            for (int i = 0; i < data.Length; i++)
                c = _crc32Table[(c ^ data[i]) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFF;
        }

        private static readonly uint[] _crc32Table = MakeCrc32Table();
        private static uint[] MakeCrc32Table()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        /// <summary>Adler-32 checksum used by zlib.</summary>
        private static uint ComputeAdler32(byte[] data)
        {
            uint a = 1, b = 0;
            for (int i = 0; i < data.Length; i++)
            {
                a = (a + data[i]) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }
    }
}
