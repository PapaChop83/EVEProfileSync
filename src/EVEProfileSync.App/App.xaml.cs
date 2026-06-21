using System.IO;
using System.Windows;
using System.Windows.Threading;
using EVEProfileSync.Core;

namespace EVEProfileSync.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        WriteUiTrace("App.OnStartup entered.");

        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EVEProfileSync");
        Directory.CreateDirectory(appDataPath);
        WriteUiTrace($"AppDataPath={appDataPath}");

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        DispatcherUnhandledException += (_, args) =>
        {
            LogStartupFailure(appDataPath, args.Exception);
            System.Windows.MessageBox.Show(
                args.Exception.ToString(),
                "EVEProfileSync Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(-1);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogStartupFailure(appDataPath, exception);
            }
        };

        try
        {
            var manifestPath = Path.Combine(AppContext.BaseDirectory, "artifact-map.json");
            var overviewService = new OverviewAssetService();
            var discoveryService = new SettingsDiscoveryService();
            var mappingService = new ArtifactMappingService(manifestPath, overviewService, appDataPath);
            var backupService = new BackupService(appDataPath);
            var processGuardService = new ProcessGuardService();
            var syncExecutor = new SyncExecutor(backupService, processGuardService);

            var viewModel = new MainViewModel(
                discoveryService,
                mappingService,
                backupService,
                syncExecutor,
                processGuardService,
                appDataPath,
                AppContext.BaseDirectory);

            var window = new MainWindow(viewModel);
            MainWindow = window;
            window.Show();
            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
            window.Focus();
            WriteUiTrace("Main window shown.");
        }
        catch (Exception exception)
        {
            LogStartupFailure(appDataPath, exception);
            System.Windows.MessageBox.Show(
                exception.ToString(),
                "EVEProfileSync Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    internal static void WriteUiTrace(string message)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "ui-trace.log");
            File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Ignore diagnostics failures.
        }
    }

    private static void LogStartupFailure(string appDataPath, Exception exception)
    {
        try
        {
            var logPath = Path.Combine(appDataPath, "startup.log");
            var message = $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(logPath, message);
        }
        catch
        {
            // Ignore logging failures and preserve the original startup error.
        }
    }
}
