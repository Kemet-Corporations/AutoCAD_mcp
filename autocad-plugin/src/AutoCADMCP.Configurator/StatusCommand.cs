using System.Text.Json.Nodes;

namespace AutoCADMCP.Configurator;

// Ported from NavisHelper.McpConfigurator (StatusCommand.cs, Kemet fork). See NOTICE.md.
//
// "--status" prints one tab-separated line per AI app for the Kemet AutoCAD Addons window, which
// runs inside AutoCAD (.NET Framework on 2021-2024) and should not need a JSON parser:
//
//   status<TAB>1
//   <id><TAB><display name><TAB><installed: yes|no><TAB><state><TAB><config path or empty>
//
// state: connected | not_connected | unreadable (config exists but could not be read)
// Read-only: it never writes a file or starts a process.
internal static partial class Program
{
    internal static class StatusCommand
    {
        internal static int Run(IReadOnlyList<IMcpClientAdapterView> adapters, TextWriter output)
        {
            output.WriteLine("status\t1");
            foreach (var adapter in adapters)
            {
                string installed, state, configPath;
                try
                {
                    (installed, state, configPath) = Inspect(adapter);
                }
                catch
                {
                    (installed, state, configPath) = ("yes", "unreadable", "");
                }
                output.WriteLine(string.Join("\t", adapter.Id, Clean(adapter.Name), installed, state, Clean(configPath)));
            }
            return 0;
        }

        private static (string Installed, string State, string ConfigPath) Inspect(IMcpClientAdapterView adapter)
        {
            if (adapter.Id == "claude-code")
            {
                // `claude mcp add --scope user` writes the top-level "mcpServers" of ~/.claude.json.
                var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
                // Connecting runs the `claude` CLI, so only a CLI on PATH counts as installed.
                return (adapter.CliFound ? "yes" : "no", HasJsonEntry(configPath, "mcpServers"), configPath);
            }

            var path = adapter.ConfigPath ?? "";
            var state = adapter.Id switch
            {
                "codex" => HasCodexEntry(path),
                "vscode" => HasJsonEntry(path, "servers"),
                "opencode" => HasJsonEntry(path, "mcp"),
                "zcode" => HasZCodeEntry(path),
                _ => HasJsonEntry(path, "mcpServers"),
            };
            return (adapter.Installed ? "yes" : "no", state, path);
        }

        private static string HasJsonEntry(string path, string container)
        {
            if (!File.Exists(path)) return "not_connected";
            try
            {
                var root = ReadJsonObject(path);
                return root[container] is JsonObject servers && servers.ContainsKey(ServerName)
                    ? "connected" : "not_connected";
            }
            catch { return "unreadable"; }
        }

        private static string HasZCodeEntry(string path)
        {
            if (!File.Exists(path)) return "not_connected";
            try
            {
                var root = ReadJsonObject(path);
                return root["mcp"] is JsonObject mcp && mcp["servers"] is JsonObject servers &&
                       servers.ContainsKey(ServerName) ? "connected" : "not_connected";
            }
            catch { return "unreadable"; }
        }

        private static string HasCodexEntry(string path)
        {
            if (!File.Exists(path)) return "not_connected";
            try
            {
                return CodexTomlConfig.ReadServer(File.ReadAllText(path)) != null ? "connected" : "not_connected";
            }
            catch { return "unreadable"; }
        }

        // Tabs and line breaks would break the line format.
        private static string Clean(string value) =>
            (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }

    /// <summary>What --status needs from an adapter, whatever its kind.</summary>
    internal interface IMcpClientAdapterView
    {
        string Id { get; }
        string Name { get; }
        bool Installed { get; }
        bool CliFound { get; }
        string? ConfigPath { get; }
    }

    private static IReadOnlyList<IMcpClientAdapterView> StatusViews(IEnumerable<IMcpClientAdapter> adapters) =>
        adapters.Select(adapter => (IMcpClientAdapterView)(adapter switch
        {
            FileAdapter file => new AdapterView(file.Id, file.Name, file.ClientInstalled, false, file.ReadOnlyConfigPath),
            CliAdapter cli => new AdapterView(cli.Id, cli.Name, cli.ExecutablePath != null, cli.ExecutablePath != null, null),
            _ => new AdapterView(adapter.Id, adapter.Id, false, false, null),
        })).ToList();

    private sealed record AdapterView(string Id, string Name, bool Installed, bool CliFound, string? ConfigPath)
        : IMcpClientAdapterView;
}
