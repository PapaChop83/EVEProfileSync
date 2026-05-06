using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EVEProfileSync.Core;

public sealed class SettingsDiscoveryService : ISettingsDiscoveryService
{
    public SettingsRoot Discover(string? manualRootPath = null)
    {
        var rootPath = ResolveRootPath(manualRootPath);
        if (!Directory.Exists(rootPath))
        {
            return new SettingsRoot(rootPath, Array.Empty<ServerInstallation>(), ResolveOverviewFolder());
        }

        var servers = EnumerateServerPaths(rootPath)
            .Select(CreateServerInstallation)
            .Where(server => server.Profiles.Count > 0)
            .OrderBy(server => server.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SettingsRoot(rootPath, servers, ResolveOverviewFolder());
    }

    private static string ResolveRootPath(string? manualRootPath)
    {
        if (!string.IsNullOrWhiteSpace(manualRootPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(manualRootPath));
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "CCP", "EVE");
    }

    private static IEnumerable<string> EnumerateServerPaths(string rootPath)
    {
        var directoryName = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (directoryName.StartsWith("c_", StringComparison.OrdinalIgnoreCase) ||
            directoryName.StartsWith("_", StringComparison.OrdinalIgnoreCase))
        {
            yield return rootPath;
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(rootPath))
        {
            if (Directory.EnumerateDirectories(directory, "settings_*").Any())
            {
                yield return directory;
            }
        }
    }

    private static ServerInstallation CreateServerInstallation(string serverPath)
    {
        var profiles = Directory.EnumerateDirectories(serverPath, "settings_*")
            .Select(profilePath => CreateProfile(Path.GetFileName(serverPath), profilePath))
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ServerInstallation(Path.GetFileName(serverPath), serverPath, profiles);
    }

    private static ProfileFolder CreateProfile(string serverName, string profilePath)
    {
        var datFiles = Directory.Exists(profilePath)
            ? Directory.EnumerateFiles(profilePath, "*.dat", SearchOption.TopDirectoryOnly).ToArray()
            : Array.Empty<string>();

        var characterFiles = datFiles
            .Select(path => CreateCharacterFile(path))
            .Where(file => file is not null)
            .Cast<CharacterSettingsFile>()
            .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var accountFiles = datFiles
            .Select(path => CreateAccountFile(path))
            .Where(file => file is not null)
            .Cast<AccountSettingsFile>()
            .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProfileFolder(serverName, Path.GetFileName(profilePath), profilePath, characterFiles, accountFiles);
    }

    private static CharacterSettingsFile? CreateCharacterFile(string path)
    {
        var fileName = Path.GetFileName(path);
        if (!fileName.StartsWith("core_char_", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var identifier = Path.GetFileNameWithoutExtension(fileName).Split('_').LastOrDefault() ?? string.Empty;
        if (!long.TryParse(identifier, out _))
        {
            return null;
        }

        return new CharacterSettingsFile(identifier, fileName, path);
    }

    private static AccountSettingsFile? CreateAccountFile(string path)
    {
        var fileName = Path.GetFileName(path);
        if (!fileName.StartsWith("core_user_", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var identifier = Path.GetFileNameWithoutExtension(fileName).Split('_').LastOrDefault() ?? string.Empty;
        if (!long.TryParse(identifier, out _))
        {
            return null;
        }

        return new AccountSettingsFile(identifier, fileName, path);
    }

    private static string? ResolveOverviewFolder()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
        {
            return null;
        }

        return Path.Combine(documents, "EVE", "Overview");
    }
}

public sealed class OverviewAssetService : IOverviewAssetService
{
    private static readonly string[] AllowedExtensions = [".xml", ".yaml", ".yml", ".txt", ".json"];

    public IReadOnlyList<string> GetOverviewAssets(string? overviewFolderPath)
    {
        if (string.IsNullOrWhiteSpace(overviewFolderPath) || !Directory.Exists(overviewFolderPath))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(overviewFolderPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(path => AllowedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed class ArtifactMappingService : IArtifactMappingService
{
    private readonly string _manifestPath;
    private readonly IOverviewAssetService _overviewAssetService;
    private readonly string _applicationDataPath;

    public ArtifactMappingService(string manifestPath, IOverviewAssetService overviewAssetService, string applicationDataPath)
    {
        _manifestPath = manifestPath;
        _overviewAssetService = overviewAssetService;
        _applicationDataPath = applicationDataPath;
    }

    public ArtifactManifestRoot LoadManifest() => ArtifactManifestLoader.LoadFromFile(_manifestPath);

    public SyncPlan BuildPlan(
        ProfileFolder sourceProfile,
        IReadOnlyList<SyncTarget> targets,
        IReadOnlyList<SyncOption> selectedOptions,
        IReadOnlyDictionary<SyncOption, SettingsFile> sourceSelections,
        string? overviewFolderPath)
    {
        if (targets.Count == 0)
        {
            throw new InvalidOperationException("At least one sync target must be selected.");
        }

        var expandedOptions = ExpandOptions(selectedOptions);
        var manifest = LoadManifest();
        var artifacts = new List<SyncArtifact>();
        var sourceSelectionDescriptions = BuildSourceSelectionDescriptions(sourceSelections);

        foreach (var option in expandedOptions)
        {
            var rules = manifest.Mappings.Where(mapping => mapping.Option == option).ToArray();
            foreach (var rule in rules)
            {
                if (rule.ArtifactKind == ArtifactKind.ProfileFile)
                {
                    artifacts.AddRange(BuildProfileFileArtifacts(rule, sourceProfile, targets, sourceSelections));
                    continue;
                }

                if (rule.ArtifactKind == ArtifactKind.OverviewExportPackage)
                {
                    artifacts.AddRange(BuildOverviewArtifacts(rule, sourceProfile, overviewFolderPath));
                }
            }
        }

        var summary = BuildSummary(sourceProfile, targets, expandedOptions, artifacts);
        var requiresManualOverviewImport = expandedOptions.Contains(SyncOption.OverviewSettings);
        return new SyncPlan(sourceProfile, targets, expandedOptions, sourceSelectionDescriptions, artifacts, summary, requiresManualOverviewImport);
    }

    private static IReadOnlyList<SyncOption> ExpandOptions(IReadOnlyList<SyncOption> selectedOptions)
    {
        var result = new HashSet<SyncOption>();
        foreach (var option in selectedOptions)
        {
            if (option == SyncOption.FullUiSync)
            {
                result.Add(SyncOption.WindowLayout);
                result.Add(SyncOption.NeocomColors);
            }
            else
            {
                result.Add(option);
            }
        }

        return result.OrderBy(option => option).ToArray();
    }

    private IEnumerable<SyncArtifact> BuildProfileFileArtifacts(
        ArtifactMappingRule rule,
        ProfileFolder sourceProfile,
        IReadOnlyList<SyncTarget> targets,
        IReadOnlyDictionary<SyncOption, SettingsFile> sourceSelections)
    {
        var sourceFiles = MatchFiles(sourceProfile.AllFiles, rule.FileNamePatterns).ToArray();
        if (sourceFiles.Length == 0)
        {
            yield break;
        }

        var applicableTargets = targets.Where(target => target.Option == rule.Option).ToArray();
        if (applicableTargets.Length == 0)
        {
            yield break;
        }

        var preferredSourceFile = sourceSelections.TryGetValue(rule.Option, out var configuredSource)
            ? sourceFiles.FirstOrDefault(file => string.Equals(file.FullPath, configuredSource.FullPath, StringComparison.OrdinalIgnoreCase))
            : null;

        foreach (var target in applicableTargets)
        {
            var sourceFile = preferredSourceFile
                ?? sourceFiles
                    .FirstOrDefault(file => string.Equals(file.FileName, target.TargetFile.FileName, StringComparison.OrdinalIgnoreCase))
                ?? sourceFiles.OrderByDescending(file => file.LastWriteTimeUtc)
                    .ThenBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
                    .First();

            if (string.Equals(sourceFile.FullPath, target.TargetFile.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return new SyncArtifact(
                rule.Option,
                ArtifactKind.ProfileFile,
                Path.Combine(sourceProfile.Name, target.TargetFile.FileName),
                sourceFile.FullPath,
                target.TargetFile.FullPath,
                $"{rule.Option}: {sourceFile.FileName} -> {target.DisplayName}");
        }
    }

    private IEnumerable<SyncArtifact> BuildOverviewArtifacts(
        ArtifactMappingRule rule,
        ProfileFolder sourceProfile,
        string? overviewFolderPath)
    {
        var overviewAssets = _overviewAssetService.GetOverviewAssets(overviewFolderPath);
        foreach (var asset in overviewAssets)
        {
            if (!MatchesAnyPattern(Path.GetFileName(asset), rule.FileNamePatterns))
            {
                continue;
            }

            var packageRoot = Path.Combine(_applicationDataPath, "overview-packages", SanitizePathSegment(sourceProfile.Name));
            var destinationPath = Path.Combine(packageRoot, Path.GetFileName(asset));
            yield return new SyncArtifact(
                rule.Option,
                ArtifactKind.OverviewExportPackage,
                Path.Combine("overview-packages", sourceProfile.Name, Path.GetFileName(asset)),
                asset,
                destinationPath,
                $"{rule.Option}: package {Path.GetFileName(asset)} for manual import.");
        }
    }

    private static IEnumerable<SettingsFile> MatchFiles(IEnumerable<SettingsFile> files, IReadOnlyList<string> patterns)
    {
        foreach (var file in files)
        {
            if (MatchesAnyPattern(file.FileName, patterns))
            {
                yield return file;
            }
        }
    }

    private static bool MatchesAnyPattern(string fileName, IReadOnlyList<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (MatchesPattern(fileName, pattern))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(
            fileName,
            regexPattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string BuildSummary(
        ProfileFolder sourceProfile,
        IReadOnlyList<SyncTarget> targets,
        IReadOnlyList<SyncOption> options,
        IReadOnlyList<SyncArtifact> artifacts)
    {
        var optionSummary = string.Join(", ", options.Select(option => option.ToString()));
        return $"Source {sourceProfile} -> {targets.Count} target(s), {artifacts.Count} artifact(s), options: {optionSummary}.";
    }

    private static IReadOnlyDictionary<SyncOption, string> BuildSourceSelectionDescriptions(
        IReadOnlyDictionary<SyncOption, SettingsFile> sourceSelections)
    {
        return sourceSelections.ToDictionary(
            pair => pair.Key,
            pair => $"{pair.Value.FileName}",
            EqualityComparer<SyncOption>.Default);
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalidCharacters.Contains(character) ? '_' : character));
    }
}

public sealed class BackupService : IBackupService
{
    private readonly string _applicationDataPath;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string ExportManifestPath = "manifest.json";
    private const string ExportFilesFolder = "files";
    private const long MaxExportEntryBytes = 10 * 1024 * 1024;
    private static readonly Regex SettingsFileNamePattern = new(@"^core_(char|user)_\d+\.dat$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public BackupService(string applicationDataPath)
    {
        _applicationDataPath = applicationDataPath;
    }

    public async Task<BackupRecord> CreateBackupAsync(SyncPlan plan, CancellationToken cancellationToken = default)
    {
        var backupId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..23];
        var backupRoot = Path.Combine(_applicationDataPath, "backups", backupId);
        var backupFilesRoot = Path.Combine(backupRoot, "files");
        Directory.CreateDirectory(backupFilesRoot);

        var fileRecords = new List<BackupFileRecord>();
        var uniqueDestinationPaths = plan.Artifacts
            .Select(artifact => artifact.DestinationPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var destinationPath in uniqueDestinationPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(destinationPath))
            {
                continue;
            }

            var backupFileName = $"{fileRecords.Count:D4}_{Path.GetFileName(destinationPath)}";
            var backupFilePath = Path.Combine(backupFilesRoot, backupFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(backupFilePath)!);
            File.Copy(destinationPath, backupFilePath, overwrite: true);
            fileRecords.Add(new BackupFileRecord(destinationPath, backupFilePath, backupFileName));
        }

        var backup = new BackupRecord(
            backupId,
            backupRoot,
            DateTimeOffset.UtcNow,
            plan.SourceProfile.FullPath,
            plan.Targets.Select(target => target.TargetFile.FullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            fileRecords);

        var metadataPath = Path.Combine(backupRoot, "backup.json");
        var json = JsonSerializer.Serialize(backup, _jsonOptions);
        await File.WriteAllTextAsync(metadataPath, json, cancellationToken).ConfigureAwait(false);
        return backup;
    }

    public async Task<string> ExportProfileAsync(ProfileFolder profile, string exportDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(exportDirectory);
        var exportId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..23];
        var exportName = $"{SanitizeFileName(profile.ServerName)}_{SanitizeFileName(profile.Name)}_{exportId}.eveprofilesyncbackup";
        var exportPath = Path.Combine(exportDirectory, exportName);

        using var archive = ZipFile.Open(exportPath, ZipArchiveMode.Create);
        var files = new List<ExportBackupFileRecord>();
        var allFiles = profile.AllFiles
            .DistinctBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var index = 0; index < allFiles.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = allFiles[index];
            if (!File.Exists(file.FullPath))
            {
                continue;
            }

            var archivePath = Path.Combine(ExportFilesFolder, $"{index:D4}_{file.FileName}").Replace('\\', '/');
            var relativePath = Path.GetRelativePath(profile.FullPath, file.FullPath);
            archive.CreateEntryFromFile(file.FullPath, archivePath, CompressionLevel.Optimal);
            files.Add(new ExportBackupFileRecord(null, archivePath, file.FileName, NormalizeArchivePath(relativePath)));
        }

        var manifest = new ExportBackupRecord(
            exportId,
            DateTimeOffset.UtcNow,
            $"{profile.ServerName}/{profile.Name}",
            files);

        var manifestEntry = archive.CreateEntry(ExportManifestPath, CompressionLevel.Optimal);
        await using (var stream = manifestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(stream, manifest, _jsonOptions, cancellationToken).ConfigureAwait(false);
        }

        return exportPath;
    }

    public async Task<RestorePreview> LoadRestorePreviewAsync(string backupFilePath, ProfileFolder targetProfile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupFilePath) || !File.Exists(backupFilePath))
        {
            throw new FileNotFoundException("Backup export file was not found.", backupFilePath);
        }

        using var archive = ZipFile.OpenRead(backupFilePath);
        var manifestEntry = archive.GetEntry(ExportManifestPath)
            ?? throw new InvalidDataException("The selected backup file is missing manifest metadata.");

        await using var stream = manifestEntry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<ExportBackupRecord>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The selected backup file could not be read.");

        var previewFiles = manifest.Files
            .Select(file => BuildRestorePreviewFile(file, archive, targetProfile))
            .OrderBy(file => file.DestinationPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new RestorePreview(backupFilePath, manifest, previewFiles);
    }

    public Task RestoreAsync(RestorePlan plan, CancellationToken cancellationToken = default)
    {
        foreach (var file in plan.Backup.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(file.OriginalPath)!);
            File.Copy(file.BackupPath, file.OriginalPath, overwrite: true);
        }

        return Task.CompletedTask;
    }

    public Task RestoreExportAsync(RestorePreview preview, CancellationToken cancellationToken = default)
    {
        using var archive = ZipFile.OpenRead(preview.BackupFilePath);
        foreach (var file in preview.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.GetEntry(file.ArchivePath)
                ?? throw new InvalidDataException($"Backup file is missing archived content for {file.FileName}.");
            ValidateEntrySize(entry, file.FileName);

            Directory.CreateDirectory(Path.GetDirectoryName(file.DestinationPath)!);
            entry.ExtractToFile(file.DestinationPath, overwrite: true);
        }

        return Task.CompletedTask;
    }

    private static RestorePreviewFile BuildRestorePreviewFile(ExportBackupFileRecord file, ZipArchive archive, ProfileFolder targetProfile)
    {
        var archiveEntry = archive.GetEntry(file.ArchivePath)
            ?? throw new InvalidDataException($"Backup file is missing archived content for {file.FileName}.");
        ValidateEntrySize(archiveEntry, file.FileName);
        ValidateArchivePath(file.ArchivePath);
        ValidateSettingsFileName(file.FileName);

        var relativePath = ResolveRelativeRestorePath(file, targetProfile);
        ValidateRelativeRestorePath(relativePath);

        var destinationPath = ResolveUnderProfile(targetProfile.FullPath, relativePath);
        return new RestorePreviewFile(relativePath, file.ArchivePath, file.FileName, destinationPath);
    }

    private static string ResolveRelativeRestorePath(ExportBackupFileRecord file, ProfileFolder targetProfile)
    {
        if (!string.IsNullOrWhiteSpace(file.RelativePath))
        {
            return NormalizeArchivePath(file.RelativePath);
        }

        if (string.IsNullOrWhiteSpace(file.OriginalPath))
        {
            throw new InvalidDataException($"Backup manifest does not include a restore path for {file.FileName}.");
        }

        var originalFullPath = Path.GetFullPath(file.OriginalPath);
        var targetRoot = GetDirectoryRootWithSeparator(targetProfile.FullPath);
        if (!IsPathWithinRoot(originalFullPath, targetRoot))
        {
            throw new InvalidDataException($"Legacy backup path is outside the selected profile: {file.OriginalPath}");
        }

        return NormalizeArchivePath(Path.GetRelativePath(targetProfile.FullPath, originalFullPath));
    }

    private static string ResolveUnderProfile(string profilePath, string relativePath)
    {
        var root = GetDirectoryRootWithSeparator(profilePath);
        var destinationPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!IsPathWithinRoot(destinationPath, root))
        {
            throw new InvalidDataException($"Backup restore path escapes the selected profile: {relativePath}");
        }

        return destinationPath;
    }

    private static void ValidateArchivePath(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || Path.IsPathRooted(archivePath))
        {
            throw new InvalidDataException("Backup archive entry path is invalid.");
        }

        var normalized = NormalizeArchivePath(archivePath);
        if (!normalized.StartsWith($"{ExportFilesFolder}/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Split('/').Any(segment => segment is "." or ".." || string.IsNullOrWhiteSpace(segment)))
        {
            throw new InvalidDataException($"Backup archive entry path is unsafe: {archivePath}");
        }
    }

    private static void ValidateRelativeRestorePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"Backup restore path must be relative: {relativePath}");
        }

        var segments = NormalizeArchivePath(relativePath).Split('/');
        if (segments.Length != 1 || segments.Any(segment => segment is "." or ".." || string.IsNullOrWhiteSpace(segment)))
        {
            throw new InvalidDataException($"Backup restore path is unsafe: {relativePath}");
        }
    }

    private static void ValidateSettingsFileName(string fileName)
    {
        if (!SettingsFileNamePattern.IsMatch(fileName))
        {
            throw new InvalidDataException($"Backup contains an unsupported settings file: {fileName}");
        }
    }

    private static void ValidateEntrySize(ZipArchiveEntry entry, string fileName)
    {
        if (entry.Length > MaxExportEntryBytes)
        {
            throw new InvalidDataException($"Backup entry is too large to restore safely: {fileName}");
        }
    }

    private static bool IsPathWithinRoot(string path, string root)
    {
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDirectoryRootWithSeparator(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static string NormalizeArchivePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalidCharacters.Contains(character) ? '_' : character));
    }
}

public sealed class SyncExecutor : ISyncExecutor
{
    private readonly IBackupService _backupService;
    private readonly IProcessGuardService _processGuardService;

    public SyncExecutor(IBackupService backupService, IProcessGuardService processGuardService)
    {
        _backupService = backupService;
        _processGuardService = processGuardService;
    }

    public async Task<SyncResult> ExecuteAsync(SyncPlan plan, CancellationToken cancellationToken = default)
    {
        if (_processGuardService.IsEveRunning())
        {
            throw new InvalidOperationException("EVE Online appears to be running. Close the client before syncing.");
        }

        var backup = await _backupService.CreateBackupAsync(plan, cancellationToken).ConfigureAwait(false);
        var copiedArtifacts = 0;

        foreach (var artifact in plan.Artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSafeWriteDestination(artifact.DestinationPath);
            Directory.CreateDirectory(Path.GetDirectoryName(artifact.DestinationPath)!);
            if (artifact.Option == SyncOption.WindowLayout &&
                artifact.Kind == ArtifactKind.ProfileFile &&
                Path.GetFileName(artifact.SourcePath).StartsWith("core_char_", StringComparison.OrdinalIgnoreCase))
            {
                CharacterSettingsMergeService.CopyLayoutPreservingTargetChat(artifact.SourcePath, artifact.DestinationPath);
            }
            else
            {
                File.Copy(artifact.SourcePath, artifact.DestinationPath, overwrite: true);
            }

            copiedArtifacts++;
        }

        var message = plan.RequiresManualOverviewImport
            ? "Sync completed. Overview packages were prepared for in-game import."
            : "Sync completed.";

        return new SyncResult(plan, backup, copiedArtifacts, message);
    }

    private static void EnsureSafeWriteDestination(string destinationPath)
    {
        var fullPath = Path.GetFullPath(destinationPath);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException($"Destination path is invalid: {destinationPath}");
        }
    }
}

public static class CharacterSettingsMergeService
{
    private static readonly byte[] SettingRecordValueMarker = [0x2c, 0x2f, 0x08];
    private static readonly byte[] ChatChannelMarker = "chatchannel"u8.ToArray();
    private static readonly byte[] XmppChatChannelsMarker = "XmppChatChannels"u8.ToArray();
    private static readonly byte[] ChannelSettingsDialogMarker = "ChannelSettingsDlg"u8.ToArray();
    private static readonly byte[] ChatWindowStackMarker = "ChatWindowStack"u8.ToArray();

    public static void CopyLayoutPreservingTargetChat(string sourcePath, string destinationPath)
    {
        var sourceBytes = File.ReadAllBytes(sourcePath);
        if (!File.Exists(destinationPath))
        {
            File.WriteAllBytes(destinationPath, sourceBytes);
            return;
        }

        var targetBytes = File.ReadAllBytes(destinationPath);
        var mergedBytes = MergeLayoutPreservingTargetChat(sourceBytes, targetBytes);
        File.WriteAllBytes(destinationPath, mergedBytes);
    }

    public static byte[] MergeLayoutPreservingTargetChat(byte[] sourceBytes, byte[] targetBytes)
    {
        if (!TryReadBlueMarshalRecordCount(sourceBytes, out var sourceRecordCount) ||
            !TryReadBlueMarshalRecordCount(targetBytes, out _))
        {
            return sourceBytes;
        }

        var sourceRecords = FindTopLevelSettingRecords(sourceBytes);
        var targetRecords = FindTopLevelSettingRecords(targetBytes)
            .Where(record => IsChatRelatedRecord(targetBytes, record))
            .GroupBy(record => record.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        if (sourceRecords.Count == 0 || targetRecords.Count == 0)
        {
            return sourceBytes;
        }

        var output = new List<byte>(sourceBytes.Length);
        output.AddRange(sourceBytes.Take(sourceRecords[0].Start));

        var outputRecordCount = 0;
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceRecord in sourceRecords)
        {
            seenKeys.Add(sourceRecord.Key);
            if (IsChatRelatedRecord(sourceBytes, sourceRecord))
            {
                if (targetRecords.TryGetValue(sourceRecord.Key, out var targetRecord))
                {
                    AppendRecord(output, targetBytes, targetRecord);
                    outputRecordCount++;
                }

                continue;
            }

            AppendRecord(output, sourceBytes, sourceRecord);
            outputRecordCount++;
        }

        foreach (var targetRecord in targetRecords.Values.Where(record => !seenKeys.Contains(record.Key)))
        {
            AppendRecord(output, targetBytes, targetRecord);
            outputRecordCount++;
        }

        var mergedBytes = output.ToArray();
        var mergedRecordCount = sourceRecordCount + outputRecordCount - sourceRecords.Count;
        WriteBlueMarshalRecordCount(mergedBytes, mergedRecordCount);
        return mergedBytes;
    }

    private static IReadOnlyList<SettingRecord> FindTopLevelSettingRecords(byte[] bytes)
    {
        var starts = new List<SettingRecordStart>();
        for (var index = 5; index < bytes.Length - 4; index++)
        {
            if (!TryReadSerializedString(bytes, index, out var key, out var afterKey))
            {
                continue;
            }

            if (afterKey + SettingRecordValueMarker.Length > bytes.Length ||
                !bytes.AsSpan(afterKey, SettingRecordValueMarker.Length).SequenceEqual(SettingRecordValueMarker))
            {
                continue;
            }

            starts.Add(new SettingRecordStart(key, index));
        }

        var records = new List<SettingRecord>();
        for (var index = 0; index < starts.Count; index++)
        {
            var start = starts[index];
            var end = index + 1 < starts.Count ? starts[index + 1].Start : bytes.Length;
            records.Add(new SettingRecord(start.Key, start.Start, end));
        }

        return records;
    }

    private static bool TryReadSerializedString(byte[] bytes, int offset, out string value, out int nextOffset)
    {
        value = string.Empty;
        nextOffset = offset;
        if (offset + 2 > bytes.Length)
        {
            return false;
        }

        var opcode = bytes[offset];
        if (opcode is not (0x13 or 0x2e or 0x53))
        {
            return false;
        }

        var length = bytes[offset + 1];
        var stringStart = offset + 2;
        var stringEnd = stringStart + length;
        if (stringEnd > bytes.Length || length == 0)
        {
            return false;
        }

        for (var index = stringStart; index < stringEnd; index++)
        {
            if (bytes[index] < 32 || bytes[index] > 126)
            {
                return false;
            }
        }

        value = System.Text.Encoding.ASCII.GetString(bytes, stringStart, length);
        nextOffset = stringEnd;
        return true;
    }

    private static bool IsChatRelatedRecord(byte[] bytes, SettingRecord record)
    {
        if (record.Key.Contains("chat", StringComparison.OrdinalIgnoreCase) ||
            record.Key.Contains("channel", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var span = bytes.AsSpan(record.Start, record.End - record.Start);
        return ContainsOrdinalIgnoreCase(span, ChatChannelMarker) ||
               ContainsOrdinal(span, XmppChatChannelsMarker) ||
               ContainsOrdinal(span, ChannelSettingsDialogMarker) ||
               ContainsOrdinal(span, ChatWindowStackMarker);
    }

    private static bool TryReadBlueMarshalRecordCount(byte[] bytes, out int count)
    {
        count = 0;
        if (bytes.Length < 5 || bytes[0] != 0x7e)
        {
            return false;
        }

        count = BitConverter.ToInt32(bytes, 1);
        return count >= 0;
    }

    private static void WriteBlueMarshalRecordCount(byte[] bytes, int count)
    {
        var countBytes = BitConverter.GetBytes(count);
        Array.Copy(countBytes, 0, bytes, 1, countBytes.Length);
    }

    private static void AppendRecord(List<byte> output, byte[] bytes, SettingRecord record)
    {
        output.AddRange(bytes.Skip(record.Start).Take(record.End - record.Start));
    }

    private static bool ContainsOrdinal(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        return haystack.IndexOf(needle) >= 0;
    }

    private static bool ContainsOrdinalIgnoreCase(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        for (var index = 0; index <= haystack.Length - needle.Length; index++)
        {
            var matches = true;
            for (var needleIndex = 0; needleIndex < needle.Length; needleIndex++)
            {
                if (ToAsciiLower(haystack[index + needleIndex]) != ToAsciiLower(needle[needleIndex]))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static byte ToAsciiLower(byte value)
    {
        return value is >= 65 and <= 90 ? (byte)(value + 32) : value;
    }

    private sealed record SettingRecordStart(string Key, int Start);
    private sealed record SettingRecord(string Key, int Start, int End);
}

public sealed class ProcessGuardService : IProcessGuardService
{
    public bool IsEveRunning()
    {
        try
        {
            return Process.GetProcessesByName("exefile").Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
