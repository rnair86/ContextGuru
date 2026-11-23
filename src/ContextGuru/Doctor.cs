using System.Globalization;
using System.Reflection;
using ContextGuru.Core.Models;
using ContextGuru.Core.Projects;
using ContextGuru.Core.Storage;
using Microsoft.Data.Sqlite;

namespace ContextGuru;

/// <summary>Runs non-mutating environment and database diagnostics for --doctor.</summary>
public static class Doctor
{
    public static int Run(string dbPath, string workingFolder, TextWriter output)
    {
        var project = ProjectResolver.Resolve(null, [], workingFolder);
        var (schemaLine, schemaOk) = CheckSchemaVersion(dbPath);
        var (writeLine, writeOk) = CheckWriteLock(dbPath);

        string[] lines =
        [
            VersionLine(),
            $"database path: {dbPath}",
            schemaLine,
            $"project name: {project.Name}",
            $"project path: {project.Path}",
            $"project source: {project.Source.ToString().ToLowerInvariant()}",
            GitFoundLine(project),
            writeLine,
        ];

        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        return schemaOk && writeOk ? 0 : 1;
    }

    static string VersionLine()
    {
        var info = typeof(Doctor).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var plus = info.IndexOf('+', StringComparison.Ordinal);
        var version = plus >= 0 ? info[..plus] : info;
        return $"version: {version}";
    }

    static (string Line, bool Ok) CheckSchemaVersion(string dbPath)
    {
        if (Directory.Exists(dbPath))
        {
            return ("schema version: failed (database path is a directory)", false);
        }

        if (!File.Exists(dbPath))
        {
            return ("schema version: not created yet (created on first save)", true);
        }

        try
        {
            using var c = Migrations.Open(dbPath, SqliteOpenMode.ReadWrite);
            var version = Migrations.ReadUserVersion(c);
            var formatted = version.ToString(CultureInfo.InvariantCulture);
            return version > Migrations.Latest
                ? ($"schema version: {formatted} (newer than this tool)", false)
                : ($"schema version: {formatted}", true);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            return ($"schema version: failed ({ex.Message})", false);
        }
    }

    static string GitFoundLine(ProjectInfo project)
    {
        var git = Path.Combine(project.Path, ".git");
        var found = Directory.Exists(git) || File.Exists(git);
        return $".git found: {(found ? "yes" : "no")}";
    }

    static (string Line, bool Ok) CheckWriteLock(string dbPath)
    {
        if (Directory.Exists(dbPath))
        {
            return ("write test: failed (database path is a directory)", false);
        }

        if (!File.Exists(dbPath))
        {
            return ("write test: skipped", true);
        }

        try
        {
            using var c = Migrations.Open(dbPath, SqliteOpenMode.ReadWrite);
            // Microsoft.Data.Sqlite issues BEGIN IMMEDIATE for BeginTransaction().
            using var tx = c.BeginTransaction();
            tx.Rollback();
            return ("write test: ok", true);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            return ($"write test: failed ({ex.Message})", false);
        }
    }
}
