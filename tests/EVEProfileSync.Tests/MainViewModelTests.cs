using EVEProfileSync.App;
using EVEProfileSync.Core;
using Xunit;

namespace EVEProfileSync.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void SelectingProfile_DefaultsSourcesAndTargetsToOptIn()
    {
        var profile = CreateProfile(characterCount: 2, accountCount: 3);
        var viewModel = CreateViewModel();
        viewModel.Profiles.Add(new ProfileItemViewModel(profile));

        viewModel.SelectedSourceProfile = viewModel.Profiles.Single();

        Assert.Null(viewModel.SelectedWindowLayoutSource);
        Assert.Null(viewModel.SelectedNeocomSource);
        Assert.Equal(3, viewModel.AccountTargets.Count);
        Assert.All(viewModel.AccountTargets, target => Assert.False(target.IsSelected));
        Assert.Equal(2, viewModel.CharacterTargets.Count);
        Assert.All(viewModel.CharacterTargets, target => Assert.False(target.IsSelected));
    }

    [Fact]
    public void ChoosingLayoutSource_RebuildsTargetsWithoutAutoSelectingLayoutSync()
    {
        var profile = CreateProfile(characterCount: 3, accountCount: 1);
        var viewModel = CreateViewModel();
        viewModel.Profiles.Add(new ProfileItemViewModel(profile));
        viewModel.SelectedSourceProfile = viewModel.Profiles.Single();

        var previouslySelectedTarget = viewModel.CharacterTargets.First();
        previouslySelectedTarget.IsSelected = true;
        viewModel.SelectedWindowLayoutSource = viewModel.WindowLayoutSources.Last();

        Assert.DoesNotContain(viewModel.CharacterTargets, target => target.Target.TargetFile.FullPath == viewModel.SelectedWindowLayoutSource!.File.FullPath);
        Assert.Contains(viewModel.CharacterTargets, target => target.Target.Id == previouslySelectedTarget.Target.Id && target.IsSelected);
        Assert.Contains(viewModel.CharacterTargets, target => target.Target.Id != previouslySelectedTarget.Target.Id && !target.IsSelected);
    }

    [Fact]
    public void ChoosingAccountSource_RebuildsTargetsWithoutAutoSelectingAccountSync()
    {
        var profile = CreateProfile(characterCount: 1, accountCount: 3);
        var viewModel = CreateViewModel();
        viewModel.Profiles.Add(new ProfileItemViewModel(profile));
        viewModel.SelectedSourceProfile = viewModel.Profiles.Single();

        var previouslySelectedTarget = viewModel.AccountTargets.First();
        previouslySelectedTarget.IsSelected = true;
        viewModel.SelectedNeocomSource = viewModel.NeocomSources.Last();

        Assert.DoesNotContain(viewModel.AccountTargets, target => target.Target.TargetFile.FullPath == viewModel.SelectedNeocomSource!.File.FullPath);
        Assert.Contains(viewModel.AccountTargets, target => target.Target.Id == previouslySelectedTarget.Target.Id && target.IsSelected);
        Assert.Contains(viewModel.AccountTargets, target => target.Target.Id != previouslySelectedTarget.Target.Id && !target.IsSelected);
    }

    [Fact]
    public async Task AccountUiSync_RequiresExplicitSourceAccount()
    {
        var profile = CreateProfile(characterCount: 1, accountCount: 2);
        var viewModel = CreateViewModel();
        viewModel.Profiles.Add(new ProfileItemViewModel(profile));
        viewModel.SelectedSourceProfile = viewModel.Profiles.Single();
        viewModel.AccountTargets.First().IsSelected = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.SyncNeocomAsync());
    }

    [Fact]
    public async Task LayoutSync_RequiresExplicitSourceCharacter()
    {
        var profile = CreateProfile(characterCount: 2, accountCount: 1);
        var viewModel = CreateViewModel();
        viewModel.Profiles.Add(new ProfileItemViewModel(profile));
        viewModel.SelectedSourceProfile = viewModel.Profiles.Single();
        viewModel.CharacterTargets.First().IsSelected = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.SyncLayoutAsync());
    }

    private static MainViewModel CreateViewModel()
    {
        var appDataRoot = TestFileSystem.CreateTempDirectory();
        return new MainViewModel(
            new FakeDiscoveryService(),
            new FakeMappingService(),
            new FakeBackupService(),
            new FakeSyncExecutor(),
            new FakeProcessGuardService(),
            appDataRoot,
            TestFileSystem.CreateTempDirectory());
    }

    private static ProfileFolder CreateProfile(int characterCount, int accountCount)
    {
        var profileRoot = TestFileSystem.CreateTempDirectory();
        var characters = Enumerable.Range(1, characterCount)
            .Select(index =>
            {
                var path = Path.Combine(profileRoot, $"core_char_{1000 + index}.dat");
                File.WriteAllText(path, $"character {index}");
                return new CharacterSettingsFile((1000 + index).ToString(), Path.GetFileName(path), path);
            })
            .ToArray();

        var accounts = Enumerable.Range(1, accountCount)
            .Select(index =>
            {
                var path = Path.Combine(profileRoot, $"core_user_{9000 + index}.dat");
                File.WriteAllText(path, $"account {index}");
                return new AccountSettingsFile((9000 + index).ToString(), Path.GetFileName(path), path);
            })
            .ToArray();

        return new ProfileFolder("c_tranquility", "settings_Default", profileRoot, characters, accounts);
    }

    private sealed class FakeDiscoveryService : ISettingsDiscoveryService
    {
        public SettingsRoot Discover(string? manualRootPath = null) => new(manualRootPath ?? string.Empty, [], null);
    }

    private sealed class FakeMappingService : IArtifactMappingService
    {
        public ArtifactManifestRoot LoadManifest() => new();

        public SyncPlan BuildPlan(
            ProfileFolder sourceProfile,
            IReadOnlyList<SyncTarget> targets,
            IReadOnlyList<SyncOption> selectedOptions,
            IReadOnlyDictionary<SyncOption, SettingsFile> sourceSelections,
            string? overviewFolderPath) =>
            new(sourceProfile, targets, selectedOptions, new Dictionary<SyncOption, string>(), [], string.Empty, false);
    }

    private sealed class FakeBackupService : IBackupService
    {
        public Task<BackupRecord> CreateBackupAsync(SyncPlan plan, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BackupRecord("test", string.Empty, DateTimeOffset.UtcNow, string.Empty, [], []));

        public Task<string> ExportProfileAsync(ProfileFolder profile, string exportDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task<RestorePreview> LoadRestorePreviewAsync(string backupFilePath, ProfileFolder targetProfile, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RestorePreview(backupFilePath, new ExportBackupRecord("test", DateTimeOffset.UtcNow, null, []), []));

        public Task RestoreAsync(RestorePlan plan, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RestoreExportAsync(RestorePreview preview, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSyncExecutor : ISyncExecutor
    {
        public Task<SyncResult> ExecuteAsync(SyncPlan plan, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SyncResult(
                plan,
                new BackupRecord("test", string.Empty, DateTimeOffset.UtcNow, string.Empty, [], []),
                0,
                "Sync completed."));
    }

    private sealed class FakeProcessGuardService : IProcessGuardService
    {
        public bool IsEveRunning() => false;
    }
}
