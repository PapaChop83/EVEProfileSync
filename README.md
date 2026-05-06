# EVEProfileSync

EVEProfileSync is a lightweight Windows desktop app for syncing EVE Online UI layout and NEOCOM color settings between local profiles, characters, and account-scoped settings files.

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
- Separates `UI Layout` and `NEOCOM Colors` so the scope of each action is clear
- Lets you label local account IDs and inspect the last modified timestamp for each account-scoped file
- Exports a portable backup archive to the app folder
- Restores from a selected backup archive after showing which files will be overwritten

## Sync model

- `UI Layout`
  Copies validated `core_char_*.dat` content from one source character to checked target characters in the selected profile.
- `NEOCOM Colors`
  Copies validated `core_user_*.dat` content from one source account to checked target accounts in the selected profile.
- `Export / Restore`
  Creates or restores a portable `.eveprofilesyncbackup` archive for the current source profile.

## Using the app

1. Open the app and confirm the settings root points to `%LOCALAPPDATA%\CCP\EVE` or one of its server folders.
2. Choose the source `settings_*` profile.
3. In `Account Overview`, optionally label local account IDs and use `Refresh Last Modified` after making an in-game account-scoped UI change - by using the last modified time you can figure out which account is which.
4. In `UI Layout`, choose a source character and the target characters to receive that layout.
5. In `NEOCOM Colors`, choose a source account and the target accounts to receive those colors.
6. Use `Export` to create a portable backup archive before making broader changes, or `Restore` to browse to a backup file and preview overwrites.

## Notes

- This is a Windows-only app.
- Character names can be resolved from public ESI, but account IDs remain local-only and should be labeled manually.

## Hot It Works
The main sync behavior is:

- UI layout: copies core_char_*.dat from a chosen source character to selected target characters.
- NEOCOM colors: copies core_user_*.dat from a chosen source account to selected target accounts.
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
