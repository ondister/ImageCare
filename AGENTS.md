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
- macOS video playback uses AVPlayer via `Platforms/macOS/MacVideoPlayerView`; frame thumbnails use `MacOsVideoFrameExtractor` with AVAssetImageGenerator. Windows playback and frame extraction use libmpv from `ImageCare.UI.Avalonia/player-native/win-x64/libmpv-2.dll`; only the Windows DLL is copied by the UI project. Keep thumbnail extraction independent from the active player and preserve the MP4 placeholder fallback.
- The location map renders only the currently selected geotagged CR3, DNG, NEF, or ARW image and needs internet for OpenStreetMap tiles.
- Native RAW assets: `Directory.Build.targets` copies `LibRawDotNet/libraw.dll` into Windows exe/test outputs; macOS dylibs flow transitively from `LibRawDotNet/runtimes/osx-arm64/native`. If RAW previews fail, check native assets before managed code.
- Time-based clustering is skipped when a folder panel exceeds 5,000 files (`GlancePanelViewModel` / `PreviewPanelViewModel`).
- Prism dialogs use `RegisterDialog`/`RegisterDialogWindow`; the shared dialog host is registered as `childWindow`.

## UI and localization

- `AvaloniaUseCompiledBindingsByDefault=false`: XAML binding mistakes fail at runtime, not build. UI tests cover only collections and `ViewModelBase`; run the app after view/XAML changes.
- New UI strings go in both `ImageCare.UI.Avalonia/Localization/Strings.resx` and `Strings.ru.resx`; XAML uses `{loc:Loc Key}` via `LocExtension`.
- `ImageCare.UI.Avalonia/tags-example.txt` is the user-facing tag dictionary and is copied to output. There is no root `tags-example.txt`.

## Adaptive help center

- Keep help centralized. `ImageCare.UI.Avalonia/Services/AdaptiveHelpService.cs` owns event subscriptions, persisted progress, localization, and presentation state. Feature views publish their normal domain events and must not decide when hints appear.
- `AdaptiveHelpPolicy.cs` contains deterministic eligibility, ranking, cooldown, return-threshold, fatigue, and mastery rules. Keep it free of Avalonia, Prism, Rx, and localization so policy tests remain fast.
- `ShellUiActionService.cs` is the only bridge from recommendations to the shell. Help actions may focus folder/preview panels, restore saved folder selections, or open search/settings. They must never copy, move, or delete files without a separate explicit user action.
- Drag-and-drop between the lower preview panels is the primary transfer workflow. Keep `transfer-media` above optional recommendations when both folders and a media selection are ready. Show the configured default operation and modifier key in its text.
- Automatic help shows one recommendation per session, observes a 24-hour cooldown, and stops repeating a topic after two closes. Repeated-error help requires two user-visible errors within five minutes. Manual help can still be opened from the title-bar icon.
- The return threshold is three times the median interval in the last ten sessions, clamped to 14–60 days, with a 30-day fallback. A basic workflow hint is mastered after three successful real actions and then leaves normal ranking.
- Persistent models are in `ImageCare.Core/Domain/Configuration/AdaptiveHelpConfiguration.cs` and properties on `Configuration`. Normalize collection properties after deserializing old or hand-edited JSON.
- `AdaptiveHelpContextService.cs` carries transient surface state, including folder-overview selection and batch activity. Views may update this context but must not rank or localize hints.
- `Views/AdaptiveHelpPanel.axaml` is the reusable panel host shared by `MainWindow` and `GlancePanelView`; the platform-aligned title-bar icon remains in `MainWindow.axaml`. Keep both macOS and Windows placement branches when changing the icon.
- Add every help string to both resource files. When adding a hint, update the catalog in `AdaptiveHelpService`, its candidate rule in `AdaptiveHelpPolicy`, and `AdaptiveHelpPolicyTests`.
- Targeted checks:
  - `dotnet test Tests/ImageCare.UI.Avalonia.Tests/ImageCare.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~AdaptiveHelpPolicyTests"`
  - `dotnet test Tests/ImageCare.Core.Tests/ImageCare.Core.Tests.csproj --filter "FullyQualifiedName~JsonConfigurationServiceTests"`

## Folder overview

- `GlancePanelViewModel` is a composition root. Behaviour lives in focused facets under `ViewModels/Glance/`: `GlancePreviewGridViewModel` (loading, viewport, zoom/sort), `GlanceSelectionViewModel`, `GlanceAnnotationEditorViewModel`, `GlanceBatchOperationsViewModel`, `GlanceOperationViewModel`, `GlanceDeletedFilesViewModel`, `GlanceFolderContentCoordinator` (folder loading/watcher sync), `GlanceTimelineCoordinator`, and `GlanceAnnotationCache`. Wire new behaviour into the owning facet rather than back into the root.
- `Views/GlancePanelView.axaml` composes `GlanceToolbarView`, `GlanceSelectionBarView`, `GlanceGridView`, `GlanceZoomBarView`, and `GlanceDeletedFilesView`; the selection bar embeds `GlanceAnnotationEditorView` and `GlanceOperationProgressView`. Facet-typed parts bind directly to their facet (`PreviewGrid.*`, `DeletedFiles.*`, `Annotations.*`, `Operations.*`); composition parts such as the toolbar and selection bar bind through the root.
- The overview preserves row virtualization through `PreviewRows`, `VirtualizingStackPanel`, and `FixedWrapPanel`. Collection changes are coalesced by `SchedulePreviewRowsRebuild`; do not rebuild rows for selection-only changes.
- Preview quality is adaptive: 512, 768, 1024, or 1536 pixels according to `ImagesPerLine`. Upgrade only visible/retained previews and preserve the global four-decode gate and viewport unloading.
- Selection semantics are desktop-standard: click replaces, Ctrl/Command toggles, Shift selects a range. Keep selection by view-model identity and remove missing items after watcher or file-operation updates.
- Rating and tag edits operate on every selected annotatable image through `IMediaAnnotationsService`; read a fresh snapshot before each write, preserve unrelated tags/rating, continue after per-file failures, and expose cancellation/progress.
- Copy, move, delete, and JPEG export reuse `IMediaBatchOperationsService`. Do not duplicate low-level file operations or bypass safe-delete confirmation. The opposite main-window folder is the copy/move destination.

## Refactoring backlog

`GlancePanelViewModel`/`GlancePanelView` are the reference composition pattern: a thin root plus focused facets and sub-views. The items below are structural debt, not known functional defects. Do not start them opportunistically inside a feature change; land each as a separate refactor with the full test suite green.

### High priority

1. `ViewModels/FoldersViewModel.cs` (1125 lines, 13 constructor dependencies) and `Views/FoldersView.axaml` (385 lines).
   - Mixes folder-tree state, search session state, recent folders, folder-tags dialog, drives, watcher/reconciliation, navigation to preview, and create/rename/delete folder commands.
   - Split into facets under `ViewModels/Folders/` (`FolderTreeViewModel`, `FolderSearchViewModel`, `RecentFoldersViewModel`, `FolderReconciliationCoordinator`); keep navigation and cross-facet wiring in the root. Split the view into tree, search overlay, and recent-folders parts.
   - Watch for `SortedObservableCollection` ordering, reconciliation version bookkeeping, left/right `FileManagerPanel` identity, search cancellation tokens, and `RevealSelectedFolderRequested`.

2. `ViewModels/SettingsViewModel.cs` (1021 lines, 8 dependencies) and `Views/SettingsView.axaml` (302 lines).
   - Independent settings groups share one view-model: general/drag-drop/adaptive help, file associations, annotation index + pinned tags + media search roots, safe deletion, and the MCP server.
   - Split per tab (`SettingsGeneralViewModel`, `FileAssociationsViewModel`, `AnnotationsSettingsViewModel`, `SafeDeletionSettingsViewModel`, `McpServerSettingsViewModel`) with per-tab views; the root owns `Open`/`Close` and `SaveConfiguration`.
   - Watch for many setters calling `SaveConfiguration`, `AvailableTags` refresh, MCP controller lifecycle, and localization option rebuilds.

3. `ImageCare.Core/Services/MediaPreviewOperationsService/WindowsMediaPreviewOperationsService.cs` (1145 lines, about 40 methods).
   - God service mixing copy/move bundles, the safe-delete buffer and `deletion-manifest.json`, undo/restore/permanent delete, deletion-journal reconciliation, selected-preview navigation, and external-process launching.
   - Split into `MediaTransferService` (copy/move, unique names, rollback), `SafeDeletionService` (buffer, manifest, journal, undo/restore/permanent delete), and a small navigation/external-process service; keep `IMediaPreviewOperationsService` as a facade so UI and MCP callers do not change.
   - Preserve `deletion-manifest.json`, `ImageCare_Deleted_` naming, rollback, and buffer trimming. MCP `dryRun`/`MaxFilesPerDelete` are separate guards and must also stay. Existing tests: `WindowsMediaPreviewOperationsServiceTests`.

4. Preview/Glance facet duplication.
   - Rough normalized-line overlap: `PreviewAnnotationCache`/`GlanceAnnotationCache` 58%, `PreviewFolderContentCoordinator`/`GlanceFolderContentCoordinator` 43%, `PreviewImageStripViewModel`/`GlancePreviewGridViewModel` 32%, `PreviewBatchOperationsViewModel`/`GlanceBatchOperationsViewModel` 24%. Shared method names include `CopySelectionAsync`, `MoveSelectionAsync`, `DeleteSelectionAsync`, `ExportSelectionAsync`, `RunBatchAsync`, `AppendPreviewsAsync`, `LoadImageAsync`, `UnloadPreviewsOutsideRange`, `HandleScrollAsync`, and `ApplyClusterColors`.
   - Extract shared, non-UI helpers incrementally, highest similarity first: one `MediaAnnotationCache`; a `MediaFolderContentCoordinator` base for folder loading and watcher reconciliation; a `MediaBatchOperationRunner` for progress, destination resolution, single-vs-multi dispatch, and notifications; and a `MediaPreviewLoader`/viewport helper for adaptive decode, the four-decode gate, unload-outside-range, sort, and cluster colors.
   - Do not merge the view-models themselves: the grid (rows, zoom, images-per-line) and the strip (horizontal virtualization, drop handling) have different layout contracts. Share behaviour, not view state.

### Medium priority

5. `ImageCare.Core/Services/MediaAnnotationsIndex/SqliteMediaAnnotationsIndex.cs` (876 lines).
   - One class owns initialization, schema DDL, upsert/search/tag queries, remove/move, directory reconciliation, and rebuild; the longest method is about 134 lines. Separate schema/connection lifetime from query and reconciliation collaborators behind the existing `IMediaAnnotationsIndex`. There are no migrations; the schema is created at runtime.

6. `Services/AdaptiveHelpService.cs` (688 lines).
   - Keep the service as the single event/presentation owner per the help-center rules, but extract pure collaborators: an `AdaptiveHelpCatalog` (hint definitions plus message/reason text) and an `AdaptiveHelpProgressStore` (get/create/normalize/progress mutation). Keep `AdaptiveHelpPolicy` framework-free and update `AdaptiveHelpPolicyTests`.

7. `ViewModels/MainVideoViewModel.cs` (642 lines).
   - Mixes libmpv event wiring, AVPlayer/macOS state sync, navigation subscriptions, and transport commands. Extract `IMediaPlayerAdapter` implementations (`MpvPlayerAdapter`, `MacPlayerAdapter`) that expose state/position/seekable/volume through one normalized event set; keep the view-model for commands and property projection. Preserve platform-conditional registration and the MP4 placeholder fallback.

8. `ViewModels/Domain/DirectoryViewModel.cs` (544 lines).
   - A per-node view-model that also owns lazy loading, rename UI, create/delete folder commands, subtree path rewriting, and icon state. Move folder mutations into a service/coordinator and keep the node as tree state plus lazy children. One instance exists per folder, so avoid resolving heavy dependencies per node.

9. `ImageCare.Core/Services/MediaAnnotationsService/XmpMediaAnnotationsStorageService.cs` (562 lines).
   - Handles sidecar discovery, sidecar read/write, embedded XMP read/write, and bundle resolution; the longest method is about 102 lines. Split sidecar and embedded-XMP paths behind the existing `IMediaAnnotationsStorageService`, keeping the "XMP next to media, excluded from media lists" contract.

### Low priority / watch list

- `PreviewPanelViewModel` (217 lines, 23 constructor dependencies) and `GlancePanelViewModel` (193 lines, 20 dependencies) are composition roots; high parameter counts are acceptable there, but introduce a parameter object/factory before adding more facets.
- `Views/FoldersView.axaml` (385), `Views/SettingsView.axaml` (302), `Views/MediaSearchView.axaml` (220), and `Controls/MediaPreviewCard.axaml` (202) are the largest remaining views; split them with the same composition approach as `GlancePanelView`.
- `MediaSearchViewModel` (396) mixes search, tag filters/suggestions, thumbnail loading, and navigation; split it if search grows.
- `JpegExifMetadataWriter` (460, static) is cohesive low-level EXIF writing; leave it unless adding formats.
- `ReviewSessionService` (481) and `ImageCareMcpHost` (384) are moderately large but cohesive; revisit only if the MCP tool surface grows.
- `FileWatcherFolderStatisticsService` (427) mixes scanning, watcher setup, bucket maintenance, and media filtering; split watcher ingestion from statistics if clustering changes.

### Rules for this backlog

- Keep refactors separate from behaviour changes.
- UI view-models have almost no direct test coverage (`ImageCare.UI.Avalonia.Tests` covers only collections, `ViewModelBase`, adaptive-help policy, and `FolderTagsEditorViewModel`). Add characterization tests for selection, batch operations, and reconciliation before moving code.
- `AvaloniaUseCompiledBindingsByDefault=false`: after any XAML split, run the app and exercise the affected view; binding failures are runtime-only.
- Preserve safe deletion, MCP destructive-operation guards, adaptive-help ownership rules, and the Preview/Glance layout contracts.
- Suggested order: (1) shared annotation cache and folder coordinator, (2) Preview/Glance batch runner, (3) `FoldersViewModel` and view, (4) `SettingsViewModel` and view, (5) `WindowsMediaPreviewOperationsService`, (6) remaining items.

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
- `ImageCare.UI.Avalonia.Tests` covers view-model helpers and deterministic adaptive-help policy rules. `LibRawDotNet.Tests` needs the native libraw library in its output; failures there usually mean missing native assets.
- There are no manual codegen steps or database migrations. The SQLite index is created at runtime.

## Existing docs

- `README.md` / `README.ru.md` - user-facing behavior and controls.
- `docs/mcp-server.ru.md` - MCP setup, tool list, security model.
- `ClearBinObjFolders.bat` - Windows cleanup helper for `bin`/`obj`.
