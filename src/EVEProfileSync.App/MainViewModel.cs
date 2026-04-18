using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Forms;
using EVEProfileSync.Core;

namespace EVEProfileSync.App;

public sealed class MainViewModel : ObservableObject
{
    private readonly ISettingsDiscoveryService _discoveryService;
    private readonly IArtifactMappingService _mappingService;
    private readonly IBackupService _backupService;
    private readonly ISyncExecutor _syncExecutor;
    private readonly IProcessGuardService _processGuardService;
    private readonly string _applicationDataPath;
    private readonly string _appBaseDirectory;
    private readonly AccountLabelStore _accountLabelStore;
    private readonly CharacterNameStore _characterNameStore;
    private bool _isRebuildingTargets;

    private string _manualRootPath = string.Empty;
    private string _statusText = "Ready.";
    private ProfileItemViewModel? _selectedSourceProfile;
    private SourceSettingsFileViewModel? _selectedWindowLayoutSource;
    private SourceSettingsFileViewModel? _selectedNeocomSource;
    private SettingsRoot? _settingsRoot;

    public MainViewModel(
        ISettingsDiscoveryService discoveryService,
        IArtifactMappingService mappingService,
        IBackupService backupService,
        ISyncExecutor syncExecutor,
        IProcessGuardService processGuardService,
        string applicationDataPath,
        string appBaseDirectory)
    {
        _discoveryService = discoveryService;
        _mappingService = mappingService;
        _backupService = backupService;
        _syncExecutor = syncExecutor;
        _processGuardService = processGuardService;
        _applicationDataPath = applicationDataPath;
        _appBaseDirectory = appBaseDirectory;
        _accountLabelStore = new AccountLabelStore(Path.Combine(_applicationDataPath, "account-labels.json"));
        _characterNameStore = new CharacterNameStore(Path.Combine(_applicationDataPath, "character-names.json"));

        Profiles = new ObservableCollection<ProfileItemViewModel>();
        TargetProfiles = new ObservableCollection<SelectableSyncTargetViewModel>();
        CharacterTargets = new ObservableCollection<SelectableSyncTargetViewModel>();
        AccountTargets = new ObservableCollection<SelectableSyncTargetViewModel>();
        AccountOverviewItems = new ObservableCollection<AccountOverviewItemViewModel>();
        WindowLayoutSources = new ObservableCollection<SourceSettingsFileViewModel>();
        NeocomSources = new ObservableCollection<SourceSettingsFileViewModel>();
        PreviewItems = new ObservableCollection<string>();
    }

    public ObservableCollection<ProfileItemViewModel> Profiles { get; }

    public ObservableCollection<SelectableSyncTargetViewModel> TargetProfiles { get; }

    public ObservableCollection<SelectableSyncTargetViewModel> CharacterTargets { get; }

    public ObservableCollection<SelectableSyncTargetViewModel> AccountTargets { get; }

    public ObservableCollection<AccountOverviewItemViewModel> AccountOverviewItems { get; }

    public ObservableCollection<SourceSettingsFileViewModel> WindowLayoutSources { get; }

    public ObservableCollection<SourceSettingsFileViewModel> NeocomSources { get; }

    public ObservableCollection<string> PreviewItems { get; }

    public string ManualRootPath
    {
        get => _manualRootPath;
        set => SetProperty(ref _manualRootPath, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public ProfileItemViewModel? SelectedSourceProfile
    {
        get => _selectedSourceProfile;
        set
        {
            if (SetProperty(ref _selectedSourceProfile, value))
            {
                RebuildTargets();
            }
        }
    }

    public string ExportFolderPath => Path.Combine(_appBaseDirectory, "Exports");

    public SourceSettingsFileViewModel? SelectedWindowLayoutSource
    {
        get => _selectedWindowLayoutSource;
        set
        {
            if (SetProperty(ref _selectedWindowLayoutSource, value) && !_isRebuildingTargets)
            {
                RebuildCharacterTargets();
            }
        }
    }

    public SourceSettingsFileViewModel? SelectedNeocomSource
    {
        get => _selectedNeocomSource;
        set => SetProperty(ref _selectedNeocomSource, value);
    }

    public async Task InitializeAsync()
    {
        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        _settingsRoot = _discoveryService.Discover(ManualRootPath);
        if (string.IsNullOrWhiteSpace(ManualRootPath))
        {
            ManualRootPath = _settingsRoot.RootPath;
        }

        Profiles.Clear();
        foreach (var profile in _settingsRoot.Servers.SelectMany(server => server.Profiles))
        {
            Profiles.Add(new ProfileItemViewModel(profile));
        }

        SelectedSourceProfile ??= Profiles.FirstOrDefault();
        if (SelectedSourceProfile is not null && !Profiles.Contains(SelectedSourceProfile))
        {
            SelectedSourceProfile = Profiles.FirstOrDefault();
        }

        RebuildTargets();
        await ResolveCharacterNamesAsync();
        await RefreshPreviewAsync();

        StatusText = Profiles.Count == 0
            ? "No profiles found yet. Select your EVE settings root or create a profile in the launcher first."
            : $"Loaded {Profiles.Count} profile(s) from {_settingsRoot.RootPath}.";
    }

    public async Task RefreshCharacterNamesForCurrentProfileAsync()
    {
        await ResolveCharacterNamesAsync();
    }

    public void BrowseForFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the EVE settings folder root or a specific server folder.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };

        if (Directory.Exists(ManualRootPath))
        {
            dialog.SelectedPath = ManualRootPath;
        }

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            ManualRootPath = dialog.SelectedPath;
        }
    }

    public Task RefreshPreviewAsync()
    {
        PreviewItems.Clear();

        if (SelectedSourceProfile is null)
        {
            PreviewItems.Add("Select a source profile to preview the sync plan.");
            return Task.CompletedTask;
        }

        var selectedTargets = TargetProfiles.Where(item => item.IsSelected).Select(item => item.Target).ToArray();
        if (selectedTargets.Length == 0)
        {
            PreviewItems.Add("Select at least one target character or account.");
            return Task.CompletedTask;
        }

        var selectedOptions = GetSelectedOptions(selectedTargets);
        if (selectedOptions.Length == 0)
        {
            PreviewItems.Add("Select at least one target to preview.");
            return Task.CompletedTask;
        }

        try
        {
            var plan = _mappingService.BuildPlan(
                SelectedSourceProfile.Profile,
                selectedTargets,
                selectedOptions,
                GetSourceSelections(),
                _settingsRoot?.OverviewFolderPath);

            PreviewItems.Add(plan.Summary);
            foreach (var sourceSelection in plan.SourceSelectionDescriptions)
            {
                PreviewItems.Add($"Source {sourceSelection.Key}: {sourceSelection.Value}");
            }
            foreach (var artifact in plan.Artifacts)
            {
                PreviewItems.Add(artifact.Description);
            }

            if (plan.Artifacts.Count == 0)
            {
                PreviewItems.Add("No matching artifacts were found for the selected options.");
            }
        }
        catch (Exception exception)
        {
            PreviewItems.Add(exception.Message);
        }

        return Task.CompletedTask;
    }

    public async Task SyncAsync()
    {
        if (SelectedSourceProfile is null)
        {
            StatusText = "Pick a source profile before syncing.";
            return;
        }

        var selectedTargets = TargetProfiles.Where(item => item.IsSelected).Select(item => item.Target).ToArray();
        var selectedOptions = GetSelectedOptions(selectedTargets);
        if (selectedTargets.Length == 0 || selectedOptions.Length == 0)
        {
            StatusText = "Select at least one target character or account.";
            return;
        }

        await ExecuteSyncAsync(selectedTargets, selectedOptions);
    }

    public async Task SyncLayoutAsync()
    {
        var targets = CharacterTargets.Where(item => item.IsSelected).Select(item => item.Target).ToArray();
        if (targets.Length == 0)
        {
            StatusText = "Select at least one UI layout target.";
            return;
        }

        await ExecuteSyncAsync(targets, [SyncOption.WindowLayout]);
    }

    public async Task SyncNeocomAsync()
    {
        var targets = AccountTargets.Where(item => item.IsSelected).Select(item => item.Target).ToArray();
        if (targets.Length == 0)
        {
            StatusText = "Select at least one NEOCOM account target.";
            return;
        }

        await ExecuteSyncAsync(targets, [SyncOption.NeocomColors]);
    }

    public async Task<string> ExportCurrentProfileAsync()
    {
        if (SelectedSourceProfile is null)
        {
            throw new InvalidOperationException("Pick a source profile before exporting.");
        }

        var exportPath = await _backupService.ExportProfileAsync(SelectedSourceProfile.Profile, ExportFolderPath);
        StatusText = $"Export created: {Path.GetFileName(exportPath)}";
        return exportPath;
    }

    public async Task<RestorePreview> LoadRestorePreviewAsync(string backupFilePath)
    {
        if (_processGuardService.IsEveRunning())
        {
            throw new InvalidOperationException("Close EVE Online before restoring a backup.");
        }

        return await _backupService.LoadRestorePreviewAsync(backupFilePath);
    }

    public async Task RestoreFromExportAsync(RestorePreview preview)
    {
        if (_processGuardService.IsEveRunning())
        {
            throw new InvalidOperationException("Close EVE Online before restoring a backup.");
        }

        await _backupService.RestoreExportAsync(preview);
        await LoadAsync();
        StatusText = $"Restored export {Path.GetFileName(preview.BackupFilePath)}.";
    }

    public void SelectAllTargets()
    {
        foreach (var target in TargetProfiles)
        {
            target.IsSelected = true;
        }
    }

    public void DeselectAllTargets()
    {
        foreach (var target in TargetProfiles)
        {
            target.IsSelected = false;
        }
    }

    public void SelectCharacterTargets()
    {
        foreach (var target in CharacterTargets)
        {
            target.IsSelected = true;
        }
    }

    public void DeselectCharacterTargets()
    {
        foreach (var target in CharacterTargets)
        {
            target.IsSelected = false;
        }
    }

    public void SelectAccountTargets()
    {
        foreach (var target in AccountTargets)
        {
            target.IsSelected = true;
        }
    }

    public void DeselectAccountTargets()
    {
        foreach (var target in AccountTargets)
        {
            target.IsSelected = false;
        }
    }

    public void SaveAccountLabel(AccountOverviewItemViewModel account)
    {
        _accountLabelStore.SetLabel(account.AccountId, account.DraftLabel);
        account.CommitLabel();
        RefreshAccountPresentation(account.AccountId);
    }

    private SyncOption[] GetSelectedOptions(IReadOnlyList<SyncTarget> selectedTargets)
    {
        var options = new HashSet<SyncOption>();
        foreach (var target in selectedTargets)
        {
            options.Add(target.Option);
        }

        return options.OrderBy(option => option).ToArray();
    }

    private IReadOnlyDictionary<SyncOption, SettingsFile> GetSourceSelections()
    {
        var selections = new Dictionary<SyncOption, SettingsFile>();

        if (SelectedWindowLayoutSource is not null)
        {
            selections[SyncOption.WindowLayout] = SelectedWindowLayoutSource.File;
        }

        if (SelectedNeocomSource is not null)
        {
            selections[SyncOption.NeocomColors] = SelectedNeocomSource.File;
        }

        return selections;
    }

    private async Task ExecuteSyncAsync(IReadOnlyList<SyncTarget> selectedTargets, IReadOnlyList<SyncOption> selectedOptions)
    {
        if (SelectedSourceProfile is null)
        {
            throw new InvalidOperationException("Pick a source profile before syncing.");
        }

        var plan = _mappingService.BuildPlan(
            SelectedSourceProfile.Profile,
            selectedTargets,
            selectedOptions,
            GetSourceSelections(),
            _settingsRoot?.OverviewFolderPath);

        if (plan.Artifacts.Count == 0)
        {
            StatusText = "Nothing to sync for the current selection.";
            return;
        }

        var result = await _syncExecutor.ExecuteAsync(plan);
        await RefreshPreviewAsync();
        StatusText = $"{result.Message} Backup: {result.Backup.Id}";
    }

    private void RebuildTargets()
    {
        _isRebuildingTargets = true;
        TargetProfiles.Clear();
        CharacterTargets.Clear();
        AccountTargets.Clear();
        AccountOverviewItems.Clear();
        WindowLayoutSources.Clear();
        NeocomSources.Clear();
        SelectedWindowLayoutSource = null;
        SelectedNeocomSource = null;

        if (SelectedSourceProfile is null)
        {
            return;
        }

        foreach (var characterFile in SelectedSourceProfile.Profile.CharacterFiles)
        {
            WindowLayoutSources.Add(new SourceSettingsFileViewModel(characterFile, BuildCharacterDisplayName(characterFile)));
        }

        foreach (var accountFile in SelectedSourceProfile.Profile.AccountFiles)
        {
            var label = _accountLabelStore.GetLabel(accountFile.AccountId);
            AccountOverviewItems.Add(new AccountOverviewItemViewModel(accountFile, label));
            var sourceViewModel = new SourceSettingsFileViewModel(
                accountFile,
                BuildAccountDisplayName(accountFile, label));
            NeocomSources.Add(sourceViewModel);

            var target = new SelectableSyncTargetViewModel(
                new SyncTarget(
                    $"account:{accountFile.AccountId}",
                    BuildAccountDisplayName(accountFile, label),
                    SyncOption.NeocomColors,
                    accountFile),
                label,
                UpdateAccountLabel)
            {
                IsSelected = true,
            };

            TargetProfiles.Add(target);
            AccountTargets.Add(target);
        }

        SelectedWindowLayoutSource = WindowLayoutSources.FirstOrDefault();
        SelectedNeocomSource = NeocomSources.FirstOrDefault();
        RebuildCharacterTargets();
        _isRebuildingTargets = false;
    }

    private void RebuildCharacterTargets()
    {
        var previousSelections = CharacterTargets.ToDictionary(
            target => target.Target.Id,
            target => target.IsSelected,
            StringComparer.OrdinalIgnoreCase);

        foreach (var target in CharacterTargets.ToArray())
        {
            TargetProfiles.Remove(target);
        }

        CharacterTargets.Clear();

        if (SelectedSourceProfile is null)
        {
            return;
        }

        var excludedPath = SelectedWindowLayoutSource?.File.FullPath;
        foreach (var characterFile in SelectedSourceProfile.Profile.CharacterFiles)
        {
            if (!string.IsNullOrWhiteSpace(excludedPath) &&
                string.Equals(characterFile.FullPath, excludedPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = new SelectableSyncTargetViewModel(
                new SyncTarget(
                    $"char:{characterFile.CharacterId}",
                    BuildCharacterDisplayName(characterFile),
                    SyncOption.WindowLayout,
                    characterFile))
            {
                IsSelected = previousSelections.TryGetValue($"char:{characterFile.CharacterId}", out var wasSelected)
                    ? wasSelected
                    : true,
            };

            CharacterTargets.Add(target);
            TargetProfiles.Add(target);
        }
    }

    private void UpdateAccountLabel(string accountId, string? label)
    {
        _accountLabelStore.SetLabel(accountId, label);
        RefreshAccountPresentation(accountId);
    }

    private void RefreshAccountPresentation(string accountId)
    {
        var label = _accountLabelStore.GetLabel(accountId);

        foreach (var target in TargetProfiles.Where(target =>
                     target.Target.Option == SyncOption.NeocomColors &&
                     target.Target.TargetFile is AccountSettingsFile accountFile &&
                     string.Equals(accountFile.AccountId, accountId, StringComparison.OrdinalIgnoreCase)))
        {
            target.SetPresentation(label);
        }

        foreach (var source in NeocomSources.Where(source =>
                     source.File is AccountSettingsFile accountFile &&
                     string.Equals(accountFile.AccountId, accountId, StringComparison.OrdinalIgnoreCase)))
        {
            source.DisplayName = BuildAccountDisplayName((AccountSettingsFile)source.File, label);
        }

        foreach (var account in AccountOverviewItems.Where(account => string.Equals(account.AccountId, accountId, StringComparison.OrdinalIgnoreCase)))
        {
            account.SetSavedLabel(label);
        }
    }

    private string BuildCharacterDisplayName(CharacterSettingsFile characterFile)
    {
        var name = _characterNameStore.GetName(characterFile.CharacterId);
        return string.IsNullOrWhiteSpace(name)
            ? $"Character {characterFile.CharacterId} ({characterFile.FileName})"
            : $"{name} ({characterFile.CharacterId})";
    }

    private string BuildAccountDisplayName(AccountSettingsFile accountFile, string? label)
    {
        var labelPrefix = string.IsNullOrWhiteSpace(label) ? string.Empty : $"{label} - ";
        return $"{labelPrefix}Account {accountFile.AccountId}";
    }

    private async Task ResolveCharacterNamesAsync()
    {
        if (SelectedSourceProfile is null)
        {
            return;
        }

        var ids = SelectedSourceProfile.Profile.CharacterFiles
            .Select(file => file.CharacterId)
            .Where(id => !_characterNameStore.HasFreshName(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (ids.Length == 0)
        {
            RefreshCharacterPresentation();
            return;
        }

        await _characterNameStore.ResolveNamesAsync(ids);
        RefreshCharacterPresentation();
    }

    private void RefreshCharacterPresentation()
    {
        foreach (var target in TargetProfiles.Where(target => target.Target.TargetFile is CharacterSettingsFile))
        {
            target.SetDisplayName(BuildCharacterDisplayName((CharacterSettingsFile)target.Target.TargetFile));
        }

        foreach (var source in WindowLayoutSources.Where(source => source.File is CharacterSettingsFile))
        {
            source.DisplayName = BuildCharacterDisplayName((CharacterSettingsFile)source.File);
        }

        foreach (var target in TargetProfiles.Where(target => target.Target.TargetFile is AccountSettingsFile accountFile))
        {
            var accountFile = (AccountSettingsFile)target.Target.TargetFile;
            var label = _accountLabelStore.GetLabel(accountFile.AccountId);
            target.SetPresentation(label);
        }

        foreach (var source in NeocomSources.Where(source => source.File is AccountSettingsFile accountFile))
        {
            var accountFile = (AccountSettingsFile)source.File;
            var label = _accountLabelStore.GetLabel(accountFile.AccountId);
            source.DisplayName = BuildAccountDisplayName(accountFile, label);
        }
    }

}

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public sealed class ProfileItemViewModel
{
    public ProfileItemViewModel(ProfileFolder profile)
    {
        Profile = profile;
    }

    public ProfileFolder Profile { get; }

    public string DisplayName => $"{Profile.ServerName} / {Profile.Name}";
}

public sealed class SelectableSyncTargetViewModel : ObservableObject
{
    private bool _isSelected;
    private string _displayName;
    private string _labelText;
    private readonly Action<string, string?>? _labelChanged;

    public SelectableSyncTargetViewModel(
        SyncTarget target,
        string? labelText = null,
        Action<string, string?>? labelChanged = null)
    {
        Target = target;
        _displayName = target.DisplayName;
        _labelText = labelText ?? string.Empty;
        _labelChanged = labelChanged;
    }

    public SyncTarget Target { get; }

    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    public bool CanEditLabel => Target.TargetFile is AccountSettingsFile;

    public string LabelText
    {
        get => _labelText;
        set
        {
            if (SetProperty(ref _labelText, value))
            {
                _labelChanged?.Invoke(((AccountSettingsFile)Target.TargetFile).AccountId, value);
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void SetPresentation(string? label)
    {
        _labelText = label ?? string.Empty;
        OnPropertyChanged(nameof(LabelText));

        if (Target.TargetFile is AccountSettingsFile accountFile)
        {
            var labelPrefix = string.IsNullOrWhiteSpace(label) ? string.Empty : $"{label} - ";
            DisplayName = $"{labelPrefix}Account {accountFile.AccountId}";
        }
    }

    public void SetDisplayName(string value)
    {
        DisplayName = value;
    }
}

public sealed class AccountOverviewItemViewModel : ObservableObject
{
    private string _savedLabel;
    private string _draftLabel;

    public AccountOverviewItemViewModel(AccountSettingsFile accountFile, string? savedLabel)
    {
        AccountFile = accountFile;
        _savedLabel = savedLabel ?? string.Empty;
        _draftLabel = _savedLabel;
    }

    public AccountSettingsFile AccountFile { get; }

    public string AccountId => AccountFile.AccountId;

    public string DisplayName => string.IsNullOrWhiteSpace(_savedLabel)
        ? $"Account {AccountId}"
        : $"{_savedLabel} - Account {AccountId}";

    public string LastModifiedText => $"Last modified: {AccountFile.LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";

    public string DraftLabel
    {
        get => _draftLabel;
        set => SetProperty(ref _draftLabel, value);
    }

    public void CommitLabel()
    {
        _savedLabel = (_draftLabel ?? string.Empty).Trim();
        OnPropertyChanged(nameof(DisplayName));
    }

    public void SetSavedLabel(string? label)
    {
        _savedLabel = label ?? string.Empty;
        _draftLabel = _savedLabel;
        OnPropertyChanged(nameof(DraftLabel));
        OnPropertyChanged(nameof(DisplayName));
    }
}

public sealed class SelectableSyncOptionViewModel : ObservableObject
{
    private bool _isSelected;

    public SelectableSyncOptionViewModel(SyncOption option, string displayName, bool isSelected = false)
    {
        Option = option;
        DisplayName = displayName;
        _isSelected = isSelected;
    }

    public SyncOption Option { get; }

    public string DisplayName { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class SourceSettingsFileViewModel : ObservableObject
{
    private string _displayName;

    public SourceSettingsFileViewModel(SettingsFile file, string displayName)
    {
        File = file;
        _displayName = displayName;
    }

    public SettingsFile File { get; }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }
}

public sealed class AccountLabelStore
{
    private readonly string _path;
    private readonly Dictionary<string, string> _labels;

    public AccountLabelStore(string path)
    {
        _path = path;
        _labels = Load(path);
    }

    public string? GetLabel(string accountId)
    {
        return _labels.TryGetValue(accountId, out var label) ? label : null;
    }

    public void SetLabel(string accountId, string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            _labels.Remove(accountId);
        }
        else
        {
            _labels[accountId] = label.Trim();
        }

        Save();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(_labels, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }

    private static Dictionary<string, string> Load(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}

public sealed class CharacterNameStore
{
    private readonly string _path;
    private readonly Dictionary<string, CharacterNameEntry> _entries;
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    public CharacterNameStore(string path)
    {
        _path = path;
        _entries = Load(path);
    }

    public string? GetName(string characterId)
    {
        return _entries.TryGetValue(characterId, out var entry) ? entry.Name : null;
    }

    public bool HasFreshName(string characterId)
    {
        return _entries.TryGetValue(characterId, out var entry) &&
               entry.ExpiresAtUtc > DateTimeOffset.UtcNow &&
               !string.IsNullOrWhiteSpace(entry.Name);
    }

    public async Task ResolveNamesAsync(IEnumerable<string> characterIds)
    {
        foreach (var characterId in characterIds)
        {
            if (!long.TryParse(characterId, out var parsedId))
            {
                continue;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://esi.evetech.net/latest/characters/{parsedId}/?datasource=tranquility");
                request.Headers.Add("User-Agent", "EVEProfileSync/1.0");
                var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var payload = await response.Content.ReadFromJsonAsync<CharacterNameResponse>().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(payload?.Name))
                {
                    _entries[characterId] = new CharacterNameEntry(payload.Name, DateTimeOffset.UtcNow.AddDays(7));
                }
            }
            catch
            {
                // Keep cached or numeric fallback labels if ESI is unavailable.
            }
        }

        Save();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }

    private static Dictionary<string, CharacterNameEntry> Load(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, CharacterNameEntry>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, CharacterNameEntry>>(File.ReadAllText(path))
                ?? new Dictionary<string, CharacterNameEntry>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, CharacterNameEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed record CharacterNameResponse(string Name);
    private sealed record CharacterNameEntry(string Name, DateTimeOffset ExpiresAtUtc);
}
