# EVEProfileSync

EVEProfileSync is a lightweight Windows desktop app for syncing EVE Online UI layout and account-scoped UI settings between local profiles, characters, and account settings files.

## Install

1. Download the latest installer: [EVEProfileSync-Setup.exe](https://github.com/PapaChop83/EVEProfileSync/releases/latest/download/EVEProfileSync-Setup.exe)
2. Run the installer and follow the setup prompts.
3. Launch `EVEProfileSync` from the Start Menu or desktop shortcut.
4. Like it? Feel free to send PLEX to character Nicholai Thomasovich. :-D

![Sanitized EVEProfileSync screenshot](docs/images/eveprofilesync-sanitized.png)

## What it does

- Auto-discovers EVE settings from `%LOCALAPPDATA%\CCP\EVE` or a manually selected folder
- Shows local `settings_*` profiles for each detected server installation
- Resolves character IDs to character names when public ESI lookups are available
- Separates `UI Layout` and `Account UI Settings` so the scope of each action is clear
- Copies full character layout settings from the selected source character to selected target characters
- Lets you label local account IDs and inspect the last modified timestamp for each account-scoped file
- Exports a portable backup archive to the app folder
- Restores from a selected backup archive after showing which files will be overwritten

## Sync model

- `UI Layout`
  Copies validated character-scoped UI layout content from one source character to checked target characters in the selected profile.
- `Account UI Settings`
  Copies validated `core_user_*.dat` content from one source account to checked target accounts in the selected profile. This can include NEOCOM appearance, icon display/order, UI transparency, and other account-scoped UI preferences.
- `Export / Restore`
  Creates or restores a portable `.eveprofilesyncbackup` archive for the current source profile.

## Security audit changes

Following a security audit, implemented following changes:

- Backup exports now store restore paths relative to the selected EVE profile instead of embedding absolute local Windows paths.
- Restore now targets the currently selected profile and rejects archive paths that are absolute, contain parent traversal, or resolve outside that profile.
- Restore only accepts expected EVE settings files such as `core_char_<id>.dat` and `core_user_<id>.dat`.
- Restore validates that every referenced archive entry exists and stays under a safe size limit before overwriting files.
- Legacy backups with absolute paths are accepted only when those paths resolve inside the selected profile.
- The restore confirmation now shows the full list of files that will be overwritten in a scrollable preview.

## Using the app

1. Open the app and confirm the settings root points to `%LOCALAPPDATA%\CCP\EVE` or one of its server folders.
2. Choose the source `settings_*` profile.
3. In `Account Overview`, optionally label local account IDs and use `Refresh Last Modified` after making an in-game account-scoped UI change - by using the last modified time you can figure out which account is which.
4. In `UI Layout`, choose a source character and the target characters to receive that layout.
5. In `Account UI Settings`, choose a source account and the target accounts to receive those account-scoped UI preferences.
6. Use `Export` to create a portable backup archive before making broader changes, or `Restore` to browse to a backup file and preview overwrites.

## Build requirements

- Windows
- .NET 8 SDK

## Local development

```powershell
dotnet restore EVEProfileSync.sln
dotnet build EVEProfileSync.sln
dotnet test EVEProfileSync.sln
dotnet run --project .\src\EVEProfileSync.App\EVEProfileSync.App.csproj
```

## Build a portable publish locally

```powershell
dotnet publish .\src\EVEProfileSync.App\EVEProfileSync.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o .\artifacts\publish\win-x64
```

The publish output is a portable Windows folder that can be zipped and attached to a GitHub release.

## Build the Inno Setup installer locally

1. Publish the app:

```powershell
dotnet publish .\src\EVEProfileSync.App\EVEProfileSync.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o .\artifacts\publish\win-x64
```

2. Compile the installer with Inno Setup 6:

```powershell
iscc .\installer\EVEProfileSync.iss /DMyAppVersion=2.0.0
```

That produces an installer like `artifacts\installer\EVEProfileSync-Setup-2.0.0.exe`.

## Public release workflow

This repo includes a GitHub Actions workflow that:

- restores, builds, and tests on pushes and pull requests
- publishes a portable `win-x64` build on version tags like `v2.0.0`
- builds an Inno Setup installer from that publish output
- uploads both the portable zip and the installer as workflow artifacts and GitHub release assets
- keeps the stable README installer link current through `releases/latest/download/EVEProfileSync-Setup.exe`

See [docs/RELEASING.md](docs/RELEASING.md) for the release checklist and tagging flow.

## Contributing

Contributions are welcome. For setup, commit conventions, and release expectations, see [CONTRIBUTING.md](CONTRIBUTING.md).

## Project layout

- `src/EVEProfileSync.Core`
  Core discovery, artifact mapping, backup export/restore, sync execution, and process-guard logic
- `src/EVEProfileSync.App`
  WPF desktop UI, app icon, account labels, and character-name resolution
- `tests/EVEProfileSync.Tests`
  Fixture-driven tests for discovery, sync planning, backup/export restore, and snapshot comparison
- `docs/images`
  Public documentation screenshots and other sanitized assets

## Notes

- This is a Windows-only app.
- Character names can be resolved from public ESI, but account IDs remain local-only and should be labeled manually.

## How It Works
The main sync behavior is:

- UI layout: copies character-scoped layout from a chosen source character into selected target characters.
- Account UI settings: copies core_user_*.dat from a chosen source account to selected target accounts.
- Overview exports: finds local overview files in Documents\EVE\Overview and prepares copies for manual in-game import.
- Before writing, it checks whether EVE is running and refuses to sync if the client process is open.
- Before overwriting target files, it creates a local backup under the app’s data folder.
- Export/restore uses a local .eveprofilesyncbackup zip-style archive containing the selected profile’s local files.

ESI Interactions: There is no EVE SSO login, no OAuth, no scopes, no access token, and no refresh token handling in this codebase.

The only ESI usage is public character-name lookup:
- First it sends a POST to https://esi.evetech.net/latest/universe/names/?datasource=tranquility with a JSON array of numeric character IDs.
- If that does not resolve everything, it falls back to GET https://esi.evetech.net/latest/characters/{characterId}/?datasource=tranquility.
- It sets User-Agent: EVEProfileSync/1.0.
- Returned character names are cached locally for 7 days in %APPDATA%\EVEProfileSync\character-names.json.

Does Data Leave The User’s System? 
Yes, but only in a narrow way: character IDs are sent to CCP’s public ESI service so the app can display character names instead of just numbers. Account labels and character-name cache files are stored locally under %APPDATA%\EVEProfileSync; exports are written locally under the app’s Exports folder.
