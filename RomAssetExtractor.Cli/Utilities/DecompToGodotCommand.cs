using System;
using System.Text;

namespace RomAssetExtractor.Cli.Utilities
{
    class DecompToGodotCommand : Command
    {
        private const bool DEFAULT_SAVE = false;

        private static Command instance;
        public static Command Instance
        {
            get
            {
                if (instance == null)
                    instance = new DecompToGodotCommand();

                return instance;
            }
        }

        private bool shouldConvert;

        public override string[] GetCommands() => new[]
        {
            "--decomp-to-godot",
            "-dtg",
        };

        public override void AppendHelpText(StringBuilder helpTextBuilder, string tabs)
        {
            helpTextBuilder.Append(tabs);
            helpTextBuilder.AppendLine("Convert decomp project maps to Godot 4.3+ scenes with collision layers.");
            helpTextBuilder.Append(tabs);
            helpTextBuilder.AppendLine("Requires --decomp to specify the decomp project path.");
            helpTextBuilder.Append(tabs);
            helpTextBuilder.Append("Defaults to: ");
            helpTextBuilder.Append(DEFAULT_SAVE);
        }

        public override int GetArgumentCount()
            => 1;

        public override bool GetIsRequired()
            => false;

        public override void Consume(string argument)
            => shouldConvert = argument == "1" ? true
            : (argument == "0" ? false
                : bool.Parse(argument));

        public override bool Execute()
        {
            Program.ShouldDecompToGodot = shouldConvert;
            return true;
        }

        public override bool ExecuteDefault()
        {
            Program.ShouldDecompToGodot = DEFAULT_SAVE;
            return true;
        }
    }
}
