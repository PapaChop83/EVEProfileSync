# Releasing EVEProfileSync

## Release format

EVEProfileSync should ship two public Windows artifacts:

- an Inno Setup installer
- a portable `win-x64` zip

Each tagged release should contain the portable app files:

- `EVEProfileSync.App.exe`
- `EVEProfileSync.App.dll`
- `EVEProfileSync.Core.dll`
- `EVEProfileSync.App.deps.json`
- `EVEProfileSync.App.runtimeconfig.json`
- `artifact-map.json`

And a setup executable such as:

- `EVEProfileSync-Setup-1.0.0.exe`
- `EVEProfileSync-Setup.exe` for the stable latest-release download link used in the README

## Versioning

- Tag releases using `v<major>.<minor>.<patch>`
- Example: `v1.0.0`
- Use semantic versioning:
  - `patch` for fixes and packaging-only corrections
  - `minor` for backward-compatible features and UI improvements
  - `major` for breaking workflow or compatibility changes

## What the GitHub workflow does

On pushes and pull requests, GitHub Actions:

1. Restores dependencies
2. Builds the solution in `Release`
3. Runs the test suite

On a version tag like `v1.0.0`, GitHub Actions additionally:

4. Publishes the WPF app for `win-x64`
5. Builds an Inno Setup installer from the publish output
6. Zips the publish output
7. Uploads both artifacts as workflow artifacts
8. Uploads both artifacts to the GitHub release page with generated release notes

Generated release notes are categorized through `.github/release.yml`, so issue and pull request labels should be kept reasonably accurate.

## Suggested release checklist

1. Verify the app launches from a fresh `Release` publish output.
2. Confirm the README screenshot and docs still match the current UI.
3. Confirm any example account and character data remains sanitized.
4. Run:

```powershell
dotnet build EVEProfileSync.sln -c Release
dotnet test EVEProfileSync.sln -c Release
dotnet publish .\src\EVEProfileSync.App\EVEProfileSync.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o .\artifacts\publish\win-x64
iscc .\installer\EVEProfileSync.iss /DMyAppVersion=1.0.0
```

5. Smoke-test both the published `EVEProfileSync.App.exe` and the generated installer.
6. Commit the release-ready changes.
7. Create and push a tag:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

## Repo hygiene for public release

- Keep the root-level local launcher bundle out of git; distribute builds through GitHub Releases instead.
- Keep screenshots sanitized before publishing docs or release notes.
- Update `README.md` whenever the UI layout or user workflow changes in a visible way.
- Keep the installer script in sync with app name, icon, and expected publish folder layout.
