using System.Text.Json.Nodes;

namespace AutoCADMCP.Configurator;

// Ported from NavisHelper.McpConfigurator (Program.FileAdapters.cs, Program.CliAdapters.cs,
// KemetClientAdapters.cs, ZCodeConfig.cs). See NOTICE.md.
internal static partial class Program
{
    internal interface IMcpClientAdapter
    {
        string Id { get; }
        DetectionResult Detect();
        ConfigureResult Configure(string mcpServerPath, bool dryRun, bool createMissing);
        ConfigureResult Remove(bool dryRun);
    }

    internal sealed record DetectionResult(string Status, string Detail, string? ConfigPath = null);

    internal sealed record ConfigureResult(string Status, string Detail, string? BackupPath = null);

    // ---------------------------------------------------------------------------------------
    // Apps configured through a file
    // ---------------------------------------------------------------------------------------

    internal abstract class FileAdapter : IMcpClientAdapter
    {
        protected FileAdapter(string id, string displayName, string configPath, string clientRootPath)
        {
            Id = id;
            DisplayName = displayName;
            ConfigPath = configPath;
            ClientRootPath = clientRootPath;
        }

        public string Id { get; }
        protected string DisplayName { get; }
        protected string ConfigPath { get; }
        protected string ClientRootPath { get; }
        internal string Name => DisplayName;
        internal string ReadOnlyConfigPath => ConfigPath;
        internal virtual bool ClientInstalled => Directory.Exists(ClientRootPath);

        public virtual DetectionResult Detect()
        {
            if (!Directory.Exists(ClientRootPath))
                return new DetectionResult("missing", DisplayName + " client directory was not found; it will be skipped.", ConfigPath);

            return File.Exists(ConfigPath)
                ? new DetectionResult("found", DisplayName + " config found.", ConfigPath)
                : new DetectionResult("missing", DisplayName + " config not found. It will be created when configuring.", ConfigPath);
        }

        public ConfigureResult Configure(string mcpServerPath, bool dryRun, bool createMissing)
        {
            if (!Directory.Exists(ClientRootPath) && !createMissing)
                return new ConfigureResult("skipped",
                    $"{DisplayName} client directory was not found. Re-run with --create-missing to create {Path.GetDirectoryName(ConfigPath)}.");

            if (dryRun)
                return new ConfigureResult("dry-run", $"Would configure {DisplayName} at {ConfigPath}.");

            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var backupPath = BackupIfExists(ConfigPath);
            ConfigureFile(mcpServerPath);
            return new ConfigureResult("configured", $"{DisplayName} now has MCP server '{ServerName}'.", backupPath);
        }

        public ConfigureResult Remove(bool dryRun)
        {
            if (!File.Exists(ConfigPath))
                return new ConfigureResult("skipped", $"{DisplayName} config was not found.");

            if (dryRun)
                return new ConfigureResult("dry-run", $"Would remove MCP server '{ServerName}' from {DisplayName} at {ConfigPath}.");

            var backupPath = BackupIfExists(ConfigPath);
            return RemoveFileEntry()
                ? new ConfigureResult("removed", $"{DisplayName} no longer has MCP server '{ServerName}'.", backupPath)
                : new ConfigureResult("skipped", $"{DisplayName} config did not contain MCP server '{ServerName}'.", backupPath);
        }

        protected abstract void ConfigureFile(string mcpServerPath);

        protected abstract bool RemoveFileEntry();

        protected static string? BackupIfExists(string path)
        {
            if (!File.Exists(path))
                return null;

            var backupPath = path + ".bak_autocadmcp_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            File.Copy(path, backupPath, overwrite: false);
            return backupPath;
        }
    }

    /// <summary>Claude Desktop, Cursor, Kimi Code, Windsurf, Antigravity: "mcpServers" → { command, args }.</summary>
    internal sealed class GenericMcpServersJsonAdapter : FileAdapter
    {
        public GenericMcpServersJsonAdapter(string id, string displayName, string configPath, string clientRootPath)
            : base(id, displayName, configPath, clientRootPath) { }

        protected override void ConfigureFile(string mcpServerPath)
        {
            var root = ReadJsonObject(ConfigPath);
            var server = EnsureObject(EnsureObject(root, "mcpServers"), ServerName);
            server["command"] = mcpServerPath;
            server["args"] = new JsonArray();
            WriteJson(ConfigPath, root);
        }

        protected override bool RemoveFileEntry()
        {
            var root = ReadJsonObject(ConfigPath);
            if (root["mcpServers"] is not JsonObject servers || !servers.Remove(ServerName))
                return false;
            WriteJson(ConfigPath, root);
            return true;
        }
    }

    /// <summary>opencode: "mcp" → { type: local, command: [exe], enabled }.</summary>
    internal sealed class OpenCodeJsonAdapter : FileAdapter
    {
        public OpenCodeJsonAdapter(string configPath, string clientRootPath)
            : base("opencode", "opencode", configPath, clientRootPath) { }

        protected override void ConfigureFile(string mcpServerPath)
        {
            var root = ReadJsonObject(ConfigPath);
            root["$schema"] ??= "https://opencode.ai/config.json";
            var server = EnsureObject(EnsureObject(root, "mcp"), ServerName);
            server["type"] = "local";
            server["command"] = new JsonArray(mcpServerPath);
            server["enabled"] = true;
            WriteJson(ConfigPath, root);
        }

        protected override bool RemoveFileEntry()
        {
            var root = ReadJsonObject(ConfigPath);
            if (root["mcp"] is not JsonObject mcp || !mcp.Remove(ServerName))
                return false;
            WriteJson(ConfigPath, root);
            return true;
        }
    }

    /// <summary>ZCode CLI: "mcp" → "servers" → { command, args }.</summary>
    internal sealed class ZCodeCliJsonAdapter : FileAdapter
    {
        public ZCodeCliJsonAdapter(string configPath, string clientRootPath)
            : base("zcode", "ZCode", configPath, clientRootPath) { }

        protected override void ConfigureFile(string mcpServerPath)
        {
            var root = ReadJsonObject(ConfigPath);
            var server = EnsureObject(EnsureObject(EnsureObject(root, "mcp"), "servers"), ServerName);
            server["command"] = mcpServerPath;
            server["args"] = new JsonArray();
            WriteJson(ConfigPath, root);
        }

        protected override bool RemoveFileEntry()
        {
            var root = ReadJsonObject(ConfigPath);
            if ((root["mcp"] as JsonObject)?["servers"] is not JsonObject servers || !servers.Remove(ServerName))
                return false;
            WriteJson(ConfigPath, root);
            return true;
        }
    }

    /// <summary>Codex: ~/.codex/config.toml, [mcp_servers.autocad-mcp]; edited without touching anything else.</summary>
    internal sealed class CodexTomlAdapter : FileAdapter
    {
        public CodexTomlAdapter(string configPath, string clientRootPath)
            : base("codex", "Codex", configPath, clientRootPath) { }

        protected override void ConfigureFile(string mcpServerPath)
        {
            var original = CodexTomlConfig.ReadFile(ConfigPath);
            var updated = CodexTomlConfig.Configure(original, mcpServerPath);
            if (!string.Equals(original, updated, StringComparison.Ordinal))
                WriteTextAtomic(ConfigPath, updated);
        }

        protected override bool RemoveFileEntry()
        {
            var original = CodexTomlConfig.ReadFile(ConfigPath);
            var updated = CodexTomlConfig.Remove(original);
            if (string.Equals(original, updated, StringComparison.Ordinal))
                return false;
            WriteTextAtomic(ConfigPath, updated);
            return true;
        }
    }

    /// <summary>VS Code: %APPDATA%\Code\User\mcp.json, "servers" → { type: stdio, command, args }.</summary>
    internal sealed class VsCodeServersJsonAdapter : FileAdapter
    {
        public VsCodeServersJsonAdapter(string configPath, string clientRootPath)
            : base("vscode", "VS Code", configPath, clientRootPath) { }

        protected override void ConfigureFile(string mcpServerPath)
        {
            var root = ReadJsonObject(ConfigPath);
            var server = EnsureObject(EnsureObject(root, "servers"), ServerName);
            server["type"] = "stdio";
            server["command"] = mcpServerPath;
            server["args"] = new JsonArray();
            WriteJson(ConfigPath, root);
        }

        protected override bool RemoveFileEntry()
        {
            var root = ReadJsonObject(ConfigPath);
            if (root["servers"] is not JsonObject servers || !servers.Remove(ServerName))
                return false;
            WriteJson(ConfigPath, root);
            return true;
        }
    }

    /// <summary>
    /// Gemini CLI: ~/.gemini/settings.json, "mcpServers". ~/.gemini is shared with Antigravity, so
    /// Gemini CLI counts as installed only when its settings file exists.
    /// </summary>
    internal sealed class GeminiCliJsonAdapter : FileAdapter
    {
        public GeminiCliJsonAdapter(string configPath, string clientRootPath)
            : base("gemini-cli", "Gemini CLI", configPath, clientRootPath) { }

        internal override bool ClientInstalled => File.Exists(ConfigPath);

        protected override void ConfigureFile(string mcpServerPath)
        {
            var root = ReadJsonObject(ConfigPath);
            var server = EnsureObject(EnsureObject(root, "mcpServers"), ServerName);
            server["command"] = mcpServerPath;
            server["args"] = new JsonArray();
            WriteJson(ConfigPath, root);
        }

        protected override bool RemoveFileEntry()
        {
            var root = ReadJsonObject(ConfigPath);
            if (root["mcpServers"] is not JsonObject servers || !servers.Remove(ServerName))
                return false;
            WriteJson(ConfigPath, root);
            return true;
        }
    }

    /// <summary>
    /// Antigravity 2.x uses ~/.gemini/config/mcp_config.json; the first release used
    /// ~/.gemini/antigravity/mcp_config.json, which is used only when the 2.x folder is absent.
    /// </summary>
    private static IMcpClientAdapter AntigravityAdapter(string userProfile)
    {
        var v2 = Path.Combine(userProfile, ".gemini", "config");
        var v1 = Path.Combine(userProfile, ".gemini", "antigravity");
        var root = Directory.Exists(v2) || !Directory.Exists(v1) ? v2 : v1;
        return new GenericMcpServersJsonAdapter("antigravity", "Antigravity", Path.Combine(root, "mcp_config.json"), root);
    }

    /// <summary>
    /// Claude Desktop's settings folder. The Microsoft Store (MSIX) version keeps a private copy of
    /// %APPDATA%\Claude under %LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude and reads that
    /// one first, so an entry written to %APPDATA%\Claude is never seen. When that folder exists it is
    /// used (the most recently written one if there are several); otherwise %APPDATA%\Claude.
    /// </summary>
    internal static string ClaudeDesktopRoot(string appData)
    {
        var standard = Path.Combine(appData, "Claude");
        try
        {
            var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            if (!Directory.Exists(packages))
                return standard;
            var store = Directory.EnumerateDirectories(packages, "Claude_*")
                .Select(package => Path.Combine(package, "LocalCache", "Roaming", "Claude"))
                .Where(Directory.Exists)
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .FirstOrDefault();
            return store ?? standard;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return standard;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Apps configured through their own CLI
    // ---------------------------------------------------------------------------------------

    internal abstract class CliAdapter : IMcpClientAdapter
    {
        protected CliAdapter(string id, string displayName, string executable)
        {
            Id = id;
            DisplayName = displayName;
            Executable = executable;
        }

        public string Id { get; }
        protected string DisplayName { get; }
        protected string Executable { get; }
        internal string Name => DisplayName;
        internal string? ExecutablePath => FindExecutable(Executable);

        public DetectionResult Detect()
        {
            var command = FindExecutable(Executable);
            return command == null
                ? new DetectionResult("missing", DisplayName + " CLI was not found in PATH.")
                : new DetectionResult("found", DisplayName + " CLI found: " + command);
        }

        public ConfigureResult Configure(string mcpServerPath, bool dryRun, bool createMissing)
        {
            var command = FindExecutable(Executable);
            if (command == null)
                return new ConfigureResult("skipped", DisplayName + " CLI was not found.");

            var removeArgs = BuildRemoveArguments().ToList();
            var addArgs = BuildAddArguments(mcpServerPath).ToList();
            if (dryRun)
                return new ConfigureResult("dry-run",
                    $"Would run: {command} {string.Join(" ", removeArgs.Select(QuoteForDisplay))}; then {command} {string.Join(" ", addArgs.Select(QuoteForDisplay))}");

            var removeResult = RunProcess(command, removeArgs);
            if (removeResult.ExitCode == -1)
                return new ConfigureResult("failed", DisplayName + " CLI remove timed out: " + removeResult.Output);

            var result = RunProcess(command, addArgs);
            return result.ExitCode == 0
                ? new ConfigureResult("configured", DisplayName + " accepted MCP server '" + ServerName + "'.")
                : new ConfigureResult("failed", DisplayName + " CLI failed: " + result.Output);
        }

        public ConfigureResult Remove(bool dryRun)
        {
            var command = FindExecutable(Executable);
            if (command == null)
                return new ConfigureResult("skipped", DisplayName + " CLI was not found.");

            var removeArgs = BuildRemoveArguments().ToList();
            if (dryRun)
                return new ConfigureResult("dry-run", $"Would run: {command} {string.Join(" ", removeArgs.Select(QuoteForDisplay))}");

            var result = RunProcess(command, removeArgs);
            if (result.ExitCode == -1)
                return new ConfigureResult("failed", DisplayName + " CLI remove timed out: " + result.Output);

            return result.ExitCode == 0
                ? new ConfigureResult("removed", DisplayName + " removed MCP server '" + ServerName + "'.")
                : new ConfigureResult("skipped", DisplayName + " CLI did not remove MCP server '" + ServerName + "': " + result.Output);
        }

        protected abstract IEnumerable<string> BuildRemoveArguments();

        protected abstract IEnumerable<string> BuildAddArguments(string mcpServerPath);
    }

    /// <summary>Claude Code: `claude mcp add --scope user autocad-mcp -- &lt;exe&gt;`.</summary>
    internal sealed class ClaudeCodeCliAdapter : CliAdapter
    {
        public ClaudeCodeCliAdapter() : base("claude-code", "Claude Code", "claude") { }

        protected override IEnumerable<string> BuildAddArguments(string mcpServerPath)
        {
            yield return "mcp";
            yield return "add";
            yield return "--scope";
            yield return "user";
            yield return ServerName;
            yield return "--";
            yield return mcpServerPath;
        }

        protected override IEnumerable<string> BuildRemoveArguments()
        {
            yield return "mcp";
            yield return "remove";
            yield return "--scope";
            yield return "user";
            yield return ServerName;
        }
    }
}
