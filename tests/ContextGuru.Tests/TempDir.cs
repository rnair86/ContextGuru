namespace ContextGuru.Tests;

/// <summary>A unique folder under the temp path that deletes itself on dispose.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cg-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    /// <summary>Creates the sub folder (and any parents) and returns its full path.</summary>
    public string Sub(string relative)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
