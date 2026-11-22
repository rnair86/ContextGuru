using ContextGuru.Core.Models;

namespace ContextGuru.Core.Projects;

public sealed class InvalidProjectPathException() : Exception("projectPath must be an absolute path to an existing folder.");

/// <summary>Works out which project a call belongs to: the argument, the first MCP root, or the server working folder.</summary>
public static class ProjectResolver
{
    const string HomeWarning = "Project root is the home folder or a filesystem root, so contexts are scoped to it.";

    public static ProjectInfo Resolve(string? projectPath, IReadOnlyList<string> rootPaths, string serverWorkingFolder)
    {
        string start;
        ProjectSource source;
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            start = projectPath.Trim();
            if (!Path.IsPathFullyQualified(start) || !Directory.Exists(start)) throw new InvalidProjectPathException();
            source = ProjectSource.Argument;
        }
        else if (rootPaths.Count > 0)
        {
            start = rootPaths[0];
            source = ProjectSource.Roots;
        }
        else
        {
            start = serverWorkingFolder;
            source = ProjectSource.Cwd;
        }

        var root = FindProjectRoot(ResolveRealPath(start));
        var isWindows = OperatingSystem.IsWindows();
        var key = MakeKey(root, isWindows);
        var isFilesystemRoot = Path.GetPathRoot(root) == root;
        var home = ResolveRealPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var warning = isFilesystemRoot || key == MakeKey(home, isWindows) ? HomeWarning : null;
        // A filesystem root has no folder name, so the root path is the name.
        var name = isFilesystemRoot ? root : Path.GetFileName(root);
        return new ProjectInfo(name, root, key, source, warning);
    }

    /// <summary>The key stored with a handoff. On Windows: backslashes, no trailing separator except a drive root, lower case.</summary>
    public static string MakeKey(string realPath, bool isWindows)
    {
        if (!isWindows) return realPath;
        var key = realPath.Replace('/', '\\').TrimEnd('\\');
        if (key.Length == 2 && key[1] == ':') key += '\\';
        return key.ToLowerInvariant();
    }

    public static IReadOnlyList<string> RootPathsFromUris(IEnumerable<string> uris) =>
        [.. uris.Where(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.IsFile).Select(u => new Uri(u).LocalPath)];

    // Resolves each segment that is a symlink or junction, so a link in a parent folder is resolved too.
    static string ResolveRealPath(string path)
    {
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        foreach (var segment in full[current.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, segment);
            // TrimEndingDirectorySeparator keeps a filesystem root such as C:\ or / intact.
            current = new DirectoryInfo(next).ResolveLinkTarget(returnFinalTarget: true) is { } target
                ? Path.TrimEndingDirectorySeparator(target.FullName)
                : next;
        }
        return current;
    }

    // Walks up to the folder holding .git (a directory, or a file for worktrees and submodules), without running git.
    static string FindProjectRoot(string start)
    {
        for (var dir = start; dir is not null; dir = Directory.GetParent(dir)?.FullName)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git)) return dir;
        }
        return start;
    }
}
