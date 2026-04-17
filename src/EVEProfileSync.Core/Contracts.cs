namespace EVEProfileSync.Core;

public interface ISettingsDiscoveryService
{
    SettingsRoot Discover(string? manualRootPath = null);
}

public interface IArtifactMappingService
{
    ArtifactManifestRoot LoadManifest();

    SyncPlan BuildPlan(
        ProfileFolder sourceProfile,
        IReadOnlyList<SyncTarget> targets,
        IReadOnlyList<SyncOption> selectedOptions,
        IReadOnlyDictionary<SyncOption, SettingsFile> sourceSelections,
        string? overviewFolderPath);
}

public interface IOverviewAssetService
{
    IReadOnlyList<string> GetOverviewAssets(string? overviewFolderPath);
}

public interface IBackupService
{
    Task<BackupRecord> CreateBackupAsync(SyncPlan plan, CancellationToken cancellationToken = default);

    Task<string> ExportProfileAsync(ProfileFolder profile, string exportDirectory, CancellationToken cancellationToken = default);

    Task<RestorePreview> LoadRestorePreviewAsync(string backupFilePath, CancellationToken cancellationToken = default);

    Task RestoreAsync(RestorePlan plan, CancellationToken cancellationToken = default);

    Task RestoreExportAsync(RestorePreview preview, CancellationToken cancellationToken = default);
}

public interface ISyncExecutor
{
    Task<SyncResult> ExecuteAsync(SyncPlan plan, CancellationToken cancellationToken = default);
}

public interface IProcessGuardService
{
    bool IsEveRunning();
}
