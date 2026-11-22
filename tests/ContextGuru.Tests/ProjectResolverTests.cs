using System.Diagnostics;
using ContextGuru.Core.Models;
using ContextGuru.Core.Projects;

namespace ContextGuru.Tests;

public class ProjectResolverTests
{
    const string HomeWarning = "Project root is the home folder or a filesystem root, so contexts are scoped to it.";

    [Fact]
    public void Resolve_ArgumentBeatsRootsAndCwd()
    {
        using var t = new TempDir();
        var a = t.Sub("a"); var b = t.Sub("b"); var c = t.Sub("c");
        var p = ProjectResolver.Resolve(a, [b], c);
        Assert.Equal(ProjectSource.Argument, p.Source);
        Assert.Equal("a", p.Name);
    }

    [Fact]
    public void Resolve_RootsBeatCwd()
    {
        using var t = new TempDir();
        var p = ProjectResolver.Resolve(null, [t.Sub("b")], t.Sub("c"));
        Assert.Equal(ProjectSource.Roots, p.Source);
        Assert.Equal("b", p.Name);
    }

    [Fact]
    public void Resolve_FirstOfSeveralRootsWins()
    {
        using var t = new TempDir();
        var p = ProjectResolver.Resolve(null, [t.Sub("first"), t.Sub("second")], t.Sub("c"));
        Assert.Equal("first", p.Name);
    }

    [Fact]
    public void Resolve_NoArgumentOrRoots_UsesWorkingFolder()
    {
        using var t = new TempDir();
        var p = ProjectResolver.Resolve(null, [], t.Sub("c"));
        Assert.Equal(ProjectSource.Cwd, p.Source);
        Assert.Equal("c", p.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_BlankProjectPath_CountsAsNotGiven(string blank)
    {
        using var t = new TempDir();
        var p = ProjectResolver.Resolve(blank, [t.Sub("b")], t.Sub("c"));
        Assert.Equal(ProjectSource.Roots, p.Source);
    }

    [Fact]
    public void Resolve_Subfolder_WalksUpToGitDirectory()
    {
        using var t = new TempDir();
        var repo = t.Sub("repo");
        t.Sub(Path.Combine("repo", ".git"));
        var deep = t.Sub(Path.Combine("repo", "src", "deep"));
        var p = ProjectResolver.Resolve(deep, [], t.Sub("c"));
        Assert.Equal(repo, p.Path);
        Assert.Equal("repo", p.Name);
        Assert.Equal(ProjectSource.Argument, p.Source);
    }

    [Fact]
    public void Resolve_Subfolder_WalksUpToGitFile()
    {
        using var t = new TempDir();
        var repo = t.Sub("repo");
        File.WriteAllText(Path.Combine(repo, ".git"), "gitdir: elsewhere");
        var deep = t.Sub(Path.Combine("repo", "src"));
        Assert.Equal(repo, ProjectResolver.Resolve(deep, [], t.Sub("c")).Path);
    }

    [Fact]
    public void Resolve_RootStartingFolder_KeepsRootsSource()
    {
        using var t = new TempDir();
        t.Sub(Path.Combine("repo", ".git"));
        var p = ProjectResolver.Resolve(null, [t.Sub(Path.Combine("repo", "src"))], t.Sub("c"));
        Assert.Equal(ProjectSource.Roots, p.Source);
        Assert.Equal("repo", p.Name);
    }

    [Fact]
    public void Resolve_NoGit_UsesStartingFolder()
    {
        using var t = new TempDir();
        var start = t.Sub(Path.Combine("plain", "inner"));
        Assert.Equal(start, ProjectResolver.Resolve(start, [], t.Sub("c")).Path);
    }

    [Fact]
    public void Resolve_RelativePath_Throws() =>
        Assert.Throws<InvalidProjectPathException>(() => ProjectResolver.Resolve(Path.Combine("some", "relative"), [], Path.GetTempPath()));

    [Fact]
    public void Resolve_MissingFolder_Throws()
    {
        using var t = new TempDir();
        Assert.Throws<InvalidProjectPathException>(() => ProjectResolver.Resolve(Path.Combine(t.Path, "nope"), [], t.Path));
    }

    [Fact]
    public void Resolve_FilePath_Throws()
    {
        using var t = new TempDir();
        var file = Path.Combine(t.Path, "f.txt");
        File.WriteAllText(file, "x");
        Assert.Throws<InvalidProjectPathException>(() => ProjectResolver.Resolve(file, [], t.Path));
    }

    [Fact]
    public void InvalidProjectPathException_HasSpecMessage() =>
        Assert.Equal("projectPath must be an absolute path to an existing folder.", new InvalidProjectPathException().Message);

    [Fact]
    public void RootPathsFromUris_KeepsOnlyFileUris()
    {
        var paths = ProjectResolver.RootPathsFromUris(["file:///C:/My%20Repo", "https://x"]);
        var one = Assert.Single(paths);
        Assert.EndsWith("My Repo", one, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_UserProfileFolder_HasWarning()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(HomeWarning, ProjectResolver.Resolve(home, [], home).Warning);
    }

    [Fact]
    public void Resolve_FilesystemRoot_HasWarningAndNameIsThePath()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var p = ProjectResolver.Resolve(root, [], root);
        Assert.Equal(HomeWarning, p.Warning);
        Assert.Equal(root, p.Name);
    }

    [Fact]
    public void Resolve_NormalRepo_HasNoWarning()
    {
        using var t = new TempDir();
        Assert.Null(ProjectResolver.Resolve(t.Sub("repo"), [], t.Path).Warning);
    }

    // Links need privileges on some systems, so the test falls back to a junction and passes quietly if neither can be made.
    static bool TryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var cmd = Process.Start(new ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, RedirectStandardOutput = true });
            cmd!.WaitForExit();
            return cmd.ExitCode == 0;
        }
    }

    [Fact]
    public void Resolve_SymlinkedFolder_GivesRealPath()
    {
        using var t = new TempDir();
        var target = t.Sub("real");
        var link = Path.Combine(t.Path, "link");
        if (!TryLink(link, target)) return;
        var p = ProjectResolver.Resolve(link, [], t.Path);
        Assert.Equal(target, p.Path);
    }

    [Fact]
    public void Resolve_SymlinkInParentFolder_GivesRealPath()
    {
        using var t = new TempDir();
        var child = t.Sub(Path.Combine("real", "child"));
        var link = Path.Combine(t.Path, "link");
        if (!TryLink(link, Path.Combine(t.Path, "real"))) return;
        var p = ProjectResolver.Resolve(Path.Combine(link, "child"), [], t.Path);
        Assert.Equal(child, p.Path);
    }

    // Codex review: a link to a filesystem root lost its root when the trailing separator was trimmed.
    [Fact]
    public void Resolve_LinkToFilesystemRoot_KeepsTheRoot()
    {
        using var t = new TempDir();
        var root = Path.GetPathRoot(t.Path)!;
        var link = Path.Combine(t.Path, "link");
        if (!TryLink(link, root)) return;
        try
        {
            var p = ProjectResolver.Resolve(link, [], t.Path);
            Assert.Equal(root, p.Path);
            Assert.Equal(root, p.Name);
            Assert.Equal(HomeWarning, p.Warning);
        }
        finally
        {
            Directory.Delete(link); // removes only the link, never the target
        }
    }

    [Theory]
    [InlineData(@"C:\Users\Me\Repo\", @"c:\users\me\repo")]
    [InlineData("C:/Users/Me/Repo", @"c:\users\me\repo")]
    [InlineData(@"C:\", @"c:\")]
    [InlineData(@"\\Server\Share\Repo", @"\\server\share\repo")]
    public void MakeKey_Windows(string path, string key) => Assert.Equal(key, ProjectResolver.MakeKey(path, true));

    [Fact] public void MakeKey_Linux_IsUnchanged() => Assert.Equal("/home/Me/Repo", ProjectResolver.MakeKey("/home/Me/Repo", false));

    [Fact]
    public void Resolve_DifferentSpellingsOfOneFolder_GiveOneKey()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var t = new TempDir();
        var plain = t.Sub("Repo");
        var keys = new[] { plain, plain + @"\", plain.Replace('\\', '/'), plain.ToUpperInvariant() }
            .Select(s => ProjectResolver.Resolve(s, [], t.Path).Key).Distinct().ToList();
        Assert.Single(keys);
    }
}
