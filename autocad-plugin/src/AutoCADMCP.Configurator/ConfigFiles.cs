using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace AutoCADMCP.Configurator;

// Ported from NavisHelper.McpConfigurator (Program.ConfigFileHelpers.cs, Program.Process.cs).
// See NOTICE.md.
internal static partial class Program
{
    internal static JsonObject ReadJsonObject(string path)
    {
        if (!File.Exists(path))
            return new JsonObject();
        return ParseJsonObject(File.ReadAllText(path, Encoding.UTF8));
    }

    internal static JsonObject ParseJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new JsonObject();

        var node = JsonNode.Parse(text);
        return node as JsonObject ?? throw new InvalidDataException("Client configuration root must be a JSON object.");
    }

    internal static JsonObject EnsureObject(JsonObject root, string propertyName)
    {
        if (root[propertyName] is JsonObject existing)
            return existing;

        if (root[propertyName] != null)
            throw new InvalidDataException("Client configuration property '" + propertyName + "' must be a JSON object.");

        var created = new JsonObject();
        root[propertyName] = created;
        return created;
    }

    private static void WriteJson(string path, JsonObject root) =>
        WriteTextAtomic(path, root.ToJsonString(JsonOptions) + Environment.NewLine);

    /// <summary>Writes to a temporary file in the same folder, then moves it over the target.</summary>
    internal static void WriteTextAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp_autocadmcp_" + Guid.NewGuid().ToString("N");
        var ownsTemporary = false;
        try
        {
            var bytes = new UTF8Encoding(false).GetBytes(content);
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporary = true;
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(tempPath, path, overwrite: true);
            ownsTemporary = false;
        }
        finally
        {
            if (ownsTemporary)
            {
                try { File.Delete(tempPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static string? FindExecutable(string name)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathExt = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.PS1")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);

        var candidates = new List<string> { name };
        if (!Path.HasExtension(name))
            candidates.AddRange(pathExt.Select(ext => name + ext));

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var candidate in candidates)
            {
                var fullPath = Path.Combine(directory.Trim(), candidate);
                if (File.Exists(fullPath))
                    return fullPath;
            }

        return null;
    }

    private static ProcessResult RunProcess(string fileName, IReadOnlyList<string> args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start " + fileName);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000))
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* Best effort cleanup after a stuck third-party CLI. */ }
            return new ProcessResult(-1, "Process timed out after 60000 ms.");
        }

        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        return new ProcessResult(process.ExitCode, (output + Environment.NewLine + error).Trim());
    }

    private static string QuoteForDisplay(string value) => value.Contains(' ') ? "\"" + value + "\"" : value;

    private sealed record ProcessResult(int ExitCode, string Output);
}
