# Contributing to EVEProfileSync

Thanks for helping improve EVEProfileSync.

## Before you start

- Check existing issues before opening a new one.
- For UI bugs, include screenshots when possible.
- For sync bugs, describe exactly which profile, character-scoped setting, or account-scoped setting was involved.

## Development setup

Requirements:

- Windows
- .NET 8 SDK
- Inno Setup 6 if you want to build the installer locally

Local workflow:

```powershell
dotnet restore EVEProfileSync.sln
dotnet build EVEProfileSync.sln
dotnet test EVEProfileSync.sln
dotnet run --project .\src\EVEProfileSync.App\EVEProfileSync.App.csproj
```

## Commit style

Use clear, scoped commit messages:

- `feat:` new user-facing functionality
- `fix:` bug fixes or regressions
- `docs:` README, release docs, or other documentation
- `build:` workflow, packaging, installer, or CI changes
- `test:` automated test changes only
- `refactor:` internal cleanup with no intended behavior change

Examples:

- `feat: add account overview panel`
- `fix: improve combo box contrast in dark theme`
- `build: publish installer on release tags`

## Pull requests

Please keep pull requests focused and include:

1. A short summary of the change.
2. Why the change was needed.
3. Screenshots for visible UI changes.
4. Any manual validation steps you used.

## Release expectations

- Public screenshots should use sanitized or made-up account and character data.
- Releases should continue shipping both:
  - an installer
  - a portable zip

See [docs/RELEASING.md](docs/RELEASING.md) for the current release process.
