using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Logging;
using Xunit;

namespace WinFEBuilder.Tests;

/// <summary>
/// Logs are grouped one folder per run. Before this, a day's work left thirty loose files in one
/// folder with no way to tell which app log belonged to which DISM log — the opposite of useful when
/// the logs are the provenance record for a piece of forensic media.
/// </summary>
public class SessionLogLayoutTests
{
    [Fact]
    public void SessionLogDir_IsASubfolderOfLogDir_NamedForTheRun()
    {
        using var tmp = new TempDir();
        var paths = new AppPaths(tmp.Path, sessionStamp: "2026-08-07_132746");

        Assert.Equal(Path.Combine(paths.LogDir, "2026-08-07_132746"), paths.SessionLogDir);
        Assert.Equal("2026-08-07_132746", paths.SessionStamp);
        Assert.True(Directory.Exists(paths.SessionLogDir));
    }

    [Fact]
    public void SessionStamp_DefaultsToNow_AndIsFilesystemSafe()
    {
        using var tmp = new TempDir();
        var paths = new AppPaths(tmp.Path);

        Assert.False(string.IsNullOrWhiteSpace(paths.SessionStamp));
        Assert.DoesNotContain(':', paths.SessionStamp);   // would be illegal in a folder name
        Assert.All(Path.GetInvalidFileNameChars(), c => Assert.DoesNotContain(c, paths.SessionStamp));
    }

    [Fact]
    public void LogFilesGoInTheSessionFolder_WithoutRedundantTimestamps()
    {
        using var tmp = new TempDir();
        var paths = new AppPaths(tmp.Path, sessionStamp: "2026-08-07_132746");
        var log = new LogService(paths.SessionLogDir);

        // The folder already carries the timestamp, so repeating it in every filename is noise.
        Assert.Equal(Path.Combine(paths.SessionLogDir, "winfebuilder.log"), log.TextLogPath);
        Assert.Equal(Path.Combine(paths.SessionLogDir, "winfebuilder.jsonl"), log.JsonLogPath);
    }

    [Fact]
    public void TwoRunsDoNotShareAFolder()
    {
        using var tmp = new TempDir();
        var first = new AppPaths(tmp.Path, sessionStamp: "2026-08-07_100000");
        var second = new AppPaths(tmp.Path, sessionStamp: "2026-08-07_110000");

        Assert.NotEqual(first.SessionLogDir, second.SessionLogDir);
        Assert.Equal(first.LogDir, second.LogDir);
        Assert.True(Directory.Exists(first.SessionLogDir));
        Assert.True(Directory.Exists(second.SessionLogDir));
    }

    [Fact]
    public void SessionStampCanStillBeUsedAsAFileSuffixWhenSharingAFolder()
    {
        // Writing into the shared root remains possible — e.g. a future tool that keeps one file per
        // run — and then the names must stay unique.
        using var tmp = new TempDir();
        var paths = new AppPaths(tmp.Path, sessionStamp: "2026-08-07_132746");
        var log = new LogService(paths.LogDir, paths.SessionStamp);

        Assert.Equal(Path.Combine(paths.LogDir, "winfebuilder_2026-08-07_132746.log"), log.TextLogPath);
    }

    [Fact]
    public void WritingALogEntryCreatesBothFilesInTheSessionFolder()
    {
        using var tmp = new TempDir();
        var paths = new AppPaths(tmp.Path, sessionStamp: "2026-08-07_132746");
        var log = new LogService(paths.SessionLogDir);

        log.Info("Test", "hello");

        Assert.True(File.Exists(log.TextLogPath));
        Assert.True(File.Exists(log.JsonLogPath));
        Assert.Contains("hello", File.ReadAllText(log.TextLogPath));

        // And nothing was written loose into the root logs folder.
        Assert.Empty(Directory.GetFiles(paths.LogDir));
    }
}
