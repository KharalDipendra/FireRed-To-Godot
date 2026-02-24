using RomAssetExtractor.GbaSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RomAssetExtractor.Pokemon.Entities
{
    internal class Tileset
    {
        internal const int PALETTE_COUNT = 16;

        internal int MetatileCount { get; private set; }
        internal Pointer Metatiles { get; private set; }
        internal Pointer MetatileAttributes { get; private set; }

        internal List<Palette> Palettes { get; private set; }
        internal byte[] PixelValues { get; private set; }
        internal byte[] AlternatePixelValues { get; set; }

        private bool isCompressed;
        private bool isSecondary;
        private Pointer bitmap;
        private Pointer palette;

        /// <summary>
        /// The tileset animation callback pointer (ARM/Thumb code address in ROM).
        /// Non-null means this tileset has animated tiles (water, flowers, etc.).
        /// </summary>
        internal Pointer AnimationCallback { get; private set; }

        private Tileset(List<Palette> palettes)
        {
            Palettes = palettes;
        }

        internal static byte[] ConvertBytesToTilesetImage(byte[] tilesetData, int tilesWide, out int tilesetImageWidth, out int tilesetImageHeight)
        {
            var totalTiles = tilesetData.Length / 32;
            var tilesTall = (int)Math.Ceiling((double)totalTiles / tilesWide);

            var imageData = new byte[64 * tilesWide * tilesTall];

            for (int i = 0; i < tilesetData.Length; i++)
            {
                var pixelPair = tilesetData[i];

                var pixel1 = pixelPair & 0xf;
                var pixel2 = pixelPair >> 4;

                var tileX = (i >> 5) % tilesWide;
                var tileY = (i >> 5) / tilesWide;
                var pixelXInTile = i & 0x3;
                var pixelYInTile = (i & 0x1f) >> 2;
                var pixelPairIndex = (64 * tilesWide * tileY) + (8 * tilesWide * pixelYInTile) + (8 * tileX) + pixelXInTile * 2;

                imageData[pixelPairIndex] = (byte)pixel1;
                imageData[pixelPairIndex + 1] = (byte)pixel2;
            }

            tilesetImageWidth = tilesWide * 8;
            tilesetImageHeight = tilesTall * 8;

            return imageData;
        }

        // Expects reader to be at tileset offset
        internal static Tileset ReadTileset(PokemonRomReader reader, List<Palette> sharedPalettes)
        {
            var tileset = new Tileset(sharedPalettes);

            tileset.isCompressed = reader.ReadBool();
            tileset.isSecondary = reader.ReadBool();

            reader.Skip(2);

            tileset.bitmap = reader.ReadPointer();
            tileset.palette = reader.ReadPointer();
            tileset.Metatiles = reader.ReadPointer();

            if (reader.Constants.Code.StartsWith("BP"))
            {
                // FireRed layout: callback, then metatileAttributes
                tileset.AnimationCallback = reader.ReadPointer();
                tileset.MetatileAttributes = reader.ReadPointer();
            }
            else
            {
                // Ruby layout: metatileAttributes, then callback
                tileset.MetatileAttributes = reader.ReadPointer();
                tileset.AnimationCallback = reader.ReadPointer();
            }

            int totalMetatileDataLength = tileset.MetatileAttributes.Distance(tileset.Metatiles);
            tileset.MetatileCount = totalMetatileDataLength / Metatile.DATA_LENGTH;

            var start = 0;

            if (tileset.isSecondary)
                start = 7;

            for (int i = start; i < PALETTE_COUNT; i++)
            {
                reader.GoToPointer(tileset.palette.OffsetBy(reader.GetSizeOf<Palette>(i)));

                var palette = reader.Read<Palette>();
                tileset.Palettes[i] = palette;
            }

            reader.GoToPointer(tileset.bitmap);
            if (tileset.isCompressed)
                tileset.PixelValues = reader.ReadCompressedData();
            else
                tileset.PixelValues = reader.ReadBytes(totalMetatileDataLength);

            return tileset;
        }

        internal static List<Palette> CreateSharedPalettesList()
        {
            var palettes = new List<Palette>(PALETTE_COUNT);

            for (int i = 0; i < PALETTE_COUNT; i++)
            {
                palettes.Add(default);
            }

            return palettes;
        }
    }
}
