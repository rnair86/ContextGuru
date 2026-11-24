using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
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
    [InlineData("codex")]
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
    [InlineData("codex")]
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

    // A real file symlink. If the link cannot be created the test fails (no skipping), so the link path is always exercised.
    static void Link(string linkPath, string realPath, string text)
    {
        Write(realPath, text);
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        File.CreateSymbolicLink(linkPath, realPath);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Install_SymlinkedConfig_UpdatesTheTarget_KeepsTheLink(string agent)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);
        var real = Path.Combine(dir.Sub("dotfiles"), "config.json");
        var original = """{ "a": 1 }""";
        Link(target.ConfigPath, real, original);

        new Installer(dir.Path).Install(agent);

        Assert.NotNull(new FileInfo(target.ConfigPath).LinkTarget);
        Assert.Equal("contextguru", (string)JsonNode.Parse(File.ReadAllText(real))!["mcpServers"]!["contextguru"]!["command"]!);
        Assert.Equal(original, File.ReadAllText(real + ".bak"));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    [InlineData("codex")]
    public void Install_SymlinkedCommandFile_UpdatesTheTarget_KeepsTheLink(string agent)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);
        var save = target.GetCommandPath("save-context");
        var real = Path.Combine(dir.Sub("dotfiles"), "save.md");
        Link(save, real, "mine");

        new Installer(dir.Path).Install(agent);

        Assert.NotNull(new FileInfo(save).LinkTarget);
        Assert.Equal(TemplateRenderer.Render("save-context", target), File.ReadAllText(real));
        Assert.Equal("mine", File.ReadAllText(real + ".bak"));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Uninstall_SymlinkedConfig_UpdatesTheTarget_KeepsTheLink(string agent)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);
        var real = Path.Combine(dir.Sub("dotfiles"), "config.json");
        Link(target.ConfigPath, real, """{ "mcpServers": { "contextguru": { "command": "contextguru", "args": [] }, "x": {} } }""");

        new Installer(dir.Path).Uninstall(agent);

        Assert.NotNull(new FileInfo(target.ConfigPath).LinkTarget);
        var servers = (JsonObject)JsonNode.Parse(File.ReadAllText(real))!["mcpServers"]!;
        Assert.Equal(["x"], servers.Select(p => p.Key));
        Assert.True(File.Exists(real + ".bak"));
    }

    // The agent config can hold secrets: replacing it must not widen who can read it. Windows only (the API is Windows ACLs).
    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    [SupportedOSPlatform("windows")]
    public void Install_ProtectedAcl_IsKept(string agent)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);
        Write(target.ConfigPath, "{}");
        var file = new FileInfo(target.ConfigPath);
        var security = file.GetAccessControl();
        // No inherited rules: only the current user.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
        file.SetAccessControl(security);
        var before = new FileInfo(target.ConfigPath).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        // Sanity: protected, with exactly one rule.
        Assert.StartsWith("D:P", before, StringComparison.Ordinal);
        Assert.Equal(1, before.Count(c => c == '('));

        new Installer(dir.Path).Install(agent);

        var after = new FileInfo(target.ConfigPath).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        Assert.Equal(before, after);
        // The backup is the old file, so it keeps the protected ACL too.
        Assert.Equal(before, new FileInfo(target.ConfigPath + ".bak").GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access));
        Assert.Equal("{}", File.ReadAllText(target.ConfigPath + ".bak"));
        Assert.Equal("contextguru", (string)ReadConfig(target)["mcpServers"]!["contextguru"]!["command"]!);
    }

    // A hook that edits the config once the installer has staged its temp file, just before the replace. Fails if nothing is staged yet.
    static Action<string> EditWhileStaged(TempDir dir, string text) => path =>
    {
        Assert.Contains(Files(dir), f => f.EndsWith(".tmp", StringComparison.Ordinal));
        Write(path, text);
    };

    // The hook changes the config after the installer has read it and just before it replaces it.
    [Theory]
    [InlineData("claude", """{ "a": 1 }""")]
    [InlineData("cursor", """{ "a": 1 }""")]
    [InlineData("claude", null)]
    [InlineData("cursor", null)]
    public void Install_ConfigChangedAfterRead_Aborts_LeavesNewerFile_NoBackup_NoOtherWrites(string agent, string? original)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);
        if (original is not null)
        {
            Write(target.ConfigPath, original);
        }
        var installer = new Installer(dir.Path) { BeforeConfigWrite = EditWhileStaged(dir, """{ "edited": true }""") };

        var ex = Assert.Throws<InstallerException>(() => installer.Install(agent));

        Assert.Contains("Run the command again", ex.Message);
        Assert.Equal("""{ "edited": true }""", File.ReadAllText(target.ConfigPath));
        Assert.Equal([target.ConfigPath], Files(dir));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Uninstall_ConfigChangedAfterRead_Aborts_LeavesNewerFile_NoBackup_KeepsCommandFiles(string agent)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);
        new Installer(dir.Path).Install(agent);
        var before = Files(dir).Order().ToArray();
        var installer = new Installer(dir.Path) { BeforeConfigWrite = EditWhileStaged(dir, """{ "edited": true }""") };

        Assert.Throws<InstallerException>(() => installer.Uninstall(agent));

        Assert.Equal("""{ "edited": true }""", File.ReadAllText(target.ConfigPath));
        Assert.Equal(before, Files(dir).Order());
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Install_ConfigGainsBomAfterRead_Aborts(string agent)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);
        Write(target.ConfigPath, """{ "a": 1 }""");
        // Same text, different bytes: only a byte comparison sees it.
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. """{ "a": 1 }"""u8];
        var installer = new Installer(dir.Path) { BeforeConfigWrite = path => File.WriteAllBytes(path, withBom) };

        Assert.Throws<InstallerException>(() => installer.Install(agent));

        Assert.Equal(withBom, File.ReadAllBytes(target.ConfigPath));
        Assert.Equal([target.ConfigPath], Files(dir));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Install_FolderNamedBak_CountsAsTaken_BackupUsesBak1(string agent)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);
        var original = """{ "a": 1 }""";
        Write(target.ConfigPath, original);
        Directory.CreateDirectory(target.ConfigPath + ".bak");

        new Installer(dir.Path).Install(agent);

        Assert.Equal(original, File.ReadAllText(target.ConfigPath + ".bak.1"));
        Assert.True(Directory.Exists(target.ConfigPath + ".bak"));
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
    // Duplicate keys inside the existing entry, and inside another server's object.
    [InlineData("claude", "{\"mcpServers\": {\"contextguru\": {\"command\": \"a\", \"command\": \"b\"}}}")]
    [InlineData("claude", "{\"mcpServers\": {\"x\": {\"command\": \"a\", \"command\": \"b\"}}}")]
    [InlineData("cursor", "{\"mcpServers\": {\"contextguru\": {\"command\": \"a\", \"command\": \"b\"}}}")]
    [InlineData("cursor", "{\"mcpServers\": {\"x\": {\"command\": \"a\", \"command\": \"b\"}}}")]
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

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Install_InvalidUtf8Config_ChangesNothing_AndReportsWhy(string agent)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);
        // A raw 0xFF byte inside a string is not valid UTF-8.
        byte[] bytes = [.. "{\"a\":\"x"u8, 0xFF, .. "y\"}"u8];
        Directory.CreateDirectory(Path.GetDirectoryName(target.ConfigPath)!);
        File.WriteAllBytes(target.ConfigPath, bytes);

        var (code, _, error) = RunInstaller(dir, "install", "--agent", agent);

        Assert.Equal(1, code);
        Assert.NotEmpty(error);
        Assert.Equal(bytes, File.ReadAllBytes(target.ConfigPath));
        Assert.Equal([target.ConfigPath], Files(dir));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Install_InvalidUtf8AfterBom_ChangesNothing_AndReportsWhy(string agent)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. "{\"k\":\"x"u8, 0xFF, .. "y\"}"u8];
        Directory.CreateDirectory(Path.GetDirectoryName(target.ConfigPath)!);
        File.WriteAllBytes(target.ConfigPath, bytes);

        var (code, _, error) = RunInstaller(dir, "install", "--agent", agent);

        Assert.Equal(1, code);
        Assert.NotEmpty(error);
        Assert.Equal(bytes, File.ReadAllBytes(target.ConfigPath));
        Assert.Equal([target.ConfigPath], Files(dir));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Install_ConfigWithUtf8Bom_IsInstalled(string agent)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);
        Directory.CreateDirectory(Path.GetDirectoryName(target.ConfigPath)!);
        File.WriteAllBytes(target.ConfigPath, [0xEF, 0xBB, 0xBF, .. "{\"a\":1}"u8]);

        new Installer(dir.Path).Install(agent);

        Assert.Equal(1, (int)ReadConfig(target)["a"]!);
        Assert.Equal("contextguru", (string)ReadConfig(target)["mcpServers"]!["contextguru"]!["command"]!);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Uninstall_DirectoryAtConfigPath_Exit1_ChangesNothing(string agent)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);
        new Installer(dir.Path).Install(agent);
        File.Delete(target.ConfigPath);
        Directory.CreateDirectory(target.ConfigPath);
        var before = Files(dir).Order().ToArray();

        var (code, _, error) = RunInstaller(dir, "uninstall", "--agent", agent);

        Assert.Equal(1, code);
        Assert.NotEmpty(error);
        Assert.Equal(before, Files(dir).Order());
        Assert.True(File.Exists(target.GetCommandPath("save-context")));
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
    [InlineData("codex")]
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
    [InlineData("codex")]
    public void Uninstall_MissingConfig_CreatesNothing(string agent)
    {
        using var dir = new TempDir();

        var (code, _, _) = RunInstaller(dir, "uninstall", "--agent", agent);

        Assert.Equal(0, code);
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    // Stub codex for tests that must not reach a real one. Each call appends CODEX_HOME, the current folder and the arguments
    // to calls.txt next to it. "mcp get" prints getOutput and exits with getExit (1: not registered). Any other call prints to stdout,
    // prints stderr (if not empty) to stderr, runs the extra batch line and exits with exitCode.
    static string StubCodex(TempDir stubDir, int exitCode = 0, string stderr = "boom", int getExit = 1, string getOutput = "", string extra = "")
    {
        string[] lines =
        [
            "@echo off",
            """>>"%~dp0calls.txt" echo %CODEX_HOME%""",
            """>>"%~dp0calls.txt" echo %CD%""",
            """>>"%~dp0calls.txt" echo %*""",
            """if "%2"=="get" (""",
            .. getOutput == "" ? (string[])[] : [$"echo {getOutput}"],
            $"exit /b {getExit}",
            ")",
            "echo NOISE-OUT",
            .. stderr == "" ? (string[])[] : [$"echo {stderr} 1>&2"],
            .. extra == "" ? (string[])[] : [extra],
            $"exit /b {exitCode}",
        ];
        File.WriteAllLines(Path.Combine(stubDir.Path, "codex.cmd"), lines);
        return stubDir.Path;
    }

    static string[] StubCalls(TempDir stubDir) => File.ReadAllLines(Path.Combine(stubDir.Path, "calls.txt"));

    // Safety test: proves the child always gets CODEX_HOME (the folder of the config) and the exact arguments.
    [Fact]
    public void Codex_Install_RunsInTheCodexHome_WithTheExactArguments()
    {
        using var dir = new TempDir();
        using var stub = new TempDir();
        var home = Path.Combine(dir.Path, ".codex");

        new Installer(dir.Path) { CodexSearchPath = StubCodex(stub) }.Install("codex");

        Assert.Equal([home, home, "mcp get contextguru --json", home, home, "mcp add contextguru -- contextguru"], StubCalls(stub));
    }

    [Fact]
    public void Codex_Uninstall_RunsInTheCodexHome_WithTheExactArguments()
    {
        using var dir = new TempDir();
        using var stub = new TempDir();
        var target = Target("codex", dir);
        var home = Path.GetDirectoryName(target.ConfigPath)!;
        Write(target.ConfigPath, "[other]\n");

        new Installer(dir.Path) { CodexSearchPath = StubCodex(stub) }.Uninstall("codex");

        Assert.Equal([home, home, "mcp remove contextguru"], StubCalls(stub));
        // The stub left the config alone, so no backup stays.
        Assert.Empty(Backups(dir));
    }

    [Fact]
    public void Codex_Uninstall_WithoutConfig_DoesNotCallCodex()
    {
        using var dir = new TempDir();
        using var stub = new TempDir();

        new Installer(dir.Path) { CodexSearchPath = StubCodex(stub) }.Uninstall("codex");

        Assert.False(File.Exists(Path.Combine(stub.Path, "calls.txt")));
    }

    [Fact]
    public void Codex_NonZeroExit_Install_ReportsStderr_NoBackup_NoCommandFiles()
    {
        using var dir = new TempDir();
        using var stub = new TempDir();
        var target = Target("codex", dir);
        Write(target.ConfigPath, "[other]\n");

        var ex = Assert.Throws<InstallerException>(() => new Installer(dir.Path) { CodexSearchPath = StubCodex(stub, exitCode: 3) }.Install("codex"));

        Assert.Equal("boom", ex.Message);
        Assert.Equal([target.ConfigPath], Files(dir));
    }

    [Fact]
    public void Codex_NonZeroExit_Uninstall_ReportsStderr_NoBackup_KeepsCommandFiles()
    {
        using var dir = new TempDir();
        using var stub = new TempDir();
        var target = Target("codex", dir);
        new Installer(dir.Path) { CodexSearchPath = StubCodex(stub) }.Install("codex");
        Write(target.ConfigPath, "[other]\n");
        var before = Files(dir).Order().ToArray();

        var ex = Assert.Throws<InstallerException>(() => new Installer(dir.Path) { CodexSearchPath = StubCodex(stub, exitCode: 3) }.Uninstall("codex"));

        Assert.Equal("boom", ex.Message);
        Assert.Equal(before, Files(dir).Order());
    }

    [Theory]
    [InlineData("install")]
    [InlineData("uninstall")]
    public void Codex_NotFound_Throws_NamingCodexAndPath_WritesNothing(string verb)
    {
        using var dir = new TempDir();
        using var empty = new TempDir();
        Write(Target("codex", dir).ConfigPath, "[other]\n");
        var installer = new Installer(dir.Path) { CodexSearchPath = empty.Path };

        Action act = verb == "install" ? () => installer.Install("codex") : () => installer.Uninstall("codex");

        var ex = Assert.Throws<InstallerException>(act);

        Assert.Contains("codex", ex.Message);
        Assert.Contains("PATH", ex.Message);
        Assert.Equal([Target("codex", dir).ConfigPath], Files(dir));
    }

    // A codex found through a relative PATH entry would be one in the current folder: it must not run.
    [Fact]
    public void Codex_RelativePathEntry_IsSkipped()
    {
        using var dir = new TempDir();
        var name = "cg-" + Guid.NewGuid().ToString("N");
        var folder = Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, name)).FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "codex.cmd"), "@echo off\r\nexit /b 0\r\n");

            var ex = Assert.Throws<InstallerException>(() => new Installer(dir.Path) { CodexSearchPath = name }.Install("codex"));

            Assert.Contains("PATH", ex.Message);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    static Action Verb(string verb, Installer installer) => verb == "install" ? () => installer.Install("codex") : () => installer.Uninstall("codex");

    static void ClearReadOnly(TempDir dir)
    {
        foreach (var f in Files(dir))
        {
            File.SetAttributes(f, FileAttributes.Normal);
        }
    }

    // A codex that cannot start (a zero-byte exe) must be reported as an installer error that names it, with nothing changed.
    [Theory]
    [InlineData("install")]
    [InlineData("uninstall")]
    public void Codex_CannotStart_ThrowsNamingTheCodexPath_ChangesNothing(string verb)
    {
        using var dir = new TempDir();
        using var stub = new TempDir();
        var codex = Path.Combine(stub.Path, "codex.exe");
        File.WriteAllBytes(codex, []);
        var target = Target("codex", dir);
        Write(target.ConfigPath, "[other]\n");

        var ex = Assert.Throws<InstallerException>(Verb(verb, new Installer(dir.Path) { CodexSearchPath = stub.Path }));

        // One line, so Run prints a single "Error:" line and exits 1.
        Assert.Contains(codex, ex.Message);
        Assert.DoesNotContain('\n', ex.Message);
        Assert.Equal([target.ConfigPath], Files(dir));
    }

    // "mcp get" exits 0 but prints something that is not a registered stdio entry we can read.
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"transport":"stdio"}""")]
    [InlineData("""{"transport":{"type":"stdio","command":5}}""")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("""{"transport":{},"transport":{}}""")]
    [InlineData("""{"transport":{"type":"stdio","command":"contextguru","command":"x","args":[]}}""")]
    [InlineData("""{"transport":{"type":"stdio","command":"contextguru","args":{}}}""")]
    [InlineData("""{"transport":{"type":"stdio","command":"contextguru"}}""")]
    [InlineData("""{"transport":{"command":"contextguru","args":[]}}""")]
    [InlineData("""{"transport":{"type":5,"command":"contextguru","args":[]}}""")]
    [InlineData("""{"transport":{"type":"stdio","args":[]}}""")]
    public void Codex_UnreadableGetOutput_Throws_ChangesNothing_DoesNotCallAdd(string getOutput)
    {
        using var dir = new TempDir();
        using var stub = new TempDir();
        var target = Target("codex", dir);
        var home = Path.GetDirectoryName(target.ConfigPath)!;
        Write(target.ConfigPath, "[other]\n");
        // If add were called it would rewrite the config, so the byte check below can fail.
        var search = StubCodex(stub, getExit: 0, getOutput: getOutput, extra: """>"%CODEX_HOME%\config.toml" echo rewritten""");

        var ex = Assert.Throws<InstallerException>(() => new Installer(dir.Path) { CodexSearchPath = search }.Install("codex"));

        Assert.Contains("codex mcp get", ex.Message);
        Assert.Equal("[other]\n", File.ReadAllText(target.ConfigPath));
        Assert.Equal([home, home, "mcp get contextguru --json"], StubCalls(stub));
        Assert.Equal([target.ConfigPath], Files(dir));
    }

    // A readable entry that is not ours is replaced: add is called.
    [Theory]
    [InlineData("""{"transport":{"type":"streamable_http","url":"http://x"}}""")]
    [InlineData("""{"transport":{"type":"stdio","command":"other","args":[]}}""")]
    [InlineData("""{"transport":{"type":"stdio","command":"contextguru","args":["a"]}}""")]
    public void Codex_ReadableEntryThatIsNotOurs_IsReplaced_AddIsCalled(string getOutput)
    {
        using var dir = new TempDir();
        using var stub = new TempDir();

        new Installer(dir.Path) { CodexSearchPath = StubCodex(stub, getExit: 0, getOutput: getOutput) }.Install("codex");

        Assert.Equal("mcp add contextguru -- contextguru", StubCalls(stub)[^1]);
    }

    [Theory]
    [InlineData("install", "codex mcp add failed with exit code 5.")]
    [InlineData("uninstall", "codex mcp remove failed with exit code 5.")]
    public void Codex_NonZeroExit_WithEmptyStderr_NamesTheExitCode(string verb, string expected)
    {
        using var dir = new TempDir();
        using var stub = new TempDir();
        Write(Target("codex", dir).ConfigPath, "[other]\n");
        var installer = new Installer(dir.Path) { CodexSearchPath = StubCodex(stub, exitCode: 5, stderr: "") };

        var ex = Assert.Throws<InstallerException>(Verb(verb, installer));

        Assert.Equal(expected, ex.Message);
    }

    // File.Copy keeps the read-only attribute, so the backup must be made writable before it is deleted.
    [Fact]
    public void Codex_Uninstall_NoOp_ReadOnlyConfig_Succeeds_NoBackup_RemovesSkills()
    {
        using var dir = new TempDir();
        using var stub = new TempDir();
        var target = Target("codex", dir);
        var installer = new Installer(dir.Path) { CodexSearchPath = StubCodex(stub) };
        installer.Install("codex");
        Write(target.ConfigPath, "[other]\n");
        File.SetAttributes(target.ConfigPath, FileAttributes.ReadOnly);
        try
        {
            installer.Uninstall("codex");

            Assert.Empty(Backups(dir));
            Assert.All(AgentTargets.CommandNames, name => Assert.False(File.Exists(target.GetCommandPath(name))));
        }
        finally
        {
            ClearReadOnly(dir);
        }
    }

    [Fact]
    public void Codex_Failure_ReadOnlyConfig_ReportsCodexsError_NoBackup()
    {
        using var dir = new TempDir();
        using var stub = new TempDir();
        var target = Target("codex", dir);
        Write(target.ConfigPath, "[broken");
        File.SetAttributes(target.ConfigPath, FileAttributes.ReadOnly);
        try
        {
            var installer = new Installer(dir.Path) { CodexSearchPath = StubCodex(stub, exitCode: 3) };

            var ex = Assert.Throws<InstallerException>(() => installer.Install("codex"));

            Assert.Equal("boom", ex.Message);
            Assert.Equal([target.ConfigPath], Files(dir));
        }
        finally
        {
            ClearReadOnly(dir);
        }
    }

    // The backup is only deleted when it equals the config now. Here codex (the stub) changes the backup, not the config.
    [Fact]
    public void Codex_BackupDifferentFromConfig_IsKept()
    {
        using var dir = new TempDir();
        using var stub = new TempDir();
        var target = Target("codex", dir);
        Write(target.ConfigPath, "[other]\n");
        var installer = new Installer(dir.Path)
        {
            CodexSearchPath = StubCodex(stub, extra: """>"%CODEX_HOME%\config.toml.bak" echo tampered"""),
        };

        installer.Install("codex");

        Assert.Equal("[other]\n", File.ReadAllText(target.ConfigPath));
        Assert.Equal([target.ConfigPath + ".bak"], Backups(dir));
        Assert.Equal("tampered", File.ReadAllText(target.ConfigPath + ".bak").Trim());
    }

    // The tests below run the real codex. It is safe because the installer always sets CODEX_HOME to a folder under the TempDir.
    [Fact]
    public void Install_Codex_AddsTable_AndWritesMarkedSkills()
    {
        using var dir = new TempDir();
        var target = Target("codex", dir);

        new Installer(dir.Path).Install("codex");

        var config = File.ReadAllText(target.ConfigPath);
        Assert.Contains("[mcp_servers.contextguru]", config);
        Assert.Contains("command = \"contextguru\"", config);
        foreach (var name in AgentTargets.CommandNames)
        {
            var text = File.ReadAllText(target.GetCommandPath(name));
            Assert.True(target.IsManagedCommand(text));
            Assert.Equal(TemplateRenderer.Render(name, target), text);
            // The marker comes after the front matter, not on the first line.
            Assert.Equal(["---", AgentTargets.ManagedMarker], [text.Split('\n')[0], text.Split('\n')[4]]);
        }
        Assert.Empty(Backups(dir));
        Assert.DoesNotContain(Files(dir), f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void Install_Codex_KeepsOtherTablesAndComments_AcrossUninstall()
    {
        using var dir = new TempDir();
        var target = Target("codex", dir);
        Write(target.ConfigPath, "# my settings\nmodel = \"x\"\n\n[mcp_servers.github]\ncommand = \"gh\"\nargs = [\"a\", \"b\"]\n\n[other]\n# keep me\nk = 1\n");
        var installer = new Installer(dir.Path);

        // Codex rewrites the mcp_servers tables, so a comment inside one of them is lost. Comments elsewhere stay.
        installer.Install("codex");
        var installed = File.ReadAllText(target.ConfigPath);
        installer.Uninstall("codex");
        var uninstalled = File.ReadAllText(target.ConfigPath);

        Assert.Contains("[mcp_servers.contextguru]", installed);
        Assert.DoesNotContain("contextguru", uninstalled);
        foreach (var text in new[] { installed, uninstalled })
        {
            foreach (var kept in new[] { "# my settings", "model = \"x\"", "[mcp_servers.github]", "# keep me", "command = \"gh\"", "args = [\"a\", \"b\"]", "[other]", "k = 1" })
            {
                Assert.Contains(kept, text);
            }
        }
    }

    [Fact]
    public void Install_Codex_ExistingConfig_IsBackedUp_AndNotAgainWhenUnchanged()
    {
        using var dir = new TempDir();
        var target = Target("codex", dir);
        var original = "[other]\nk = 1\n";
        Write(target.ConfigPath, original);
        // A backup name already taken: the next free one is used.
        Write(target.ConfigPath + ".bak", "older");
        var installer = new Installer(dir.Path);

        installer.Install("codex");
        installer.Install("codex");

        Assert.Equal("older", File.ReadAllText(target.ConfigPath + ".bak"));
        Assert.Equal(original, File.ReadAllText(target.ConfigPath + ".bak.1"));
        Assert.Equal([target.ConfigPath + ".bak", target.ConfigPath + ".bak.1"], Backups(dir).Where(f => f.StartsWith(target.ConfigPath, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("install", "[mcp_servers")]
    [InlineData("install", "mcp_servers = 5\n")]
    [InlineData("uninstall", "[mcp_servers")]
    [InlineData("uninstall", "mcp_servers = 5\n")]
    public void Codex_UnparseableConfig_ChangesNothing_NoBackup_NoCommandFiles_Exit1(string verb, string config)
    {
        using var dir = new TempDir();
        var target = Target("codex", dir);
        Write(target.ConfigPath, config);

        var (code, output, error) = RunInstaller(dir, verb, "--agent", "codex");

        Assert.Equal(1, code);
        Assert.Empty(output);
        Assert.NotEmpty(error);
        Assert.Equal(config, File.ReadAllText(target.ConfigPath));
        Assert.Equal([target.ConfigPath], Files(dir));
    }

    [Fact]
    public void Install_Codex_AlreadyRegistered_KeepsTheUsersExtraKeys_NoBackup()
    {
        using var dir = new TempDir();
        var target = Target("codex", dir);
        var original = "[mcp_servers.contextguru]\ncommand = \"contextguru\"\nstartup_timeout_sec = 30\n";
        Write(target.ConfigPath, original);

        new Installer(dir.Path).Install("codex");

        Assert.Equal(original, File.ReadAllText(target.ConfigPath));
        Assert.Empty(Backups(dir));
        Assert.True(File.Exists(target.GetCommandPath("save-context")));
    }

    [Fact]
    public void Install_Codex_EntryWithOtherCommand_IsReplaced_AndOldConfigBackedUp()
    {
        using var dir = new TempDir();
        var target = Target("codex", dir);
        var stale = "[mcp_servers.contextguru]\ncommand = \"old\"\nargs = [\"a\"]\n";
        Write(target.ConfigPath, stale);

        new Installer(dir.Path).Install("codex");

        var config = File.ReadAllText(target.ConfigPath);
        Assert.Contains("command = \"contextguru\"", config);
        Assert.DoesNotContain("old", config);
        Assert.Equal(stale, File.ReadAllText(target.ConfigPath + ".bak"));
    }

    // The real codex cannot replace a read-only config: it fails and leaves it alone. No backup stays and no command file is written.
    [Fact]
    public void Install_Codex_ReadOnlyConfig_CodexFails_Exit1_NoBackup_NoCommandFiles()
    {
        using var dir = new TempDir();
        var target = Target("codex", dir);
        Write(target.ConfigPath, "[other]\nk = 1\n");
        File.SetAttributes(target.ConfigPath, FileAttributes.ReadOnly);
        try
        {
            var (code, output, error) = RunInstaller(dir, "install", "--agent", "codex");

            Assert.Equal(1, code);
            Assert.Empty(output);
            Assert.NotEmpty(error);
            Assert.Equal("[other]\nk = 1\n", File.ReadAllText(target.ConfigPath));
            Assert.Equal([target.ConfigPath], Files(dir));
        }
        finally
        {
            ClearReadOnly(dir);
        }
    }

    // The right command with extra arguments is not the entry we write, so it is replaced.
    [Fact]
    public void Install_Codex_EntryWithExtraArgs_IsReplaced_AndOldConfigBackedUp()
    {
        using var dir = new TempDir();
        var target = Target("codex", dir);
        var stale = "[mcp_servers.contextguru]\ncommand = \"contextguru\"\nargs = [\"extra\"]\n";
        Write(target.ConfigPath, stale);

        new Installer(dir.Path).Install("codex");

        var config = File.ReadAllText(target.ConfigPath);
        Assert.Contains("command = \"contextguru\"", config);
        Assert.DoesNotContain("extra", config);
        Assert.Equal(stale, File.ReadAllText(target.ConfigPath + ".bak"));
    }

    [Fact]
    public void Uninstall_Codex_RemovesOnlyTheEntryAndMarkedFiles()
    {
        using var dir = new TempDir();
        var target = Target("codex", dir);
        var installer = new Installer(dir.Path);
        installer.Install("codex");
        var mine = target.GetCommandPath("save-context");
        Write(mine, "mine");
        var configBefore = File.ReadAllText(target.ConfigPath);

        installer.Uninstall("codex");

        Assert.Equal("mine", File.ReadAllText(mine));
        foreach (var name in AgentTargets.CommandNames.Where(n => n != "save-context"))
        {
            Assert.False(File.Exists(target.GetCommandPath(name)));
            // The now empty skill folder is left alone.
            Assert.True(Directory.Exists(Path.GetDirectoryName(target.GetCommandPath(name))));
        }
        Assert.DoesNotContain("contextguru", File.ReadAllText(target.ConfigPath));
        Assert.Equal([target.ConfigPath + ".bak"], Backups(dir));
        Assert.Equal(configBefore, File.ReadAllText(target.ConfigPath + ".bak"));
    }

    // The codex process output must not reach ours: the summary is the only line on stdout.
    [Fact]
    public void Run_Codex_PrintsOnlyTheSummary()
    {
        using var dir = new TempDir();

        var install = RunInstaller(dir, "install", "--agent", "codex");
        var uninstall = RunInstaller(dir, "uninstall", "--agent", "codex");

        Assert.Equal((0, "Installed ContextGuru for codex." + Environment.NewLine, ""), install);
        Assert.Equal((0, "Uninstalled ContextGuru for codex." + Environment.NewLine, ""), uninstall);
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
        // A read-only config can be read but not replaced (on Windows), so the failure comes after the temp file is written.
        Write(target.ConfigPath, "{}");
        File.SetAttributes(target.ConfigPath, FileAttributes.ReadOnly);
        try
        {
            var (code, output, error) = RunInstaller(dir, "install", "--agent", "claude");

            Assert.Equal(1, code);
            Assert.Empty(output);
            Assert.NotEmpty(error);
            Assert.DoesNotContain(Files(dir), f => f.EndsWith(".tmp", StringComparison.Ordinal));
        }
        finally
        {
            foreach (var f in Files(dir))
            {
                File.SetAttributes(f, FileAttributes.Normal);
            }
        }
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    public void Run_ReplaceFails_ErrorNamesConfigAndBackupPath_OnOneLine(string agent)
    {
        using var dir = new TempDir();
        var target = Target(agent, dir);
        // A read-only config can be read but not replaced (on Windows), so File.Replace is the call that fails.
        Write(target.ConfigPath, "{}");
        File.SetAttributes(target.ConfigPath, FileAttributes.ReadOnly);
        try
        {
            var (code, output, error) = RunInstaller(dir, "install", "--agent", agent);

            Assert.Equal(1, code);
            Assert.Empty(output);
            // One line that says where the original is: at the config path, or at the backup path if it was already renamed.
            Assert.Single(error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            Assert.Contains(target.ConfigPath, error, StringComparison.Ordinal);
            Assert.Contains(target.ConfigPath + ".bak", error, StringComparison.Ordinal);
            Assert.Equal("{}", File.ReadAllText(target.ConfigPath));
            Assert.DoesNotContain(Files(dir), f => f.EndsWith(".tmp", StringComparison.Ordinal));
        }
        finally
        {
            foreach (var f in Files(dir))
            {
                File.SetAttributes(f, FileAttributes.Normal);
            }
        }
    }

    // Process routing. Only bad arguments: a valid install or uninstall would edit the real agent configuration,
    // because the tool reads the real user profile folder.
    [Theory]
    [InlineData("install", "--agent", "vim")]
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
