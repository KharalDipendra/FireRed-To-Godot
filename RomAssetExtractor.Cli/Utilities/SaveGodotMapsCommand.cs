using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RomAssetExtractor.Cli.Utilities
{
    class SaveGodotMapsCommand : Command
    {
        private const bool DEFAULT_SAVE = false;

        private static Command instance;
        public static Command Instance
        {
            get
            {
                if (instance == null)
                    instance = new SaveGodotMapsCommand();

                return instance;
            }
        }

        private bool shouldSave;

        public override string[] GetCommands() => new[]
        {
            "--save-godot-maps",
            "-sgm",
        };

        public override void AppendHelpText(StringBuilder helpTextBuilder, string tabs)
        {
            helpTextBuilder.Append(tabs);
            helpTextBuilder.AppendLine("Export maps as Godot 4 TileSet (.tres) and TileMap scene (.tscn) files.");
            helpTextBuilder.Append(tabs);
            helpTextBuilder.AppendLine("Generates ready-to-use Godot scenes with Ground and Overlay TileMapLayers.");
            helpTextBuilder.Append(tabs);
            helpTextBuilder.Append("Defaults to: ");
            helpTextBuilder.Append(DEFAULT_SAVE);
        }

        public override int GetArgumentCount()
            => 1;

        public override bool GetIsRequired()
            => false;

        public override void Consume(string argument)
            => shouldSave = argument == "1" ? true
            : (argument == "0" ? false
                : bool.Parse(argument));

        public override bool Execute()
        {
            Program.ShouldSaveGodotMaps = shouldSave;
            return true;
        }

        public override bool ExecuteDefault()
        {
            Program.ShouldSaveGodotMaps = DEFAULT_SAVE;
            return true;
        }
    }
}
