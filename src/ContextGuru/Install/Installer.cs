using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ContextGuru.Install;

/// <summary>An expected install failure (bad configuration, unsupported agent). <see cref="Installer.Run"/> reports it and exits 1.</summary>
public sealed class InstallerException(string message) : Exception(message);

public sealed class Installer(string baseFolder)
{
    const string ServerName = "contextguru";
    const string Usage = "Usage: contextguru install|uninstall --agent claude|codex|cursor";

    // Relaxed escaping keeps characters such as < > & ' and non-ASCII text as the user wrote them.
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Writes the marked command files and adds or replaces only the "contextguru" server entry.</summary>
    public void Install(string agent)
    {
        var target = GetJsonTarget(agent);
        // Parse first: a bad configuration must stop everything before any write.
        var config = ReadConfig(target.ConfigPath);
        var root = config ?? new JsonObject();
        var servers = GetServers(root);
        var entry = new JsonObject { ["command"] = ServerName, ["args"] = new JsonArray() };

        if (!JsonNode.DeepEquals(servers?[ServerName], entry))
        {
            if (servers is null)
            {
                servers = [];
                root["mcpServers"] = servers;
            }
            servers[ServerName] = entry;
            WriteConfig(target.ConfigPath, root, backup: config is not null);
        }

        foreach (var name in AgentTargets.CommandNames)
        {
            var path = target.GetCommandPath(name);
            var text = TemplateRenderer.Render(name, target);
            var existing = File.Exists(path) ? File.ReadAllText(path) : null;
            if (existing == text)
            {
                continue;
            }
            if (existing is not null && !target.IsManagedCommand(existing))
            {
                Backup(path);
            }
            WriteAtomically(path, text);
        }
    }

    /// <summary>Removes the marked command files and the "contextguru" server entry. Everything else stays.</summary>
    public void Uninstall(string agent)
    {
        var target = GetJsonTarget(agent);
        var config = ReadConfig(target.ConfigPath);

        if (config is not null && GetServers(config) is { } servers && servers.Remove(ServerName))
        {
            WriteConfig(target.ConfigPath, config, backup: true);
        }

        foreach (var name in AgentTargets.CommandNames)
        {
            var path = target.GetCommandPath(name);
            if (File.Exists(path) && target.IsManagedCommand(File.ReadAllText(path)))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>Runs "install|uninstall --agent claude|codex|cursor". Returns 0 on success, 2 for bad arguments, 1 for a failure.</summary>
    public static int Run(string[] args, string baseFolder, TextWriter output, TextWriter error)
    {
        if (args is not [("install" or "uninstall") and var verb, "--agent", ("claude" or "codex" or "cursor") and var agent])
        {
            error.WriteLine(Usage);
            return 2;
        }

        try
        {
            var installer = new Installer(baseFolder);
            if (verb == "install")
            {
                installer.Install(agent);
                output.WriteLine($"Installed ContextGuru for {agent}.");
            }
            else
            {
                installer.Uninstall(agent);
                output.WriteLine($"Uninstalled ContextGuru for {agent}.");
            }
            return 0;
        }
        catch (Exception e) when (e is InstallerException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Error: {e.Message}");
            return 1;
        }
    }

    AgentTarget GetJsonTarget(string agent)
    {
        var target = AgentTargets.For(agent, baseFolder);
        // Codex configuration is TOML, added in a later task. Fail before anything is written.
        return target.ConfigFormat == ConfigFormat.Json
            ? target
            : throw new InstallerException($"{agent} is not supported yet: its configuration is TOML.");
    }

    // Returns null when the file does not exist. Anything that is not a JSON object with an object-valued mcpServers is rejected.
    static JsonObject? ReadConfig(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new InstallerException($"{path} is not a JSON object. Nothing was changed.");
            // JsonNode reads objects lazily: duplicate keys only throw ArgumentException on first access.
            if (root.TryGetPropertyValue("mcpServers", out var servers))
            {
                _ = (servers as JsonObject)?.Count
                    ?? throw new InstallerException($"mcpServers in {path} is not a JSON object. Nothing was changed.");
            }
            return root;
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            throw new InstallerException($"Cannot parse {path}: {e.Message} Nothing was changed.");
        }
    }

    // ReadConfig has already checked the shape.
    static JsonObject? GetServers(JsonObject root) => root["mcpServers"] as JsonObject;

    static void WriteConfig(string path, JsonObject root, bool backup)
    {
        if (backup)
        {
            Backup(path);
        }
        WriteAtomically(path, root.ToJsonString(JsonOptions));
    }

    // Copies to path.bak, or .bak.1, .bak.2 and so on. Never overwrites a backup.
    static void Backup(string path)
    {
        var backup = path + ".bak";
        for (var i = 1; File.Exists(backup); i++)
        {
            backup = $"{path}.bak.{i}";
        }
        File.Copy(path, backup);
    }

    // Writes a temp file next to the target, then moves it over the target.
    static void WriteAtomically(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, text);
            if (!OperatingSystem.IsWindows() && File.Exists(path))
            {
                // The agent configuration can hold secrets: the replacement keeps the old permissions.
                File.SetUnixFileMode(temp, File.GetUnixFileMode(path));
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
