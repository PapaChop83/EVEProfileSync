using EVEProfileSync.Core;
using Xunit;

namespace EVEProfileSync.Tests;

public sealed class CoreWorkflowTests
{
    private readonly string _manifestPath = Path.Combine(AppContext.BaseDirectory, "artifact-map.json");

    [Fact]
    public void Discovery_FindsProfilesAndDataFiles()
    {
        var fixtureRoot = TestFileSystem.CopyFixtureTree("CCP");
        var eveRoot = Path.Combine(fixtureRoot, "CCP", "EVE");

        var service = new SettingsDiscoveryService();
        var result = service.Discover(eveRoot);

        Assert.Equal(eveRoot, result.RootPath);
        Assert.Single(result.Servers);
        Assert.Equal(2, result.Servers.Single().Profiles.Count);
        Assert.Single(result.Servers.Single().Profiles.Single(profile => profile.Name == "settings_Default").CharacterFiles);
        Assert.Single(result.Servers.Single().Profiles.Single(profile => profile.Name == "settings_Alt").AccountFiles);
    }

    [Fact]
    public void Discovery_IgnoresMalformedSettingsFiles()
    {
        var root = TestFileSystem.CreateTempDirectory();
        var profileRoot = Path.Combine(root, "CCP", "EVE", "c_tranquility", "settings_Default");
        Directory.CreateDirectory(profileRoot);
        File.WriteAllText(Path.Combine(profileRoot, "core_char_1001.dat"), "ok");
        File.WriteAllText(Path.Combine(profileRoot, "core_char_('char', None, 'dat').dat"), "bad");
        File.WriteAllText(Path.Combine(profileRoot, "core_user_9001.dat"), "ok");
        File.WriteAllText(Path.Combine(profileRoot, "core_user_bad.dat"), "bad");

        var service = new SettingsDiscoveryService();
        var result = service.Discover(Path.Combine(root, "CCP", "EVE"));
        var profile = result.Servers.Single().Profiles.Single();

        Assert.Single(profile.CharacterFiles);
        Assert.Equal("1001", profile.CharacterFiles.Single().CharacterId);
        Assert.Single(profile.AccountFiles);
        Assert.Equal("9001", profile.AccountFiles.Single().AccountId);
    }

    [Fact]
    public void Planner_BuildsWindowAndNeocomArtifacts()
    {
        var fixtureRoot = TestFileSystem.CopyFixtureTree("CCP");
        var eveRoot = Path.Combine(fixtureRoot, "CCP", "EVE");
        var appDataRoot = TestFileSystem.CreateTempDirectory();
        var discovery = new SettingsDiscoveryService();
        var settingsRoot = discovery.Discover(eveRoot);
        var source = settingsRoot.Servers.Single().Profiles.Single(profile => profile.Name == "settings_Default");
        var target = settingsRoot.Servers.Single().Profiles.Single(profile => profile.Name == "settings_Alt");
        var targets = new SyncTarget[]
        {
            new("char:2002", "Character 2002", SyncOption.WindowLayout, target.CharacterFiles.Single()),
            new("account:9002", "Account 9002", SyncOption.NeocomColors, target.AccountFiles.Single()),
        };

        var planner = new ArtifactMappingService(_manifestPath, new OverviewAssetService(), appDataRoot);
        var plan = planner.BuildPlan(
            source,
            targets,
            [SyncOption.WindowLayout, SyncOption.NeocomColors],
            new Dictionary<SyncOption, SettingsFile>(),
            overviewFolderPath: null);

        Assert.Equal(2, plan.Artifacts.Count);
        Assert.Contains(plan.Artifacts, artifact => artifact.Option == SyncOption.WindowLayout && artifact.DestinationPath.EndsWith("core_char_2002.dat"));
        Assert.Contains(plan.Artifacts, artifact => artifact.Option == SyncOption.NeocomColors && artifact.DestinationPath.EndsWith("core_user_9002.dat"));
        Assert.False(plan.RequiresManualOverviewImport);
    }

    [Fact]
    public void Planner_UsesOverviewAssetsForOverviewOption()
    {
        var fixtureRoot = TestFileSystem.CopyFixtureTree("CCP");
        var overviewFixtureRoot = TestFileSystem.CopyFixtureTree("Documents");
        var eveRoot = Path.Combine(fixtureRoot, "CCP", "EVE");
        var overviewRoot = Path.Combine(overviewFixtureRoot, "Documents", "EVE", "Overview");
        var appDataRoot = TestFileSystem.CreateTempDirectory();

        var discovery = new SettingsDiscoveryService();
        var settingsRoot = discovery.Discover(eveRoot);
        var source = settingsRoot.Servers.Single().Profiles.Single(profile => profile.Name == "settings_Default");
        var target = settingsRoot.Servers.Single().Profiles.Single(profile => profile.Name == "settings_Alt");
        var targets = new SyncTarget[]
        {
            new("char:2002", "Character 2002", SyncOption.WindowLayout, target.CharacterFiles.Single()),
        };

        var planner = new ArtifactMappingService(_manifestPath, new OverviewAssetService(), appDataRoot);
        var plan = planner.BuildPlan(source, targets, [SyncOption.OverviewSettings], new Dictionary<SyncOption, SettingsFile>(), overviewRoot);

        Assert.True(plan.RequiresManualOverviewImport);
        Assert.Equal(2, plan.Artifacts.Count);
        Assert.All(plan.Artifacts, artifact =>
        {
            Assert.Equal(ArtifactKind.OverviewExportPackage, artifact.Kind);
            Assert.Contains(Path.Combine("overview-packages", source.Name), artifact.RelativePath);
        });
    }

    [Fact]
    public async Task SyncAndRestore_RoundTripsTargetFiles()
    {
        var fixtureRoot = TestFileSystem.CopyFixtureTree("CCP");
        var eveRoot = Path.Combine(fixtureRoot, "CCP", "EVE");
        var appDataRoot = TestFileSystem.CreateTempDirectory();
        var discovery = new SettingsDiscoveryService();
        var settingsRoot = discovery.Discover(eveRoot);
        var source = settingsRoot.Servers.Single().Profiles.Single(profile => profile.Name == "settings_Default");
        var target = settingsRoot.Servers.Single().Profiles.Single(profile => profile.Name == "settings_Alt");
        var planner = new ArtifactMappingService(_manifestPath, new OverviewAssetService(), appDataRoot);
        var backupService = new BackupService(appDataRoot);
        var executor = new SyncExecutor(backupService, new FakeProcessGuardService(isRunning: false));
        var targets = new SyncTarget[]
        {
            new("char:2002", "Character 2002", SyncOption.WindowLayout, target.CharacterFiles.Single()),
        };

        var targetCharFile = target.CharacterFiles.Single().FullPath;
        var originalContent = await File.ReadAllTextAsync(targetCharFile);
        var sourceContent = await File.ReadAllTextAsync(source.CharacterFiles.Single().FullPath);

        var result = await executor.ExecuteAsync(planner.BuildPlan(source, targets, [SyncOption.WindowLayout], new Dictionary<SyncOption, SettingsFile>(), null));
        var syncedContent = await File.ReadAllTextAsync(targetCharFile);

        Assert.Equal(sourceContent, syncedContent);
        Assert.NotEqual(originalContent, syncedContent);

        await backupService.RestoreAsync(new RestorePlan(result.Backup));
        var restoredContent = await File.ReadAllTextAsync(targetCharFile);

        Assert.Equal(originalContent, restoredContent);
    }

    [Fact]
    public async Task ExportAndRestoreArchive_RoundTripsProfileFiles()
    {
        var fixtureRoot = TestFileSystem.CopyFixtureTree("CCP");
        var eveRoot = Path.Combine(fixtureRoot, "CCP", "EVE");
        var exportRoot = TestFileSystem.CreateTempDirectory();
        var discovery = new SettingsDiscoveryService();
        var settingsRoot = discovery.Discover(eveRoot);
        var source = settingsRoot.Servers.Single().Profiles.Single(profile => profile.Name == "settings_Default");
        var backupService = new BackupService(TestFileSystem.CreateTempDirectory());

        var exportPath = await backupService.ExportProfileAsync(source, exportRoot);
        Assert.True(File.Exists(exportPath));

        var originalCharacterPath = source.CharacterFiles.Single().FullPath;
        var originalContent = await File.ReadAllTextAsync(originalCharacterPath);
        await File.WriteAllTextAsync(originalCharacterPath, "changed");

        var preview = await backupService.LoadRestorePreviewAsync(exportPath);
        await backupService.RestoreExportAsync(preview);
        var restoredContent = await File.ReadAllTextAsync(originalCharacterPath);

        Assert.Equal(originalContent, restoredContent);
        Assert.Contains(preview.PathsToRestore, path => path.EndsWith("core_char_1001.dat"));
    }

    [Fact]
    public void MappingHarness_ReportsChangedFilesBetweenSnapshots()
    {
        var beforeRoot = TestFileSystem.CopyFixtureTree("Snapshots\\Before");
        var afterRoot = TestFileSystem.CopyFixtureTree("Snapshots\\After");
        var changedFiles = MappingHarness.CompareSnapshots(beforeRoot, afterRoot);

        Assert.Contains("c_tranquility\\settings_Default\\core_char_1001.dat", changedFiles);
        Assert.Contains("c_tranquility\\settings_Default\\core_user_9001.dat", changedFiles);
        Assert.DoesNotContain("c_tranquility\\settings_Default\\keep.dat", changedFiles);
    }

    [Fact]
    public void Planner_UsesExplicitSourceSelectionForWindowLayout()
    {
        var fixtureRoot = TestFileSystem.CopyFixtureTree("CCP");
        var eveRoot = Path.Combine(fixtureRoot, "CCP", "EVE");
        var appDataRoot = TestFileSystem.CreateTempDirectory();
        var discovery = new SettingsDiscoveryService();
        var settingsRoot = discovery.Discover(eveRoot);
        var source = settingsRoot.Servers.Single().Profiles.Single(profile => profile.Name == "settings_Default");
        var target = settingsRoot.Servers.Single().Profiles.Single(profile => profile.Name == "settings_Alt");
        var selectedSourceFile = source.CharacterFiles.Single();
        var targets = new SyncTarget[]
        {
            new("char:2002", "Character 2002", SyncOption.WindowLayout, target.CharacterFiles.Single()),
        };

        var planner = new ArtifactMappingService(_manifestPath, new OverviewAssetService(), appDataRoot);
        var plan = planner.BuildPlan(
            source,
            targets,
            [SyncOption.WindowLayout],
            new Dictionary<SyncOption, SettingsFile>
            {
                [SyncOption.WindowLayout] = selectedSourceFile,
            },
            null);

        Assert.Single(plan.Artifacts);
        Assert.Equal(selectedSourceFile.FullPath, plan.Artifacts[0].SourcePath);
        Assert.Equal(selectedSourceFile.FileName, plan.SourceSelectionDescriptions[SyncOption.WindowLayout]);
    }

    private sealed class FakeProcessGuardService : IProcessGuardService
    {
        private readonly bool _isRunning;

        public FakeProcessGuardService(bool isRunning)
        {
            _isRunning = isRunning;
        }

        public bool IsEveRunning() => _isRunning;
    }
}
