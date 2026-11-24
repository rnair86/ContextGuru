using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ContextGuru.Install;

/// <summary>Registers the server in the Codex configuration by running the Codex CLI, which owns the TOML file.</summary>
/// <param name="codexPath">Absolute path of the codex executable.</param>
/// <param name="home">The CODEX_HOME folder, always passed to codex so it never touches another configuration.</param>
sealed class CodexConfig(string codexPath, string home)
{
    const string ServerName = "contextguru";

    /// <summary>Finds codex by hand in the fully qualified PATH entries, so a codex in the current folder is never run.</summary>
    public static string Find(string? searchPath)
    {
        // npm installs a .cmd, the standalone installer an .exe.
        string[] names = OperatingSystem.IsWindows() ? ["codex.exe", "codex.cmd"] : ["codex"];
        var folders = (searchPath ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var folder in folders.Where(Path.IsPathFullyQualified))
        {
            foreach (var name in names)
            {
                var path = Path.Combine(folder, name);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }
        throw new InstallerException("Could not find codex on the PATH. Install the Codex CLI and try again.");
    }

    /// <summary>True when the server is already registered as a stdio server that runs "contextguru" with no arguments.</summary>
    public bool IsRegistered()
    {
        var (code, stdout, _) = Run("mcp", "get", ServerName, "--json");
        if (code != 0)
        {
            return false;
        }
        // Expected shape: { "transport": { "type": string, and for "stdio" also "command": string, "args": array } }.
        // Anything else is an error, not "not registered", so the config is not rewritten on output we do not understand.
        // The library throws JsonException for bad JSON and for duplicate keys (at the parse, see ParseOptions), and
        // InvalidOperationException when a string cannot be decoded (a lone surrogate escape): at the parse for a key, at TryGetValue for a value.
        try
        {
            var root = JsonNode.Parse(stdout, documentOptions: Installer.ParseOptions);
            if (root is not JsonObject { } obj || obj["transport"] is not JsonObject transport || AsString(transport["type"]) is not { } type)
            {
                throw Unreadable();
            }
            if (type != "stdio")
            {
                return false;
            }
            if (AsString(transport["command"]) is not { } command || transport["args"] is not JsonArray args)
            {
                throw Unreadable();
            }
            return command == ServerName && args.Count == 0;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            throw Unreadable();
        }
    }

    static string? AsString(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    static InstallerException Unreadable() => new("Could not read the output of `codex mcp get`.");

    public void Add() => RunChecked("mcp", "add", ServerName, "--", ServerName);

    public void Remove() => RunChecked("mcp", "remove", ServerName);

    void RunChecked(params string[] args)
    {
        var (code, _, stderr) = Run(args);
        if (code != 0)
        {
            // codex normally explains itself on stderr. Without it, name the call and the exit code.
            throw new InstallerException(stderr.Trim() is { Length: > 0 } message ? message : $"codex {args[0]} {args[1]} failed with exit code {code}.");
        }
    }

    // Both streams are captured, so nothing from codex reaches our stdout.
    (int Code, string Stdout, string Stderr) Run(params string[] args)
    {
        var info = new ProcessStartInfo(codexPath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = home,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }
        info.Environment["CODEX_HOME"] = home;

        Process process;
        try
        {
            process = Process.Start(info)!;
        }
        catch (Win32Exception e)
        {
            throw new InstallerException($"Could not start {codexPath}: {e.Message}");
        }
        using (process)
        {
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }
    }
}
