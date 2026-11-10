# EPUB Merger

[中文文档](README.md)

A simple Windows desktop EPUB merger with two independent GUIs: the original WPF application and an unpackaged WinUI 3 application. Both use .NET 10; WinUI uses Windows App SDK 2.6.5. It combines multiple EPUB files into a single EPUB 3 file in the specified order, while keeping each source book as a top-level item in the merged contents. The application supports drag-and-drop ordering, custom titles and output paths, cover preview, bilingual UI, and system, light, and dark themes.

## Screenshots

These screenshots show WinUI; WPF retains its original interface.

### Light theme (empty state)

![WinUI application in the light theme](readmeRef/winui-light-en.png)

### Dark theme (empty state)

![WinUI application in the dark theme](readmeRef/winui-dark.png)

### Application in use

![WinUI application with EPUB files and a cover loaded](readmeRef/winui-in-use.png)

## Features

- Add multiple EPUB files and reorder them by drag and drop or buttons
- Remove selected files or clear the list
- Support EPUB 2 and EPUB 3 input files
- Keep each source book as a top-level item in the merged contents
- Customize the output file path and merged title
- Select or drop a cover image and preview it
- Extract and export a cover image from a source EPUB
- Import EPUB files from folders, including recursive subfolder scanning
- Sort inputs naturally, by name, or keep the original order
- Restore recent tasks
- Cancel a merge while it is running
- Open the output file or its containing folder after a successful merge
- Support JPG, JPEG, PNG, GIF, WEBP, and SVG covers
- Switch between system, light, and dark themes
- Switch the interface between Chinese and English at runtime
- Display localized merge and cover-reading errors while recording detailed logs for troubleshooting
- Show merge progress and synchronize it with the taskbar progress indicator
- Automatically generate the EPUB 3 container, navigation page, and table of contents
- Handle duplicate resource names to prevent files from being overwritten

## Usage

1. Start the application.
2. Click **Add Files**, or drag EPUB files into the list on the left.
3. Select files and use **Move Up** or **Move Down** to adjust the merge order.
4. Set the output file, merged title, and optional cover image on the right.
5. Click **Start Merge**.
6. Wait for the status bar to report completion, then find the generated EPUB file at the configured output location.

The output file must use the `.epub` extension and must not overwrite an input file.

## CLI mode

The published package contains a standalone CLI executable that does not create a window. WPF, WinUI and CLI use separate `wpf`, `winui` and `cli` subdirectories to keep their runtime dependencies isolated. Run the following commands from the `cli` directory:

```powershell
EpubMerge.Cli.exe -i .\vol*.epub -o .\merged.epub --title "Collection"
EpubMerge.Cli.exe -d .\books -r -o .\merged.epub --sort natural
EpubMerge.Cli.exe -i .\one.epub .\two.epub -o .\merged.epub --cover-from-index 1
```

Common options:

- `-i`/`--input`: input EPUB files, wildcards, or a list of paths
- `-d`/`--directory`: scan a directory; use `-r`/`--recursive` to scan subdirectories
- `-o`/`--output`: output EPUB path (required)
- `--cover`: specify an external cover image, or use `--cover-from-index N` to select an embedded cover from the Nth book
- `--sort natural|name|none`: natural sort, name sort, or preserve input order
- `--progress bar|plain|jsonl`: interactive progress bar, stable line records, or JSONL progress events; redirected output automatically falls back to line records by default
- `-q`/`--quiet`, `-v`/`--verbose`: control log output
- `-h`/`--help`, `--version`: display help or version information

CLI exit codes are `0` (success), `1` (argument or input validation failure), and `2` (runtime error or cancellation).
`--quiet` suppresses progress and success output while keeping errors on stderr. `plain` emits line-oriented records such as
`PROGRESS phase=merging completed=1 total=3`; `jsonl` emits one parseable JSON object per line.

## Requirements

- Windows 11, x64 or ARM64 (x64 verified locally; other operating systems must meet both .NET 10 and Windows App SDK support requirements)
- Self-contained packages bundle the .NET / Windows Desktop / Windows App SDK runtimes required by each host
- Framework-dependent WPF requires .NET 10 Desktop Runtime for the matching architecture
- Framework-dependent WinUI requires .NET 10 Runtime and Windows App Runtime 2.6.5 for the matching architecture
- Framework-dependent CLI requires .NET 10 Runtime for the matching architecture
- WinUI cover previews use system image decoders; unsupported images produce an explicit preview error without modifying the source

The GUIs do not import or synchronize each other's data. WPF retains its original behavior: history in `EpubMerge.Gui\history.json` beneath the application directory, logs in the application directory, and theme settings in `HKCU\Software\EpubMerge.Gui`. WinUI retains history, logs and cache in `%LOCALAPPDATA%\EpubMerge`, with an independent theme key at `HKCU\Software\EpubMerge.Gui.WinUI`; it does not automatically import WPF history.

## Build

Run the following commands from the repository root:

```powershell
dotnet restore EpubMerge.slnx -p:Platform=x64
dotnet build EpubMerge.slnx -p:Platform=x64
dotnet test EpubMerge.slnx -p:Platform=x64
```

Run WPF:

```powershell
dotnet run --project src\EpubMerge.Gui\EpubMerge.Gui.csproj
```

Run WinUI:

```powershell
dotnet run --project src\EpubMerge.Gui.WinUI\EpubMerge.Gui.WinUI.csproj -p:Platform=x64
```

Run the CLI project:

```powershell
dotnet run --project src\EpubMerge.Cli\EpubMerge.Cli.csproj -- --help
```

## GitHub Actions release

The repository includes a GitHub Actions workflow. Pushing to `master` or opening a pull request automatically restores dependencies, builds the project, and runs the tests.

To publish a release, create and push a tag beginning with `V`, for example:

```powershell
git tag V1.0.0
git push origin V1.0.0
```

Before pushing the tag, update `RELEASE_NOTES.md` in the repository root with the user-visible changes for that version. The workflow writes this file to the GitHub Release description and appends GitHub-generated commit notes.

The workflow builds both GUIs and runs all tests, then creates four archives on a Windows runner: self-contained and framework-dependent packages for both `win-x64` and `win-arm64`. Each archive contains:

```text
wpf\EpubMerge.Gui.exe
winui\EpubMerge.Gui.WinUI.exe
cli\EpubMerge.Cli.exe
```

Dependencies are isolated across the three directories. Distribute the entire `winui` directory: WinUI is not a single-file executable. WPF and CLI retain single-file managed assembly bundling; keep any accompanying native dependencies. Self-contained packages include the runtimes required by each host.

Development requires the .NET 10 SDK, Visual Studio WinUI tools and Windows SDK 26100. `global.json` excludes preview SDKs. Local ARM tools are not required. Native AOT and Store MSIX publishing are deferred to a later GitHub Actions phase.

## Project structure

```text
src/
  EpubMerge.Core.Pure/  EPUB validation and merge logic
  EpubMerge.Cli/        Standalone command-line application
  EpubMerge.Gui/        Original WPF GUI with independent ViewModels/resources
  EpubMerge.Gui.WinUI/  Unpackaged WinUI 3 graphical interface
  EpubMerge.Presentation/ WinUI ViewModels, localization and storage
tests/
  EpubMerge.Core.Tests/ Core logic tests
  EpubMerge.Cli.Tests/   CLI behavior and output tests
  EpubMerge.Gui.Tests/  Original WPF ViewModel tests
  EpubMerge.Presentation.Tests/ WinUI presentation tests without GUI framework loading
readmeRef/              README screenshots
```

## Notes

- The merged result is an EPUB 3 container with regenerated navigation and table-of-contents information.
- Encrypted or font-obfuscated EPUB files containing `META-INF/encryption.xml` are not currently supported.
- Source files are read only; the application does not modify the original EPUB files.

## License

This project is licensed under the terms in [LICENSE](LICENSE).