using Microsoft.Extensions.DependencyInjection;
using WinFEBuilder.App.Forms;
using WinFEBuilder.Core;
using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Logging;

namespace WinFEBuilder.App;

internal static class Program
{
    public static IServiceProvider Services { get; private set; } = null!;

    [STAThread]
    private static void Main()
    {
        // High-DPI, per-monitor v2 (also declared in app.manifest).
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var paths = new AppPaths();

        var services = new ServiceCollection();
        services.AddWinFeBuilderCore(paths);
        services.AddSingleton<MainForm>();
        Services = services.BuildServiceProvider();

        var log = Services.GetRequiredService<ILogService>();
        log.Info("App", $"WinFE Builder starting. Base: {paths.BaseDir}");

        Application.ThreadException += (_, e) =>
        {
            log.Error("App", "Unhandled UI thread exception.", e.Exception);
            MessageBox.Show(e.Exception.Message, "WinFE Builder — Unexpected error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                log.Error("App", "Unhandled domain exception.", ex);
        };

        var form = Services.GetRequiredService<MainForm>();
        Application.Run(form);
    }
}
