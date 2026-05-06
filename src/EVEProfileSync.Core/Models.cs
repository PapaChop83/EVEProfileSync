using System.Text.Json.Serialization;

namespace EVEProfileSync.Core;

public sealed record SettingsRoot(
    string RootPath,
    IReadOnlyList<ServerInstallation> Servers,
    string? OverviewFolderPath);

public sealed record ServerInstallation(
    string Name,
    string FullPath,
    IReadOnlyList<ProfileFolder> Profiles);

public sealed record ProfileFolder(
    string ServerName,
    string Name,
    string FullPath,
    IReadOnlyList<CharacterSettingsFile> CharacterFiles,
    IReadOnlyList<AccountSettingsFile> AccountFiles)
{
    public IReadOnlyList<SettingsFile> AllFiles =>
        CharacterFiles.Cast<SettingsFile>().Concat(AccountFiles).ToArray();

    public override string ToString() => $"{ServerName} / {Name}";
}

public abstract record SettingsFile(string Id, string FileName, string FullPath)
{
    public DateTime LastWriteTimeUtc => File.GetLastWriteTimeUtc(FullPath);
}

public sealed record CharacterSettingsFile(string CharacterId, string FileName, string FullPath)
    : SettingsFile(CharacterId, FileName, FullPath);

public sealed record AccountSettingsFile(string AccountId, string FileName, string FullPath)
    : SettingsFile(AccountId, FileName, FullPath);

public sealed record SyncTarget(
    string Id,
    string DisplayName,
    SyncOption Option,
    SettingsFile TargetFile);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SyncOption
{
    WindowLayout,
    OverviewSettings,
    NeocomColors,
    FullUiSync,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ArtifactKind
{
    ProfileFile,
    OverviewExportPackage,
}

public sealed record SyncArtifact(
    SyncOption Option,
    ArtifactKind Kind,
    string RelativePath,
    string SourcePath,
    string DestinationPath,
    string Description);

public sealed record SyncPlan(
    ProfileFolder SourceProfile,
    IReadOnlyList<SyncTarget> Targets,
    IReadOnlyList<SyncOption> SelectedOptions,
    IReadOnlyDictionary<SyncOption, string> SourceSelectionDescriptions,
    IReadOnlyList<SyncArtifact> Artifacts,
    string Summary,
    bool RequiresManualOverviewImport);

public sealed record BackupFileRecord(
    string OriginalPath,
    string BackupPath,
    string RelativePath);

public sealed record BackupRecord(
    string Id,
    string RootPath,
    DateTimeOffset CreatedAt,
    string SourceProfilePath,
    IReadOnlyList<string> TargetPaths,
    IReadOnlyList<BackupFileRecord> Files);

public sealed record RestorePlan(BackupRecord Backup);

public sealed record ExportBackupFileRecord(
    string? OriginalPath,
    string ArchivePath,
    string FileName,
    string? RelativePath = null);

public sealed record ExportBackupRecord(
    string Id,
    DateTimeOffset CreatedAt,
    string? SourceProfilePath,
    IReadOnlyList<ExportBackupFileRecord> Files);

public sealed record RestorePreview(
    string BackupFilePath,
    ExportBackupRecord Backup,
    IReadOnlyList<RestorePreviewFile> Files)
{
    public IReadOnlyList<string> PathsToRestore =>
        Files.Select(file => file.DestinationPath).ToArray();
}

public sealed record RestorePreviewFile(
    string RelativePath,
    string ArchivePath,
    string FileName,
    string DestinationPath);

public sealed record SyncResult(
    SyncPlan Plan,
    BackupRecord Backup,
    int CopiedArtifacts,
    string Message);
