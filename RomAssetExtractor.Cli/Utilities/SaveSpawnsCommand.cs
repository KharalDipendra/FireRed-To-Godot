using System;
using System.Text;

namespace RomAssetExtractor.Cli.Utilities
{
    class SaveSpawnsCommand : Command
    {
        private const bool DEFAULT_SAVE = false;

        private static Command instance;
        public static Command Instance
        {
            get
            {
                if (instance == null)
                    instance = new SaveSpawnsCommand();

                return instance;
            }
        }

        private bool shouldSave;

        public override string[] GetCommands() => new[]
        {
            "--save-spawns",
            "-ss",
        };

        public override void AppendHelpText(StringBuilder helpTextBuilder, string tabs)
        {
            helpTextBuilder.Append(tabs);
            helpTextBuilder.AppendLine("Save per-map Pokemon spawn/encounter data as JSON files.");
            helpTextBuilder.Append(tabs);
            helpTextBuilder.AppendLine("Reads wild_encounters.json from the decomp project (requires --decomp).");
            helpTextBuilder.Append(tabs);
            helpTextBuilder.AppendLine("Outputs spawns/<MapName>.json for each map with encounters,");
            helpTextBuilder.Append(tabs);
            helpTextBuilder.AppendLine("plus an all_spawns.json summary of all maps and their Pokemon.");
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
            Program.ShouldSaveSpawns = shouldSave;
            return true;
        }

        public override bool ExecuteDefault()
        {
            Program.ShouldSaveSpawns = DEFAULT_SAVE;
            return true;
        }
    }
}
