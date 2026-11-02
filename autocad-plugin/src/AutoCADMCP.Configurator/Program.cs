using System.Text;
using System.Text.Json;

namespace AutoCADMCP.Configurator;

/// <summary>
/// KEMET: connects AI apps to the AutoCAD MCP server. Ported from the Kemet fork of NavisHelper's
/// configurator (MIT, see NOTICE.md) so Kemet AutoCAD Addons can use the same command line:
///
///   --status                              one tab-separated line per AI app (see StatusCommand)
///   --configure --clients &lt;ids|all&gt;  add the "autocad-mcp" entry
///   --remove    --clients &lt;ids|all&gt;  remove it
///   --detect                              human-readable detection (default with no action)
///   --dry-run, --create-missing, --mcp-server &lt;path&gt;
///
/// Every write backs the config up first ("&lt;config&gt;.bak_autocadmcp_&lt;utc&gt;"), replaces the
/// file atomically, and leaves every other server entry untouched.
/// Output lines "&lt;id&gt;: &lt;status&gt; - &lt;detail&gt;" are what the Kemet window parses.
/// </summary>
internal static partial class Program
{
    /// <summary>The MCP server entry name written into every AI app's config.</summary>
    internal const string ServerName = "autocad-mcp";

    /// <summary>The server executable this configurator registers.</summary>
    private const string ServerExeName = "autocad-mcp-server.exe";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            if (args.Length == 1 && args[0].Equals("--status", StringComparison.OrdinalIgnoreCase))
                return StatusCommand.Run(StatusViews(BuildAdapters()), Console.Out);

            var options = Options.Parse(args);
            if (options.ShowHelp)
            {
                PrintHelp();
                return 0;
            }

            if (options.Configure && options.Remove)
                throw new InvalidOperationException("Use either --configure or --remove, not both.");

            var mcpServerPath = options.Remove ? string.Empty : ResolveMcpServerPath(options.McpServerPath);
            var selected = SelectAdapters(BuildAdapters(), options.Clients);
            var hadErrors = false;

            if (options.Detect || (!options.Configure && !options.Remove))
                foreach (var adapter in selected)
                    hadErrors |= !PrintDetection(adapter, mcpServerPath);

            if (options.Configure)
                foreach (var adapter in selected)
                    hadErrors |= !Report(adapter, () => adapter.Configure(mcpServerPath, options.DryRun, options.CreateMissing));

            if (options.Remove)
                foreach (var adapter in selected)
                    hadErrors |= !Report(adapter, () => adapter.Remove(options.DryRun));

            return hadErrors ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    private static IReadOnlyList<IMcpClientAdapter> BuildAdapters()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return new IMcpClientAdapter[]
        {
            new GenericMcpServersJsonAdapter("claude-desktop", "Claude Desktop",
                Path.Combine(ClaudeDesktopRoot(appData), "claude_desktop_config.json"),
                ClaudeDesktopRoot(appData)),
            new ClaudeCodeCliAdapter(),
            new CodexTomlAdapter(Path.Combine(userProfile, ".codex", "config.toml"), Path.Combine(userProfile, ".codex")),
            new GenericMcpServersJsonAdapter("cursor", "Cursor",
                Path.Combine(userProfile, ".cursor", "mcp.json"), Path.Combine(userProfile, ".cursor")),
            new OpenCodeJsonAdapter(Path.Combine(appData, "OpenCode", "opencode.json"), Path.Combine(appData, "OpenCode")),
            new GenericMcpServersJsonAdapter("kimi", "Kimi Code",
                Path.Combine(userProfile, ".kimi-code", "mcp.json"), Path.Combine(userProfile, ".kimi-code")),
            new ZCodeCliJsonAdapter(Path.Combine(userProfile, ".zcode", "cli", "config.json"), Path.Combine(userProfile, ".zcode")),
            new VsCodeServersJsonAdapter(Path.Combine(appData, "Code", "User", "mcp.json"), Path.Combine(appData, "Code", "User")),
            new GenericMcpServersJsonAdapter("windsurf", "Windsurf",
                Path.Combine(userProfile, ".codeium", "windsurf", "mcp_config.json"),
                Path.Combine(userProfile, ".codeium", "windsurf")),
            new GeminiCliJsonAdapter(Path.Combine(userProfile, ".gemini", "settings.json"), Path.Combine(userProfile, ".gemini")),
            AntigravityAdapter(userProfile),
        };
    }

    private static IReadOnlyList<IMcpClientAdapter> SelectAdapters(IReadOnlyList<IMcpClientAdapter> adapters, string clients)
    {
        if (string.IsNullOrWhiteSpace(clients) || string.Equals(clients, "all", StringComparison.OrdinalIgnoreCase))
            return adapters;

        var requested = clients
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unknown = requested
            .Where(id => adapters.All(adapter => !string.Equals(adapter.Id, id, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException("Unknown client id(s): " + string.Join(", ", unknown));

        return adapters.Where(adapter => requested.Contains(adapter.Id)).ToList();
    }

    /// <summary>
    /// The server to register: --mcp-server if given, else autocad-mcp-server.exe next to this
    /// configurator, in a sibling "McpServer" folder (the per-user install layout), or in the
    /// standard per-user install folder.
    /// </summary>
    private static string ResolveMcpServerPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var resolved = Path.GetFullPath(explicitPath);
            if (!File.Exists(resolved))
                throw new FileNotFoundException("MCP server executable was not found.", resolved);
            return resolved;
        }

        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, ServerExeName),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "McpServer", ServerExeName)),
            Path.Combine(baseDirectory, "..", "server", ServerExeName),
            Path.Combine(StandardInstallRoot, "McpServer", ServerExeName),
        };
        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (File.Exists(full)) return full;
        }

        throw new FileNotFoundException(
            ServerExeName + " was not found next to the configurator or in " +
            Path.Combine(StandardInstallRoot, "McpServer") + ". Pass --mcp-server with an explicit path.");
    }

    /// <summary>%LocalAppData%\KemetAutoCAD: where Kemet AutoCAD Addons installs the server and this tool.</summary>
    internal static string StandardInstallRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KemetAutoCAD");

    private static bool PrintDetection(IMcpClientAdapter adapter, string mcpServerPath)
    {
        try
        {
            var detection = adapter.Detect();
            Console.WriteLine($"{adapter.Id}: {detection.Status} - {detection.Detail}");
            if (!string.IsNullOrWhiteSpace(detection.ConfigPath))
                Console.WriteLine($"  config: {detection.ConfigPath}");
            if (!string.IsNullOrWhiteSpace(mcpServerPath))
                Console.WriteLine($"  server: {mcpServerPath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{adapter.Id}: failed - {ex.Message}");
            return false;
        }
    }

    /// <summary>Runs one configure/remove and prints "&lt;id&gt;: &lt;status&gt; - &lt;detail&gt;".</summary>
    private static bool Report(IMcpClientAdapter adapter, Func<ConfigureResult> action)
    {
        try
        {
            var result = action();
            var isFailure = string.Equals(result.Status, "failed", StringComparison.OrdinalIgnoreCase);
            var output = isFailure ? Console.Error : Console.Out;
            output.WriteLine($"{adapter.Id}: {result.Status} - {result.Detail}");
            if (!string.IsNullOrWhiteSpace(result.BackupPath))
                output.WriteLine($"  backup: {result.BackupPath}");
            return !isFailure;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{adapter.Id}: failed - {ex.Message}");
            return false;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("AutoCAD MCP Configurator (Kemet)");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  autocad-mcp-configurator.exe --status");
        Console.WriteLine("  autocad-mcp-configurator.exe --detect");
        Console.WriteLine("  autocad-mcp-configurator.exe --configure --clients claude-desktop,cursor [--dry-run] [--create-missing]");
        Console.WriteLine("  autocad-mcp-configurator.exe --configure --clients all --mcp-server \"C:\\path\\to\\autocad-mcp-server.exe\"");
        Console.WriteLine("  autocad-mcp-configurator.exe --remove --clients all");
        Console.WriteLine();
        Console.WriteLine("Client ids:");
        Console.WriteLine("  claude-desktop, claude-code, codex, cursor, opencode, kimi, zcode, vscode, windsurf, gemini-cli, antigravity");
        Console.WriteLine();
        Console.WriteLine("Server entry name: " + ServerName);
    }
}
