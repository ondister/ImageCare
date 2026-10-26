# ImageCare - Agent Instructions

ImageCare is a desktop media manager for reviewing, sorting, annotating, and moving large photo collections. It uses .NET 10, Avalonia 12, and Prism/DryIoc. Release targets are Windows x64 and macOS arm64 only; the UI throws `PlatformNotSupportedException` on other platforms. The MCP server runs in-process inside the UI.

## Commands

```powershell
dotnet restore ImageCare.sln
dotnet build ImageCare.sln --configuration Release --no-restore
dotnet test ImageCare.sln --configuration Release --no-restore
dotnet run --project ImageCare.UI.Avalonia/ImageCare.UI.Avalonia.csproj
```

- Run one test: `dotnet test Tests/ImageCare.Core.Tests/ImageCare.Core.Tests.csproj --filter "FullyQualifiedName~Cr3MediaPreviewProviderTests"`
- Run one test project: `dotnet test Tests/ImageCare.Mcp.Tests/ImageCare.Mcp.Tests.csproj --configuration Release`
- Tests use NUnit 4 with `NUnit3TestAdapter`, not xUnit/MSTest. Moq is referenced only by `ImageCare.Core.Tests` and `ImageCare.Mcp.Tests`.
- There is no lint/format workflow. `TreatWarningsAsErrors` is global (`Directory.Build.targets`); `ImageCare.Core` and `LibRawDotNet` also set `IsAotCompatible=true`, so AOT analyzer warnings fail the build.
- The SDK is pinned by `global.json` (10.0.401, `latestPatch`; prereleases disallowed).

## CI and release

- `.github/workflows/windows-build-deploy.yml` runs tests on Windows for pushes to `dev` and tags `v*`. There is no `pull_request` trigger, so feature branches get no CI until pushed to `dev`.
- Tags `v*` also publish Windows x64 and macOS arm64 artifacts and create a GitHub release. macOS is only built on tags; verify macOS-specific changes locally with the publish script.

### Publish

macOS arm64:

```bash
zsh scripts/publish-macos-arm64.sh
# Output: artifacts/macos-arm64/ImageCare.app (ad-hoc signed)
```

Windows x64:

```powershell
dotnet restore ImageCare.UI.Avalonia/ImageCare.UI.Avalonia.csproj --runtime win-x64 -p:PublishReadyToRun=true
dotnet publish ImageCare.UI.Avalonia/ImageCare.UI.Avalonia.csproj --configuration Release --runtime win-x64 --self-contained true --no-restore -p:PublishReadyToRun=true --output artifacts/windows-x64/publish
```

`dotnet publish` without an explicit RID fails by design (`RequireRuntimeIdentifierForPublish`). Only `win-x64` and `osx-arm64` are supported.

## Repository map

- `ImageCare.Core/` - domain and service layer: XMP sidecar annotations, SQLite annotation index, folder scanning/clustering/statistics, media preview and metadata providers. Platform-specific implementations also live here and are selected by UI DI.
- `ImageCare.UI.Avalonia/` - Prism app. DI and navigation registration are in `App.axaml.cs`; region constants in `RegionNames.cs`; shell layout in `Views/MainWindow.axaml`.
- `ImageCare.Mcp/` - in-process MCP host and tools (`Tools/`, `Sessions/`, `Tags/`, `Security/`), default endpoint `http://127.0.0.1:5277/mcp`.
- `LibRawDotNet/` - P/Invoke LibRaw wrapper; `ImageCare.Core` depends on it.
- Project dependencies: UI -> Core + Mcp; Mcp -> Core; tests -> their target projects.

## Non-obvious architecture

- `MediaAnnotationsCoordinator` is started/stopped by `App.axaml.cs` and coordinates XMP storage with the SQLite annotation index. XMP files live next to media files and are deliberately excluded from media lists.
- Safe deletion is implemented in `WindowsMediaPreviewOperationsService` using `deletion-manifest.json` and `ImageCare_Deleted_` temporary files. Preserve that flow when touching file operations.
- `WindowsMediaPreviewOperationsService` and `WindowsConfigurationFileSource` are cross-platform despite their names: they delegate through injected `IFileSystemService`/`IProcessService` or `Environment.SpecialFolder.LocalApplicationData`.
- Media previews dispatch through `MediaPreviewProvidersFactory` to per-format `IMediaPreviewProvider`s (`Jpeg`, `Cr3`, `Arw`, `Nef`, `Dng`, `Mp4`, unsupported fallback). Add a provider and metadata type rather than branching on extensions in UI code.
- macOS video uses AVPlayer via `Platforms/macOS/MacVideoPlayerView`, not libmpv. Windows uses libmpv from `ImageCare.UI.Avalonia/player-native/win-x64/libmpv-2.dll`; only the Windows DLL is copied by the UI project.
- The location map renders only the currently selected Canon CR3 or DNG image (GPS is not parsed for other formats) and needs internet for OpenStreetMap tiles.
- Native RAW assets: `Directory.Build.targets` copies `LibRawDotNet/libraw.dll` into Windows exe/test outputs; macOS dylibs flow transitively from `LibRawDotNet/runtimes/osx-arm64/native`. If RAW previews fail, check native assets before managed code.
- Time-based clustering is skipped when a folder panel exceeds 5,000 files (`GlancePanelViewModel` / `PreviewPanelViewModel`).
- Prism dialogs use `RegisterDialog`/`RegisterDialogWindow`; the shared dialog host is registered as `childWindow`.

## UI and localization

- `AvaloniaUseCompiledBindingsByDefault=false`: XAML binding mistakes fail at runtime, not build. UI tests cover only collections and `ViewModelBase`; run the app after view/XAML changes.
- New UI strings go in both `ImageCare.UI.Avalonia/Localization/Strings.resx` and `Strings.ru.resx`; XAML uses `{loc:Loc Key}` via `LocExtension`.
- `ImageCare.UI.Avalonia/tags-example.txt` is the user-facing tag dictionary and is copied to output. There is no root `tags-example.txt`.

## MCP server

- `ImageCareMcpHost` uses `WithToolsFromAssembly`. After adding or renaming a tool, update `Tests/ImageCare.Mcp.Tests/McpHostSmokeTests.ExpectedTools` and `docs/mcp-server.ru.md`.
- Destructive operations must keep `dryRun` semantics and the `MaxFilesPerDelete` guard.
- Configuration comes from `%LOCALAPPDATA%\ImageCare\configuration.json` (`McpServerEnabled`, `McpServerPort`, `McpServerAccessToken`); sessions/caches go to `%LOCALAPPDATA%\ImageCare\Mcp`. The host is loopback-only and validates the `Host` header.
- `ImageCare.Mcp` uses `Microsoft.AspNetCore.App` via `FrameworkReference`, so publishing must be self-contained or provide the ASP.NET Core runtime.

## Configuration and logs

- Settings: `%LOCALAPPDATA%\ImageCare\configuration.json`.
- Logs: `%LOCALAPPDATA%\ImageCare\Logs\Errors.log` (Serilog, warning level and above, rolling daily).

## Testing notes

- `ImageCare.Core.Tests` reads fixtures from `TestImages/` under `TestContext.CurrentContext.TestDirectory`. Preview tests call `Assert.Ignore` when fixtures are missing, so absent files silently reduce coverage.
- `ImageCare.Mcp.Tests` starts a real Kestrel host on ephemeral ports with temp directories and inert service mocks; no external services are required.
- `ImageCare.UI.Avalonia.Tests` is small (collections/`ViewModelBase`). `LibRawDotNet.Tests` needs the native libraw library in its output; failures there usually mean missing native assets.
- There are no manual codegen steps or database migrations. The SQLite index is created at runtime.

## Existing docs

- `README.md` / `README.ru.md` - user-facing behavior and controls.
- `docs/mcp-server.ru.md` - MCP setup, tool list, security model.
- `ClearBinObjFolders.bat` - Windows cleanup helper for `bin`/`obj`.
