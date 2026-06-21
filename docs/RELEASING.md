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

- `EVEProfileSync-Setup-2.0.0.exe`
- `EVEProfileSync-Setup.exe` for the stable latest-release download link used in the README

## Versioning

- Tag releases using `v<major>.<minor>.<patch>`
- Example: `v2.0.0`
- Use semantic versioning:
  - `patch` for fixes and packaging-only corrections
  - `minor` for backward-compatible features and UI improvements
  - `major` for breaking workflow or compatibility changes

## What the GitHub workflow does

On pushes and pull requests, GitHub Actions:

1. Restores dependencies
2. Builds the solution in `Release`
3. Runs the test suite

On a version tag like `v2.0.0`, GitHub Actions additionally:

4. Publishes the WPF app for `win-x64`
5. Optionally signs the published binaries when a code-signing certificate is configured
6. Builds an Inno Setup installer from the publish output
7. Optionally signs the installer when a code-signing certificate is configured
8. Zips the publish output
9. Uploads both artifacts as workflow artifacts
10. Uploads both artifacts to the GitHub release page with generated release notes

Generated release notes are categorized through `.github/release.yml`, so issue and pull request labels should be kept reasonably accurate.

## Optional code signing

To sign release builds in GitHub Actions, add these repository secrets:

- `CODE_SIGN_CERT_BASE64`
  Base64-encoded contents of your `.pfx` code-signing certificate
- `CODE_SIGN_CERT_PASSWORD`
  Password for the `.pfx`

The workflow will automatically:

- sign all `.exe` and `.dll` files in `artifacts\publish\win-x64`
- sign both `EVEProfileSync-Setup-<version>.exe` and `EVEProfileSync-Setup.exe`
- timestamp signatures with `http://timestamp.digicert.com`

Notes:

- A valid signature improves installer trust and shows your publisher name.
- Microsoft Defender SmartScreen still uses reputation, so a standard certificate may continue to warn until reputation builds.
- An EV code-signing certificate is the faster path if your goal is to reduce SmartScreen warnings for a new app.

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
iscc .\installer\EVEProfileSync.iss /DMyAppVersion=2.0.0
```

5. Smoke-test both the published `EVEProfileSync.App.exe` and the generated installer.
6. Commit the release-ready changes.
7. Create and push a tag:

```powershell
git tag v2.0.0
git push origin v2.0.0
```

8. Confirm the generated GitHub release contains `EVEProfileSync-Setup.exe`.
9. Confirm the README installer link resolves to the new latest release asset.

## Repo hygiene for public release

- Keep the root-level local launcher bundle out of git; distribute builds through GitHub Releases instead.
- Keep screenshots sanitized before publishing docs or release notes.
- Update `README.md` whenever the UI layout or user workflow changes in a visible way.
- Keep the installer script in sync with app name, icon, and expected publish folder layout.
