using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Hashing;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Services;
using Xunit;

namespace WinFEBuilder.Tests;

public class ToolServiceTests
{
    private static ToolService Build(TempDir tmp) =>
        new(new LogService(tmp.Dir("logs")), new HashService());

    private static string MakeFramework(TempDir tmp)
    {
        // Minimal IntelWinFE-like framework with a media root (Boot/EFI/Sources) under USB\x86-x64.
        var fw = tmp.Dir("framework");
        tmp.Dir(@"framework\USB\x86-x64\Boot");
        tmp.Dir(@"framework\USB\x86-x64\EFI");
        tmp.Dir(@"framework\USB\x86-x64\Sources");
        return fw;
    }

    [Fact]
    public void ResolveFrameworkToolsDir_ReturnsMediaToolsArchPath()
    {
        using var tmp = new TempDir();
        var fw = MakeFramework(tmp);
        var svc = Build(tmp);

        var dir = svc.ResolveFrameworkToolsDir(fw, "x64");
        Assert.NotNull(dir);
        Assert.EndsWith(Path.Combine("USB", "x86-x64", "tools", "x64"), dir!);
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public async Task AddToolToFramework_CopiesIntoToolsArch()
    {
        using var tmp = new TempDir();
        var fw = MakeFramework(tmp);
        var svc = Build(tmp);

        var toolSrc = tmp.Dir("FTKImager");
        tmp.File(@"FTKImager\FTKImager.exe", "MZ");
        tmp.File(@"FTKImager\lib\x.dll", "dll");

        var r = await svc.AddToolToFrameworkAsync(toolSrc, fw, "x64", null);
        Assert.True(r.Success);

        var expected = Path.Combine(fw, "USB", "x86-x64", "tools", "x64", "FTKImager", "FTKImager.exe");
        Assert.True(File.Exists(expected));

        var list = svc.ListFrameworkTools(fw);
        Assert.Contains(list, t => t.Name == "FTKImager" && t.Architecture == "x64");
    }

    [Fact]
    public async Task RemoveFrameworkTool_Deletes()
    {
        using var tmp = new TempDir();
        var fw = MakeFramework(tmp);
        var svc = Build(tmp);
        var toolSrc = tmp.Dir("Tool");
        tmp.File(@"Tool\t.exe", "x");

        await svc.AddToolToFrameworkAsync(toolSrc, fw, "x86", null);
        var tool = svc.ListFrameworkTools(fw).Single(t => t.Name == "Tool");
        Assert.Equal("x86", tool.Architecture);

        svc.RemoveFrameworkTool(tool.Path);
        Assert.DoesNotContain(svc.ListFrameworkTools(fw), t => t.Name == "Tool");
    }

    [Fact]
    public void ResolveFrameworkToolsDir_NullWhenNoMediaRoot()
    {
        using var tmp = new TempDir();
        var fw = tmp.Dir("emptyfw");
        var svc = Build(tmp);
        Assert.Null(svc.ResolveFrameworkToolsDir(fw, "x64"));
    }
}

public class DriverEnumerationTests
{
    private static DriverService Build(TempDir tmp)
    {
        var log = new LogService(tmp.Dir("logs"));
        var runner = new ProcessRunner(log);
        var hash = new HashService();
        var adk = new AdkDetectionService(log);
        var dism = new DismService(log, runner, adk, hash);
        var paths = new AppPaths(tmp.Path);
        return new DriverService(log, runner, dism, hash, paths);
    }

    [Fact]
    public async Task EnumerateDrivers_FindsInfAndDetectsArch()
    {
        using var tmp = new TempDir();
        var dir = tmp.Dir("drivers");
        tmp.File(@"drivers\net.inf", "[Version]\nClass=Net\nProvider=%P%\n[Manufacturer]\n%M%=Std,NTamd64\n[Std.NTamd64]\n");
        tmp.File(@"drivers\old32.inf", "[Version]\nClass=Net\n[Manufacturer]\n%M%=Std,NTx86\n[Std.NTx86]\n");

        var svc = Build(tmp);
        var list = await svc.EnumerateDriversAsync(dir, "amd64");

        Assert.Equal(2, list.Count);
        var amd = list.First(d => d.InfName == "net.inf");
        Assert.Contains("amd64", amd.Architectures);
        Assert.True(amd.CompatibleWithTarget);

        var x86 = list.First(d => d.InfName == "old32.inf");
        Assert.False(x86.CompatibleWithTarget); // x86 driver, amd64 target
    }
}
