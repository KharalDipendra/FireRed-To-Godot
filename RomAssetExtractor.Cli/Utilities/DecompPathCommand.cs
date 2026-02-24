using System;
using System.IO;
using System.Text;

namespace RomAssetExtractor.Cli.Utilities
{
    class DecompPathCommand : Command
    {
        private static Command instance;
        public static Command Instance
        {
            get
            {
                if (instance == null)
                    instance = new DecompPathCommand();

                return instance;
            }
        }

        private string decompArgument;

        public override string[] GetCommands() => new[]
        {
            "--decomp",
            "-d",
        };

        public override void AppendHelpText(StringBuilder helpTextBuilder, string tabs)
        {
            helpTextBuilder.Append(tabs);
            helpTextBuilder.AppendLine("Path to a pokefirered (or similar Gen 3) decomp project root.");
            helpTextBuilder.Append(tabs);
            helpTextBuilder.Append("Required when using --decomp-to-godot.");
        }

        public override int GetArgumentCount()
            => 1;

        public override bool GetIsRequired()
            => false;

        public override void Consume(string argument)
            => decompArgument = argument;

        public override bool Execute()
        {
            if (string.IsNullOrWhiteSpace(decompArgument))
                throw new ArgumentException("No decomp path specified!");

            if (!Directory.Exists(decompArgument))
                throw new ArgumentException($"Decomp path not found: {decompArgument}");

            Program.DecompPath = Path.GetFullPath(decompArgument);
            return true;
        }

        public override bool ExecuteDefault()
        {
            Program.DecompPath = null;
            return true;
        }
    }
}
