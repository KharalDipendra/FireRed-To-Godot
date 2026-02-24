using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace DecompToGodot
{
    /// <summary>
    /// Loads decomp tileset assets (4bpp indexed tiles.png, JASC-PAL palettes,
    /// metatiles.bin) and renders metatile atlas PNGs for use in Godot TileSet
    /// resources.
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
        /// Render the ground atlas (bottom layer) and overlay atlas (top layer)
        /// for a primary + secondary tileset combination.
        /// </summary>
        /// <param name="primaryDir">Path to primary tileset directory (e.g. data/tilesets/primary/general)</param>
        /// <param name="secondaryDir">Path to secondary tileset directory</param>
        /// <param name="groundAtlasPath">Output path for ground layer atlas PNG</param>
        /// <param name="overlayAtlasPath">Output path for overlay layer atlas PNG</param>
        /// <returns>Total number of metatile positions in the atlas</returns>
        /// <summary>
        /// Render the ground atlas (bottom layer) and overlay atlas (top layer)
        /// for a primary + secondary tileset combination.
        /// </summary>
        /// <param name="primaryDir">Path to primary tileset directory</param>
        /// <param name="secondaryDir">Path to secondary tileset directory (metatiles)</param>
        /// <param name="groundAtlasPath">Output path for ground layer atlas PNG</param>
        /// <param name="overlayAtlasPath">Output path for overlay layer atlas PNG</param>
        /// <param name="secondaryTilesDir">
        /// Optional: directory containing tiles.png for the secondary tileset.
        /// Some tilesets (e.g. SilphCo) share tiles from another tileset (e.g. Condominiums).
        /// If null, defaults to secondaryDir.
        /// </param>
        /// <param name="secondaryPalettesDir">
        /// Optional: directory containing palettes/ for the secondary tileset.
        /// If null, defaults to secondaryDir.
        /// </param>
        /// <returns>Total number of metatile positions in the atlas</returns>
        public static int RenderAtlases(
            string primaryDir, string secondaryDir,
            string groundAtlasPath, string overlayAtlasPath,
            string secondaryTilesDir = null, string secondaryPalettesDir = null)
        {
            // Fall back to secondaryDir when no cross-reference override is given
            if (secondaryTilesDir == null) secondaryTilesDir = secondaryDir;
            if (secondaryPalettesDir == null) secondaryPalettesDir = secondaryDir;

            // ── Load primary tile pixel indices (4bpp) ──
            var primaryPixels = LoadTilePixelIndices(Path.Combine(primaryDir, "tiles.png"));
            int primaryPngWidth = primaryPixels.GetLength(0);
            int primaryPngHeight = primaryPixels.GetLength(1);
            int primaryTileCount = (primaryPngWidth / 8) * (primaryPngHeight / 8);

            // ── Load secondary tile pixel indices (may come from a different tileset dir) ──
            byte[,] secondaryPixels = null;
            int secondaryPngWidth = 0;
            string secTilesPath = Path.Combine(secondaryTilesDir, "tiles.png");
            if (File.Exists(secTilesPath))
            {
                secondaryPixels = LoadTilePixelIndices(secTilesPath);
                secondaryPngWidth = secondaryPixels.GetLength(0);
            }

            // ── Load and merge palettes ──
            //  palettes[0..6]   from primary
            //  palettes[7..12]  from secondary (may come from a different tileset dir)
            var palettes = new Color[16][];
            for (int i = 0; i < 16; i++)
                palettes[i] = new Color[16]; // default black/transparent

            // Primary palettes 0-6
            for (int i = 0; i < NUM_PALS_IN_PRIMARY; i++)
            {
                string palFile = Path.Combine(primaryDir, "palettes", $"{i:D2}.pal");
                if (File.Exists(palFile))
                    palettes[i] = LoadJascPalette(palFile);
            }
            // Secondary palettes 7-12 (loaded from overridden palette dir)
            for (int i = NUM_PALS_IN_PRIMARY; i < NUM_PALS_TOTAL; i++)
            {
                string palFile = Path.Combine(secondaryPalettesDir, "palettes", $"{i:D2}.pal");
                if (File.Exists(palFile))
                    palettes[i] = LoadJascPalette(palFile);
            }

            // ── Load metatiles ──
            var primaryMetatiles = ReadMetatilesBin(Path.Combine(primaryDir, "metatiles.bin"));
            var secondaryMetatiles = ReadMetatilesBin(Path.Combine(secondaryDir, "metatiles.bin"));

            int primaryMetatileCount = primaryMetatiles.Length;
            int secondaryMetatileCount = secondaryMetatiles.Length;

            // Total atlas positions = NUM_METATILES_IN_PRIMARY + secondaryMetatileCount
            // Metatile IDs 0..(primary-1) → atlas positions 0..(primary-1)
            // Metatile IDs 640..(640+secondary-1) → atlas positions 640..(640+secondary-1)
            int totalPositions = NUM_METATILES_IN_PRIMARY + secondaryMetatileCount;
            int atlasRows = (totalPositions + ATLAS_COLUMNS - 1) / ATLAS_COLUMNS;
            int atlasWidth = ATLAS_COLUMNS * 16;
            int atlasHeight = atlasRows * 16;

            // ARGB pixel buffers (initialized to 0 = transparent)
            var groundBuf = new byte[atlasWidth * atlasHeight * 4];
            var overlayBuf = new byte[atlasWidth * atlasHeight * 4];

            // ── Render primary metatiles (positions 0 to primaryMetatileCount-1) ──
            for (int i = 0; i < primaryMetatileCount; i++)
            {
                int col = i % ATLAS_COLUMNS;
                int row = i / ATLAS_COLUMNS;
                int ax = col * 16;
                int ay = row * 16;

                RenderMetatileLayer(groundBuf, atlasWidth, ax, ay,
                    primaryMetatiles[i], false,
                    primaryPixels, primaryPngWidth,
                    secondaryPixels, secondaryPngWidth,
                    palettes);

                RenderMetatileLayer(overlayBuf, atlasWidth, ax, ay,
                    primaryMetatiles[i], true,
                    primaryPixels, primaryPngWidth,
                    secondaryPixels, secondaryPngWidth,
                    palettes);
            }

            // ── Render secondary metatiles (positions 640 to 640+secondaryMetatileCount-1) ──
            for (int i = 0; i < secondaryMetatileCount; i++)
            {
                int pos = NUM_METATILES_IN_PRIMARY + i;
                int col = pos % ATLAS_COLUMNS;
                int row = pos / ATLAS_COLUMNS;
                int ax = col * 16;
                int ay = row * 16;

                RenderMetatileLayer(groundBuf, atlasWidth, ax, ay,
                    secondaryMetatiles[i], false,
                    primaryPixels, primaryPngWidth,
                    secondaryPixels, secondaryPngWidth,
                    palettes);

                RenderMetatileLayer(overlayBuf, atlasWidth, ax, ay,
                    secondaryMetatiles[i], true,
                    primaryPixels, primaryPngWidth,
                    secondaryPixels, secondaryPngWidth,
                    palettes);
            }

            // ── Save atlas PNGs ──
            SaveArgbBitmap(groundBuf, atlasWidth, atlasHeight, groundAtlasPath);
            SaveArgbBitmap(overlayBuf, atlasWidth, atlasHeight, overlayAtlasPath);

            return totalPositions;
        }

        /// <summary>
        /// Read the raw 4-bit palette indices from a 4bpp indexed PNG.
        /// Returns byte[width, height] of indices 0-15.
        /// </summary>
        private static byte[,] LoadTilePixelIndices(string tilesPngPath)
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
        private static Color[] LoadJascPalette(string palPath)
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
        private static ushort[][] ReadMetatilesBin(string path)
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
        /// Render one layer of a metatile into an ARGB pixel buffer.
        /// </summary>
        /// <param name="buf">ARGB buffer (4 bytes per pixel: B, G, R, A)</param>
        /// <param name="bufWidth">Buffer width in pixels</param>
        /// <param name="destX">Destination X in buffer (top-left of 16×16 area)</param>
        /// <param name="destY">Destination Y in buffer</param>
        /// <param name="metatile">8 tile entries for this metatile</param>
        /// <param name="isTopLayer">true = render entries 4-7 (top/overlay), false = entries 0-3 (bottom/ground)</param>
        private static void RenderMetatileLayer(
            byte[] buf, int bufWidth,
            int destX, int destY,
            ushort[] metatile, bool isTopLayer,
            byte[,] primaryPixels, int primaryPngWidth,
            byte[,] secondaryPixels, int secondaryPngWidth,
            Color[][] palettes)
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

                // Select tile source
                byte[,] pixels;
                int pngWidth;
                int adjustedId;

                if (tileId < NUM_TILES_IN_PRIMARY)
                {
                    pixels = primaryPixels;
                    pngWidth = primaryPngWidth;
                    adjustedId = tileId;
                }
                else
                {
                    pixels = secondaryPixels;
                    pngWidth = secondaryPngWidth;
                    adjustedId = tileId - NUM_TILES_IN_PRIMARY;
                }

                if (pixels == null || pngWidth == 0) continue;

                int tilesPerRow = pngWidth / 8;
                int maxTile = tilesPerRow * (pixels.GetLength(1) / 8);
                if (adjustedId < 0 || adjustedId >= maxTile) continue;

                int tileCol = adjustedId % tilesPerRow;
                int tileRow = adjustedId / tilesPerRow;

                Color[] pal = (palNum < 16) ? palettes[palNum] : palettes[0];

                // Render 8×8 tile
                for (int py = 0; py < 8; py++)
                {
                    for (int px = 0; px < 8; px++)
                    {
                        int srcPx = hFlip ? (7 - px) : px;
                        int srcPy = vFlip ? (7 - py) : py;

                        int pixX = tileCol * 8 + srcPx;
                        int pixY = tileRow * 8 + srcPy;

                        if (pixX >= pixels.GetLength(0) || pixY >= pixels.GetLength(1))
                            continue;

                        byte idx = pixels[pixX, pixY];
                        if (idx == 0) continue; // palette index 0 = transparent

                        Color c = pal[idx];
                        int dx = destX + tileOffX + px;
                        int dy = destY + tileOffY + py;
                        int off = (dy * bufWidth + dx) * 4;

                        buf[off + 0] = c.B;
                        buf[off + 1] = c.G;
                        buf[off + 2] = c.R;
                        buf[off + 3] = 255; // fully opaque
                    }
                }
            }
        }

        /// <summary>
        /// Save an ARGB pixel buffer as a 32-bit PNG.
        /// </summary>
        public static void SaveArgbBitmap(byte[] buf, int width, int height, string path)
        {
            using (var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                var lockData = bmp.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);

                Marshal.Copy(buf, 0, lockData.Scan0, buf.Length);
                bmp.UnlockBits(lockData);

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                bmp.Save(path, ImageFormat.Png);
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
