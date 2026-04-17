using System.IO;
using System.Windows;
using EVEProfileSync.Core;

namespace EVEProfileSync.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EVEProfileSync");
        Directory.CreateDirectory(appDataPath);

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
    }
}
