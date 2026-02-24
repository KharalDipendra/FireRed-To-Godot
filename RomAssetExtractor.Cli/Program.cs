using RomAssetExtractor.Cli.Utilities;
using RomAssetExtractor.Godot;
using RomAssetExtractor.Pokemon.Entities;
using DecompToGodot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RomAssetExtractor.Cli
{
    public class Program
    {
        public static string OutputDirectory { get; set; }
        public static string RomPath { get; set; }
        public static bool ShouldSaveBitmaps { get; set; }
        public static bool ShouldSaveTrainers { get; set; }
        public static bool ShouldSaveMaps { get; set; }
        public static bool ShouldSaveMapRenders { get; set; }
        public static bool ShouldSaveGodotMaps { get; set; }

        // ── DecompToGodot options ──
        public static string DecompPath { get; set; }
        public static bool ShouldDecompToGodot { get; set; }
        public static bool ShouldSaveSpawns { get; set; }

        static void Main(string[] args)
        {
            Console.WriteLine("ROM Asset Extractor CLI");
            Console.WriteLine("for Gen-III Pokemon games + Decomp-to-Godot conversion");
            Console.WriteLine("\tOriginal by TheJjokerR: https://github.com/thejjokerr/");
            Console.WriteLine();
            Console.WriteLine("\tSome logic and constants have been taken from these open-source projects:");
            Console.WriteLine("\t- Jugales: https://pokewebkit.com/start");
            Console.WriteLine("\t- Magical: https://github.com/magical/pokemon-gba-sprites/");
            Console.WriteLine("\t- Kyoufu Kawa: https://www.romhacking.net/utilities/463/");
            Console.WriteLine();
            Console.Write(Command.GetAllHelpTexts());

            try
            {
                Command.HandleArguments(args);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine(ex.Message);
                try { Console.ReadKey(); } catch { }
                return;
            }

            // ── ROM extraction (original functionality) ──
            if (!string.IsNullOrEmpty(RomPath) && File.Exists(RomPath))
            {
                Action<Bank[], string> godotExporter = ShouldSaveGodotMaps
                    ? (Action<Bank[], string>)GodotMapExporter.ExportAllMaps
                    : null;

                AssetExtractor.ExtractRom(RomPath, OutputDirectory, ShouldSaveBitmaps, ShouldSaveTrainers, ShouldSaveMaps, ShouldSaveMapRenders, mapPostProcessor: godotExporter).GetAwaiter().GetResult();

                Console.WriteLine("ROM extraction finished.");
                Console.WriteLine();
            }

            // ── Decomp-to-Godot conversion ──
            if (ShouldDecompToGodot && !string.IsNullOrEmpty(DecompPath))
            {
                Console.WriteLine("Starting Decomp-to-Godot conversion...");
                Console.WriteLine();

                try
                {
                    var converter = new Converter(DecompPath, OutputDirectory, mapFilter: null);
                    converter.SaveSpawns = ShouldSaveSpawns;
                    converter.Run();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Decomp-to-Godot error: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }

                Console.WriteLine();
            }

            Console.WriteLine("Completely finished.");
            Console.WriteLine();
            Console.WriteLine("Press any key to close");
            try { Console.ReadKey(); } catch { }
        }
    }
}
