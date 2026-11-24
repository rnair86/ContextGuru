using System.Text.Json.Nodes;
using ContextGuru.Install;

namespace ContextGuru.Tests;

// Every install and uninstall test uses a TempDir as the base folder, so no real agent configuration is touched.
public class InstallerTests
{
    static readonly DateTime Old = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    static AgentTarget Target(string agent, TempDir dir) => AgentTargets.For(agent, dir.Path);

    static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    static string[] Files(TempDir dir) => Directory.GetFiles(dir.Path, "*", SearchOption.AllDirectories);

    static string[] Backups(TempDir dir) => Files(dir).Where(f => f.Contains(".bak")).Order().ToArray();

    static JsonNode ReadConfig(AgentTarget target) => JsonNode.Parse(File.ReadAllText(target.ConfigPath))!;

    // Sets every file to an old time, so a later write shows as a newer time.
    static void AgeFiles(TempDir dir)
    {
        foreach (var f in Files(dir))
        {
            File.SetLastWriteTimeUtc(f, Old);
        }
    }

    static (int Code, string Output, string Error) RunInstaller(TempDir dir, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = Installer.Run(args, dir.Path, output, error);
        return (code, output.ToString(), error.ToString());
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Install_WritesMarkedFiles_AndRegistersServer(string agent)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);

        new Installer(dir.Path).Install(agent);

        foreach (var name in AgentTargets.CommandNames)
        {
            var text = File.ReadAllText(target.GetCommandPath(name));
            Assert.True(target.IsManagedCommand(text));
            Assert.Equal(TemplateRenderer.Render(name, target), text);
        }
        var entry = ReadConfig(target)["mcpServers"]!["contextguru"]!;
        Assert.Equal("contextguru", (string)entry["command"]!);
        Assert.Empty(entry["args"]!.AsArray());
        Assert.Empty(Backups(dir));
        Assert.DoesNotContain(Files(dir), f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Install_Twice_ChangesNothing(string agent)
    {
        using var dir = new TempDir();
        var installer = new Installer(dir.Path);
        installer.Install(agent);
        AgeFiles(dir);
        var before = Files(dir).ToDictionary(f => f, f => File.ReadAllText(f));

        installer.Install(agent);

        Assert.Equal(before.Keys.Order(), Files(dir).Order());
        foreach (var (file, text) in before)
        {
            Assert.Equal(text, File.ReadAllText(file));
            Assert.Equal(Old, File.GetLastWriteTimeUtc(file));
        }
        Assert.Empty(Backups(dir));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Install_UnmarkedCommandFile_IsBackedUpToBak_ThenBak1_ThenBak2(string agent)
    {
        using var dir = new TempDir();
        var installer = new Installer(dir.Path);
        var save = Target(agent, dir).GetCommandPath("save-context");

        Write(save, "mine");
        installer.Install(agent);
        Write(save, "mine2");
        installer.Install(agent);
        Write(save, "mine3");
        installer.Install(agent);

        Assert.Equal("mine", File.ReadAllText(save + ".bak"));
        Assert.Equal("mine2", File.ReadAllText(save + ".bak.1"));
        Assert.Equal("mine3", File.ReadAllText(save + ".bak.2"));
        Assert.Equal(3, Backups(dir).Length);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Install_StaleMarkedCommandFile_IsRewrittenWithoutBackup(string agent)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);
        var save = target.GetCommandPath("save-context");
        Write(save, "<!-- contextguru:managed v1 -->\nold text");

        new Installer(dir.Path).Install(agent);

        Assert.Equal(TemplateRenderer.Render("save-context", target), File.ReadAllText(save));
        Assert.Empty(Backups(dir));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Install_ExistingConfig_IsBackedUp_AndNotAgainWhenUnchanged(string agent)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);
        var original = """{ "mcpServers": { "x": { "command": "x" } } }""";
        Write(target.ConfigPath, original);
        var installer = new Installer(dir.Path);

        installer.Install(agent);
        installer.Install(agent);

        Assert.Equal(original, File.ReadAllText(target.ConfigPath + ".bak"));
        Assert.Equal([target.ConfigPath + ".bak"], Backups(dir).Where(f => f.StartsWith(target.ConfigPath, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Install_StaleEntry_IsReplaced_AndOldConfigBackedUp(string agent)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);
        var stale = """{ "mcpServers": { "contextguru": { "command": "old", "args": ["a"], "env": { "K": "V" } } } }""";
        Write(target.ConfigPath, stale);

        new Installer(dir.Path).Install(agent);

        var entry = ReadConfig(target)["mcpServers"]!["contextguru"]!;
        Assert.Equal("contextguru", (string)entry["command"]!);
        Assert.Empty(entry["args"]!.AsArray());
        Assert.Null(entry["env"]);
        Assert.Equal(stale, File.ReadAllText(target.ConfigPath + ".bak"));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Install_KeepsOtherConfigKeys_Order_AndLiteralCharacters_AcrossUninstall(string agent)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);
        Write(target.ConfigPath, """
            {
              "theme": "dark é <b>&'",
              "mcpServers": {
                "x": { "command": "npx", "args": ["a<b>&'é"] }
              },
              "z": 1
            }
            """);
        var installer = new Installer(dir.Path);

        installer.Install(agent);
        var installed = File.ReadAllText(target.ConfigPath);
        installer.Uninstall(agent);
        var uninstalled = File.ReadAllText(target.ConfigPath);

        Assert.Equal(["theme", "mcpServers", "z"], ((JsonObject)JsonNode.Parse(installed)!).Select(p => p.Key));
        Assert.Equal(["x", "contextguru"], ((JsonObject)JsonNode.Parse(installed)!["mcpServers"]!).Select(p => p.Key));
        foreach (var text in new[] { installed, uninstalled })
        {
            Assert.Contains("dark é <b>&'", text);
            Assert.Contains("a<b>&'é", text);
            Assert.DoesNotContain("\\u", text);
            Assert.Equal(1, (int)JsonNode.Parse(text)!["z"]!);
            Assert.Equal("npx", (string)JsonNode.Parse(text)!["mcpServers"]!["x"]!["command"]!);
        }
        Assert.Null(JsonNode.Parse(uninstalled)!["mcpServers"]!["contextguru"]);
    }

    [Theory]
    [InlineData("claude", "{ not json")]
    [InlineData("claude", "")]
    [InlineData("claude", "[]")]
    [InlineData("claude", "\"text\"")]
    [InlineData("claude", "null")]
    [InlineData("claude", "{\"mcpServers\": []}")]
    [InlineData("claude", "{\"mcpServers\": null}")]
    [InlineData("claude", "{\"mcpServers\": \"x\"}")]
    [InlineData("claude", "{\"a\": 1, \"a\": 2}")]
    [InlineData("claude", "{\"mcpServers\": {\"x\": {}, \"x\": {}}}")]
    [InlineData("cursor", "{ not json")]
    [InlineData("cursor", "")]
    [InlineData("cursor", "{\"mcpServers\": []}")]
    [InlineData("cursor", "{\"a\": 1, \"a\": 2}")]
    [InlineData("cursor", "{\"mcpServers\": {\"x\": {}, \"x\": {}}}")]
    public void Install_UnparseableConfig_ChangesNothing_AndReportsWhy(string agent, string config)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);
        Write(target.ConfigPath, config);

        var (code, output, error) = RunInstaller(dir, "install", "--agent", agent);

        Assert.Equal(1, code);
        Assert.Empty(output);
        Assert.NotEmpty(error);
        Assert.Equal(config, File.ReadAllText(target.ConfigPath));
        Assert.Equal([target.ConfigPath], Files(dir));
    }

    [Fact]
    public void Uninstall_UnparseableConfig_ChangesNothing()
    {
        using var dir = new TempDir();
        var target = Target("claude", dir);
        new Installer(dir.Path).Install("claude");
        Write(target.ConfigPath, "{ not json");
        AgeFiles(dir);
        var before = Files(dir).Order().ToArray();

        var (code, _, error) = RunInstaller(dir, "uninstall", "--agent", "claude");

        Assert.Equal(1, code);
        Assert.NotEmpty(error);
        Assert.Equal(before, Files(dir).Order());
        Assert.All(Files(dir), f => Assert.Equal(Old, File.GetLastWriteTimeUtc(f)));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Uninstall_RemovesOnlyMarkedFilesAndTheEntry(string agent)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);
        var installer = new Installer(dir.Path);
        var database = Path.Combine(dir.Path, "contextguru.db");
        Write(database, "db");
        installer.Install(agent);
        var mine = target.GetCommandPath("save-context");
        Write(mine, "mine");
        var configBefore = File.ReadAllText(target.ConfigPath);

        installer.Uninstall(agent);

        Assert.Equal("mine", File.ReadAllText(mine));
        foreach (var name in AgentTargets.CommandNames.Where(n => n != "save-context"))
        {
            Assert.False(File.Exists(target.GetCommandPath(name)));
        }
        Assert.Equal("db", File.ReadAllText(database));
        Assert.Null(ReadConfig(target)["mcpServers"]!["contextguru"]);
        // Only the config is backed up: unmarked files are left alone and marked files are the installer's own.
        Assert.Equal([target.ConfigPath + ".bak"], Backups(dir));
        Assert.Equal(configBefore, File.ReadAllText(target.ConfigPath + ".bak"));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Uninstall_Twice_ChangesNothing(string agent)
    {
        using var dir = new TempDir();
        var installer = new Installer(dir.Path);
        installer.Install(agent);
        installer.Uninstall(agent);
        AgeFiles(dir);
        var before = Files(dir).Order().ToArray();

        installer.Uninstall(agent);

        Assert.Equal(before, Files(dir).Order());
        Assert.All(Files(dir), f => Assert.Equal(Old, File.GetLastWriteTimeUtc(f)));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Uninstall_MissingConfig_CreatesNothing(string agent)
    {
        using var dir = new TempDir();

        var (code, _, _) = RunInstaller(dir, "uninstall", "--agent", agent);

        Assert.Equal(0, code);
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    [Theory]
    [InlineData("install")]
    [InlineData("uninstall")]
    public void Codex_FailsBeforeWritingAnything(string verb)
    {
        using var dir = new TempDir();

        var (code, output, error) = RunInstaller(dir, verb, "--agent", "codex");

        Assert.Equal(1, code);
        Assert.Empty(output);
        Assert.Contains("codex", error);
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
        var installer = new Installer(dir.Path);
        Assert.Throws<InstallerException>(() => installer.Install("codex"));
        Assert.Throws<InstallerException>(() => installer.Uninstall("codex"));
    }

    [Theory]
    [InlineData("install")]
    [InlineData("install", "--agent")]
    [InlineData("install", "--agent", "vim")]
    [InlineData("install", "--agent", "Claude")]
    [InlineData("install", "--agent", "claude", "extra")]
    [InlineData("install", "--agent", "claude", "--agent", "cursor")]
    [InlineData("install", "claude")]
    [InlineData("install", "--agent=claude")]
    [InlineData("uninstall")]
    [InlineData("uninstall", "--agent", "vim")]
    [InlineData("uninstall", "--agent", "cursor", "extra")]
    [InlineData("bogus", "--agent", "claude")]
    public void Run_MissingUnknownOrExtraArguments_Exit2_WithUsageOnError(params string[] args)
    {
        using var dir = new TempDir();

        var (code, output, error) = RunInstaller(dir, args);

        Assert.Equal(2, code);
        Assert.Empty(output);
        Assert.Contains("Usage:", error);
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    [Theory]
    [InlineData("install")]
    [InlineData("uninstall")]
    public void Run_Success_PrintsSummaryToOutput_Exit0(string verb)
    {
        using var dir = new TempDir();

        var (code, output, error) = RunInstaller(dir, verb, "--agent", "claude");

        Assert.Equal(0, code);
        Assert.Contains("claude", output);
        Assert.Empty(error);
    }

    [Fact]
    public void Run_IoFailure_Exit1_WithMessageAndNoTempFileLeft()
    {
        using var dir = new TempDir();
        var target = Target("claude", dir);
        // A folder where the config file should be: the write cannot replace it.
        Directory.CreateDirectory(target.ConfigPath);

        var (code, output, error) = RunInstaller(dir, "install", "--agent", "claude");

        Assert.Equal(1, code);
        Assert.Empty(output);
        Assert.NotEmpty(error);
        Assert.DoesNotContain(Files(dir), f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void Install_KeepsConfigFileMode_OnUnix()
    {
        // Unix modes do not exist on Windows. Linux and macOS are not run in version 1.
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        using var dir = new TempDir();
        var target = Target("claude", dir);
        Write(target.ConfigPath, "{}");
        File.SetUnixFileMode(target.ConfigPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        new Installer(dir.Path).Install("claude");

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(target.ConfigPath));
    }

    // Process routing. Only bad arguments: a valid install or uninstall would edit the real agent configuration,
    // because the tool reads the real user profile folder.
    [Theory]
    [InlineData("install")]
    [InlineData("install", "--agent")]
    [InlineData("install", "--agent", "vim")]
    [InlineData("uninstall")]
    [InlineData("uninstall", "--agent", "vim")]
    public async Task Process_InstallWithBadArguments_Exit2_UsageOnStderr(params string[] args)
    {
        using var dir = new TempDir();
        await using var server = ServerProcess.Start(Path.Combine(dir.Path, "c.db"), args);

        var result = await server.WaitForExitAsync();

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("Usage: contextguru install|uninstall --agent", result.Stderr);
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("--agent", "claude")]
    public async Task Process_OtherArguments_Exit2_UsageNamesInstallAndUninstall(params string[] args)
    {
        using var dir = new TempDir();
        await using var server = ServerProcess.Start(Path.Combine(dir.Path, "c.db"), args);

        var result = await server.WaitForExitAsync();

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Stdout);
        Assert.Contains("install|uninstall --agent claude|codex|cursor", result.Stderr);
    }
}
