using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RomAssetExtractor.Cli.Utilities
{
    class RomPathCommand : Command
    {
        private static Command instance;
        public static Command Instance
        {
            get
            {
                if (instance == null)
                    instance = new RomPathCommand();

                return instance;
            }
        }

        private string romArgument;

        public override string[] GetCommands() => new[]
        {
            "--rom",
            "-r",
        };

        public override void AppendHelpText(StringBuilder helpTextBuilder, string tabs)
        {
            helpTextBuilder.Append(tabs);
            helpTextBuilder.Append("The path to the Pokemon ROM to extract assets from (optional if using --decomp-to-godot).");
        }

        public override int GetArgumentCount()
            => 1;
        public override bool GetIsRequired()
            => false;

        public override void Consume(string argument)
            => romArgument = argument;

        public override bool Execute()
        {
            if (string.IsNullOrWhiteSpace(romArgument))
                throw new ArgumentException("No ROM path specified!");

            if (!File.Exists(romArgument))
                throw new ArgumentException("Invalid ROM path specified!");

            Program.RomPath = romArgument;
            return true;
        }

        public override bool ExecuteDefault()
        {
            // ROM path is optional now — decomp-to-godot works without it
            Program.RomPath = null;
            return true;
        }
    }
}
