# ImageCare - Agent Instructions

## Project Overview

ImageCare is a desktop media manager for reviewing, sorting, annotating, and moving large photo collections. It's built with .NET 10 and Avalonia UI 12, targeting Windows x64 and macOS arm64.

**Key Features:**
- Two-panel file manager with shared preview strip for sorting
- Timeline view with folder overview, clustering by time (for folders with up to 5000 files)
- Preview extraction and metadata for JPEG, RAW (CR3, ARW, NEF), and MP4
- GPS location map for geotagged Canon CR3 images
- XMP sidecar ratings/tags with importable tag dictionaries
- Indexed search by properties, rating, tags
- Safe deletion buffer with restoration
- Local MCP server for AI integration
- English/Russian localization

## Tech Stack

- **.NET**: 10.0.401 (see `global.json`), C# with nullable/reference types enabled, implicit usings
- **UI Framework**: Avalonia UI 12.x (cross-platform XAML)
- **Architecture**: MVVM with Prism.Avalonia + DryIoc DI
- **Data**: SQLite for media annotations index, XMP sidecar files for annotations
- **Image Processing**: LibRaw (via LibRawDotNet) for RAW, SkiaSharp, MetadataExtractor
- **Video**: LibMpv via HanumanInstitute.LibMpv
- **Maps**: Mapsui
- **Reactive**: System.Reactive
- **Logging**: Serilog
- **Build**: TreatWarningsAsErrors enabled globally

## Solution Structure

```
ImageCare.sln
├── ImageCare.Core/           # Core domain logic, services, models (cross-platform)
├── ImageCare.UI.Avalonia/    # Avalonia UI app (Windows/macOS entry point)
├── ImageCare.Mcp/            # Local MCP server for AI assistant integration
├── LibRawDotNet/             # RAW image decoding wrapper (LibRaw)
└── Tests/
    ├── ImageCare.Core.Tests/
    ├── ImageCare.UI.Avalonia.Tests/
    ├── ImageCare.Mcp.Tests/
    └── LibRawDotNet.Tests/
```

### Project Dependencies

- `ImageCare.Core` depends on `LibRawDotNet`
- `ImageCare.UI.Avalonia` depends on `ImageCare.Core` and `ImageCare.Mcp`
- `ImageCare.Mcp` depends on `ImageCare.Core`
- Tests reference their respective projects

## Build & Run

### Prerequisites
- .NET SDK 10.0.401+ (or compatible per global.json)
- Windows or macOS

### Build
```bash
dotnet restore ImageCare.sln
dotnet build ImageCare.sln --configuration Release --no-restore
```

### Run (development)
```bash
dotnet run --project ImageCare.UI.Avalonia/ImageCare.UI.Avalonia.csproj
```

### Publish
**macOS arm64:**
```bash
zsh scripts/publish-macos-arm64.sh
# Output: artifacts/macos-arm64/ImageCare.app
```

**Windows x64:**
```powershell
dotnet restore ImageCare.UI.Avalonia/ImageCare.UI.Avalonia.csproj --runtime win-x64 -p:PublishReadyToRun=true
dotnet publish ImageCare.UI.Avalonia/ImageCare.UI.Avalonia.csproj --configuration Release --runtime win-x64 --self-contained true --no-restore -p:PublishReadyToRun=true --output artifacts/windows-x64/publish
```

**Note:** Publishing requires explicit RuntimeIdentifier (win-x64 or osx-arm64). All compiler/analyzer warnings are treated as errors.

## Architecture

### ImageCare.Core
Core business logic, platform-agnostic where possible.

**Key areas:**
- `Domain/` - Models/entities (Media, Folders, Annotations, Configuration, etc.)
- `Domain/Media/Assets/` - Embedded preview images (mp4_media_preview.jpg, unknown_media_preview.jpg/png)
- `Services/` - Business services
  - `MediaAnnotationsService/` - Rating/tags with XMP sidecars
  - `MediaAnnotationsIndex/` - SQLite index for fast search
  - `MediaPreviewService/` - Thumbnail/preview generation
  - `FolderService/`, `FolderClusterizationService/`, `FolderStatisticsService/`
  - `FileSystemService/`, `FileSystemWatcherService/`
  - `ConfigurationService/` - Settings persistence

**Patterns:**
- Services are registered in DI with platform-specific implementations
- XMP annotations stored as sidecar files (not embedded in original files)
- SQLite index for searching media annotations
- LibRaw integration via P/Invoke in LibRawDotNet

### ImageCare.UI.Avalonia
Avalonia UI application using Prism MVVM.

**Structure:**
- `App.axaml.cs` - Prism application setup, DI registration, MCP server startup/shutdown
- `Program.cs` - Entry point, Avalonia configuration (Windows uses AngleEgl/Software rendering)
- `Views/` - XAML views (MainWindow, FoldersView, PreviewPanelView, etc.)
- `ViewModels/` - ViewModels following Prism patterns
- `Services/` - UI-specific services (window services, dialogs, clipboard, drag-drop)
- `Behaviors/`, `Controls/`, `Styles/` - UI helpers
- `Localization/` - i18n (English/Russian)
- `Bridge/`, `Mapping/` - Adapters/mapping

**DI Registration (in App.axaml.cs):**
- Platform-specific types registered based on OS (Windows/macOS)
- Core services registered as singletons (Configuration, Annotations, Preview, etc.)
- ViewModels/Views registered for navigation
- Serilog logging configured to file (`%LocalAppData%/ImageCare/Logs/Errors.log`) and log viewer sink
- MCP server controller registered and started asynchronously after main window opens

**Navigation Regions:**
- `FoldersView`, `MainImageView`, `MainVideoView`, `PreviewPanelView`, `BottomBarView` registered for navigation

### ImageCare.Mcp
Local MCP (Model Context Protocol) server exposing ImageCare functionality to AI assistants.

**Structure:**
- `ImageCareMcpHost.cs` - MCP host setup
- `ImageCareMcpOptions.cs` - Configuration options
- `Media/` - Media-related tools/resources
- `Tags/` - Tag-related operations
- `Tools/` - MCP tool implementations
- `Sessions/` - Session management
- `Security/` - Security/auth handling
- `Services/` - MCP services
- `Ui/` - UI integration

Uses `ModelContextProtocol.AspNetCore 2.3.0`. Started by UI app when enabled.

### LibRawDotNet
Native RAW decoding wrapper using LibRaw.

- P/Invoke to LibRaw
- Uses TurboJpegWrapper for JPEG operations
- Native libraries: `libraw.dll` (Windows) or dylibs in `runtimes/osx-arm64/native/` (macOS)
- `Directory.Build.targets` copies Windows libraw.dll to output for exe/test projects
- LibRawDotNet.csproj has transitive copy logic for macOS dylibs

## Key Conventions

### Code Style
- TreatWarningsAsErrors = true (enforced by Directory.Build.targets)
- Nullable reference types enabled
- Implicit usings enabled
- AOT compatible for Core and LibRawDotNet
- AllowUnsafeBlocks enabled in Core

### File Organization
- XMP sidecar files for annotations (stored alongside media files)
- Preview assets in Core are copied to output
- Windows uses WinExe output type when on Windows
- Tags example file copied to output

### Platform Support
- **Release targets:** Windows x64, macOS arm64 only
- **Runtime detection:** Uses `OperatingSystem.IsWindows()` / `OperatingSystem.IsMacOS()`
- Platform-specific implementations selected in DI
- Native player (libmpv) placed in `player-native/{rid}/` on Windows

### Configuration
- Configuration stored via `JsonConfigurationService`
- Windows config location uses WindowsConfigurationFileSource
- Logs in `%LocalAppData%/ImageCare/Logs/` (Windows) or equivalent platform location

## Testing

Tests are in `Tests/` folder:
- `ImageCare.Core.Tests` - Core logic tests
- `ImageCare.UI.Avalonia.Tests` - UI tests
- `ImageCare.Mcp.Tests` - MCP server tests  
- `LibRawDotNet.Tests` - RAW decoding tests

Run tests with:
```bash
dotnet test ImageCare.sln
```

## Important Files

- `README.md` / `README.ru.md` - User documentation
- `ChangeLog.txt` - Change history
- `tags-example.txt` - Example tag dictionary (325 bird species from Belarus)
- `.editorconfig` - Code style rules
- `ClearBinObjFolders.bat` - Cleanup script

## Development Notes

- The app uses Prism's dialog system (registered as `childWindow`)
- Media annotations are coordinated by `MediaAnnotationsCoordinator`
- Folder clustering marks related shooting sessions with colored frames
- Safe deletion buffer allows file restoration
- MCP server can be enabled/disabled via configuration and runs in background
- For geotagged maps, only currently selected Canon CR3 image is shown (requires internet for OSM tiles)
- XMP files don't appear as media items
- File operations (copy/move/delete) configurable, drag-drop supported
- Time-based clustering works for folders with up to 5000 media files
