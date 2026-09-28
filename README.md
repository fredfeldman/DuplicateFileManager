# Duplicate File Manager

Duplicate File Manager is a Windows WPF application for finding exact duplicate files in a selected folder tree. It scans locally and does not upload file contents.

## Features

- Native Windows folder selection with a persistent scan root.
- Exact duplicate detection using file size, a fast first-and-last-chunk fingerprint, and final SHA-256 confirmation.
- Bounded parallel hashing for faster scans.
- Expandable duplicate groups with file paths, sizes, modified dates, and removal selection.
- Cooperative scan cancellation that preserves the previous completed results.
- Naming protection for Whisparr, Radarr, or Sonarr managed files.
- Configurable `config.xml` paths for all three naming-rule providers.
- Explicit confirmation before permanently deleting selected files.

## Requirements

- Windows
- .NET 10 SDK

## Build And Run

From the repository root:

```powershell
dotnet build .\DuplicateFileManager.Wpf\DuplicateFileManager.Wpf.csproj
dotnet run --project .\DuplicateFileManager.Wpf\DuplicateFileManager.Wpf.csproj
```

## Usage

1. Select a naming-protection provider from the sidebar, or choose `No naming protection`.
2. Open **Settings** if Whisparr, Radarr, or Sonarr uses a nonstandard `config.xml` location.
3. Select **Choose folder and scan** and choose the root directory.
4. Review the duplicate groups. Files recognized as managed by the selected provider are not selected for removal automatically.
5. Select unwanted copies and choose **Remove selected**. Deletion is permanent after confirmation.

The selected scan root, naming provider, and provider config paths are saved under `%LOCALAPPDATA%\DuplicateFileManager\settings.json`.

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).
