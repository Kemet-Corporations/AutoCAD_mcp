namespace AutoCADMCP.Configurator;

// Ported from NavisHelper.McpConfigurator (Program.Options.cs), without --doctor/--json.
// See NOTICE.md.
internal static partial class Program
{
    internal sealed record Options
    {
        public bool ShowHelp { get; private init; }
        public bool Detect { get; private init; }
        public bool Configure { get; private init; }
        public bool Remove { get; private init; }
        public bool DryRun { get; private init; }
        public bool CreateMissing { get; private init; }
        public string Clients { get; private init; } = "all";
        public string? McpServerPath { get; private init; }

        public static Options Parse(string[] args)
        {
            var options = new Options();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (Is(arg, "--help") || Is(arg, "-h") || Is(arg, "/?"))
                    options = options with { ShowHelp = true };
                else if (Is(arg, "--detect"))
                    options = options with { Detect = true };
                else if (Is(arg, "--configure"))
                    options = options with { Configure = true };
                else if (Is(arg, "--remove"))
                    options = options with { Remove = true };
                else if (Is(arg, "--dry-run"))
                    options = options with { DryRun = true };
                else if (Is(arg, "--create-missing"))
                    options = options with { CreateMissing = true };
                else if (Is(arg, "--clients"))
                    options = options with { Clients = ReadValue(args, ref i, arg) };
                else if (Is(arg, "--mcp-server"))
                    options = options with { McpServerPath = ReadValue(args, ref i, arg) };
                else
                    throw new InvalidOperationException("Unknown option: " + arg);
            }
            return options;
        }

        private static bool Is(string arg, string option) => string.Equals(arg, option, StringComparison.OrdinalIgnoreCase);

        private static string ReadValue(string[] args, ref int index, string optionName)
        {
            if (index + 1 >= args.Length)
                throw new InvalidOperationException("Missing value for " + optionName);
            index++;
            return args[index];
        }
    }
}
