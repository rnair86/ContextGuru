using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ContextGuru.Install;

/// <summary>An expected install failure (bad configuration, unsupported agent). <see cref="Installer.Run"/> reports it and exits 1.</summary>
public sealed class InstallerException(string message) : Exception(message);

public sealed class Installer(string baseFolder)
{
    // Test hook, called once the config's temp file is written, just before the re-check and the replace. Lets a test change the file at that moment.
    internal Action<string>? BeforeConfigWrite { get; init; }

    const string ServerName = "contextguru";
    const string Usage = "Usage: contextguru install|uninstall --agent claude|codex|cursor";

    // Invalid bytes must fail the parse: the default decoder would turn them into U+FFFD and the rewrite would keep that.
    static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    // Duplicate keys anywhere in the file are a parse failure: JsonNode would otherwise keep or lose one silently.
    static readonly JsonDocumentOptions ParseOptions = new() { AllowDuplicateProperties = false };

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
        var configPath = ResolveLink(target.ConfigPath);
        var config = ReadConfig(configPath);
        var root = config?.Root ?? new JsonObject();
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
            WriteConfig(configPath, root, config?.Bytes);
        }

        foreach (var name in AgentTargets.CommandNames)
        {
            var path = ResolveLink(target.GetCommandPath(name));
            var text = TemplateRenderer.Render(name, target);
            var existing = File.Exists(path) ? File.ReadAllText(path) : null;
            if (existing == text)
            {
                continue;
            }
            // An existing file that is not ours is kept as a backup.
            WriteAtomically(path, text, backup: existing is not null && !target.IsManagedCommand(existing));
        }
    }

    /// <summary>Removes the marked command files and the "contextguru" server entry. Everything else stays.</summary>
    public void Uninstall(string agent)
    {
        var target = GetJsonTarget(agent);
        var configPath = ResolveLink(target.ConfigPath);
        var config = ReadConfig(configPath);

        if (config is not null && GetServers(config.Root) is { } servers && servers.Remove(ServerName))
        {
            WriteConfig(configPath, config.Root, config.Bytes);
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

    // A replace would swap the link itself and leave its target alone: work on the final target instead.
    static string ResolveLink(string path) =>
        Path.Exists(path) ? File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path : path;

    // The parsed configuration with the exact bytes it was read from.
    sealed record ConfigFile(byte[] Bytes, JsonObject Root);

    // Returns null when nothing exists at the path. A folder there is not "missing": reading it fails.
    static byte[]? ReadBytes(string path) => Path.Exists(path) ? File.ReadAllBytes(path) : null;

    // Returns null when the file does not exist. Anything that is not a JSON object with an object-valued mcpServers is rejected.
    static ConfigFile? ReadConfig(string path)
    {
        if (ReadBytes(path) is not { } bytes)
        {
            return null;
        }

        try
        {
            // Decode the bytes ourselves: File.ReadAllText switches to a lenient decoder when it finds a BOM.
            var utf8 = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? bytes.AsSpan(Encoding.UTF8.Preamble.Length) : bytes;
            var root = JsonNode.Parse(StrictUtf8.GetString(utf8), documentOptions: ParseOptions) as JsonObject
                ?? throw new InstallerException($"{path} is not a JSON object. Nothing was changed.");
            if (root.TryGetPropertyValue("mcpServers", out var servers) && servers is not JsonObject)
            {
                throw new InstallerException($"mcpServers in {path} is not a JSON object. Nothing was changed.");
            }
            return new ConfigFile(bytes, root);
        }
        catch (JsonException e)
        {
            throw new InstallerException($"Cannot parse {path}: {e.Message} Nothing was changed.");
        }
        catch (DecoderFallbackException)
        {
            throw new InstallerException($"{path} is not valid UTF-8. Nothing was changed.");
        }
    }

    // ReadConfig has already checked the shape.
    static JsonObject? GetServers(JsonObject root) => root["mcpServers"] as JsonObject;

    void WriteConfig(string path, JsonObject root, byte[]? original) =>
        WriteAtomically(path, root.ToJsonString(JsonOptions), backup: original is not null, beforeReplace: () =>
        {
            BeforeConfigWrite?.Invoke(path);
            // Compare the raw bytes with what we read, so an edit made since then is not lost. This narrows the window, it does not close it.
            var current = ReadBytes(path);
            var unchanged = current is null || original is null ? current == original : current.AsSpan().SequenceEqual(original);
            if (!unchanged)
            {
                throw new InstallerException($"{path} changed while it was being updated. Run the command again.");
            }
        });

    // path.bak, or .bak.1, .bak.2 and so on: the first name nothing exists at, so a backup is never overwritten.
    static string FreeBackupName(string path)
    {
        var backup = path + ".bak";
        for (var i = 1; Path.Exists(backup); i++)
        {
            backup = $"{path}.bak.{i}";
        }
        return backup;
    }

    // Writes a temp file next to the target, then puts it in place. With a backup, one File.Replace call renames the old file to
    // the backup name and gives the new file the old file's permissions (the Windows ACL), so both keep them.
    // Without a backup the temp file is moved over the target and keeps the permissions it was created with: on Windows the
    // folder's inherited ones. On Unix the temp file is created with the old file's mode (it can only be stricter after the umask).
    static void WriteAtomically(string path, string text, bool backup, Action? beforeReplace = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        var exists = File.Exists(path);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (exists && !OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = File.GetUnixFileMode(path);
        }
        try
        {
            using (var stream = new FileStream(temp, options))
            {
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
            // Chosen before the final check, so that only the replace follows it.
            var backupPath = exists && backup ? FreeBackupName(path) : null;
            // Runs with the temp file staged, immediately before the target is replaced or created.
            beforeReplace?.Invoke();
            if (!exists)
            {
                File.Move(temp, path);
            }
            else if (backupPath is not null)
            {
                try
                {
                    File.Replace(temp, path, backupPath);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Most failures leave the original in place. Rarely Windows has already renamed it to the backup name.
                    throw new InstallerException($"Could not replace {path}: {e.Message} The original is still at {path}, or at {backupPath} if it was already renamed.");
                }
            }
            else
            {
                File.Move(temp, path, overwrite: true);
            }
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
