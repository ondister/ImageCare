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

- `ImageCare.Core/` - domain and service layer: XMP annotations, SQLite annotation index, folder scanning/clustering/statistics, media previews and metadata. Platform-specific implementations also live here and are selected by UI DI.
- `ImageCare.UI.Avalonia/` - Prism app. DI and navigation registration are in `App.axaml.cs`; region constants are in `RegionNames.cs`; shell layout is in `Views/MainWindow.axaml`.
- `ImageCare.Mcp/` - in-process MCP host and tools (`Tools/`, `Sessions/`, `Tags/`, `Security/`), default endpoint `http://127.0.0.1:5277/mcp`.
- `LibRawDotNet/` - P/Invoke LibRaw wrapper; `ImageCare.Core` depends on it.
- Project dependencies: UI -> Core + Mcp; Mcp -> Core; tests -> their target projects.

## Non-obvious architecture

- `MediaAnnotationsCoordinator` is started and stopped by `App.axaml.cs`. It coordinates XMP storage with the SQLite annotation index. XMP files live next to media files and are deliberately excluded from media lists.
- `XmpMediaAnnotationsStorageService` is a facade over `SidecarXmpStorage`, `EmbeddedJpegXmpStorage`, and `XmpDocumentCodec`. Preserve unknown XML and qualifiers, optimistic concurrency, sidecar placement, JPEG APP1 ordering, scan bytes, atomic replacement, and post-commit snapshot semantics.
- `SqliteMediaAnnotationsIndex` is a facade over `SqliteMediaAnnotationsDatabase`, `SqliteMediaAnnotationsQueries`, and `MediaAnnotationsIndexReconciler`. There are no migrations; the schema is created at runtime.
- `WindowsMediaPreviewOperationsService` is a cross-platform facade over `MediaTransferService`, `SafeDeletionService`, `MediaPreviewNavigationService`, and `ExternalProcessLauncher`. `DeletionManifestStore` owns `deletion-manifest.json`; safe deletion preserves `ImageCare_Deleted_` temporary names, journal states, rollback, atomic manifest writes, and buffer trimming.
- `WindowsConfigurationFileSource` is also cross-platform despite its name and uses `Environment.SpecialFolder.LocalApplicationData`.
- Media previews dispatch through `MediaPreviewProvidersFactory` to per-format `IMediaPreviewProvider`s (`Jpeg`, `Cr3`, `Arw`, `Nef`, `Dng`, `Mp4`, unsupported fallback). Add a provider and metadata type rather than branching on extensions in UI code.
- `MediaAnnotationCache`, `MediaBatchOperationRunner`, `MediaPreviewLoader`, and `MediaFolderFileIndex` contain behavior shared by Preview and Glance. Keep the grid and horizontal strip as separate view-models because their layout and viewport contracts differ.
- The horizontal preview strip uses `PreviewsListBox` as a carousel: selection changes center the current item, including the first and last item, while `HorizontalScrollBehavior` reports content-relative offsets for adaptive bitmap loading. Do not replace centered navigation with plain `ScrollIntoView`.
- `MainVideoViewModel` projects commands and normalized player state through `IMediaPlayerAdapter`. `MainVideoView` creates `MpvPlayerAdapter` on Windows or `MacPlayerAdapter` on macOS. Frame thumbnail extraction remains independent from the active player; preserve the MP4 placeholder fallback.
- macOS video playback uses AVPlayer via `Platforms/macOS/MacVideoPlayerView`; frame thumbnails use `MacOsVideoFrameExtractor` with AVAssetImageGenerator. Windows playback and extraction use libmpv from `ImageCare.UI.Avalonia/player-native/win-x64/libmpv-2.dll`; only the Windows DLL is copied by the UI project.
- The location map renders only the currently selected geotagged CR3, DNG, NEF, or ARW image and needs internet for OpenStreetMap tiles.
- Native RAW assets: `Directory.Build.targets` copies `LibRawDotNet/libraw.dll` into Windows exe/test outputs; macOS dylibs flow transitively from `LibRawDotNet/runtimes/osx-arm64/native`. If RAW previews fail, check native assets before managed code.
- Time-based clustering is skipped when a folder panel exceeds 5,000 files (`GlancePanelViewModel` / `PreviewPanelViewModel`).
- Prism dialogs use `RegisterDialog`/`RegisterDialogWindow`; the shared dialog host is registered as `childWindow`.

## UI composition and localization

- `AvaloniaUseCompiledBindingsByDefault=false`: XAML binding mistakes fail at runtime, not during build. Run the app after XAML or binding changes.
- Do not use manual `partial` declarations to split view-models or services. Avalonia-generated `partial` code-behind remains framework plumbing, not a decomposition technique.
- `FoldersViewModel` is a composition root over `FolderTreeViewModel`, `FolderSearchViewModel`, `RecentFoldersViewModel`, and `FolderReconciliationCoordinator`. Folder mutations belong to the singleton `FolderMutationCoordinator`; `DirectoryViewModel` delegates create, rename, and delete operations to it.
- `FoldersView.axaml` binds directly to folder facets. Preserve `SortedObservableCollection` ordering, reconciliation versions, left/right `FileManagerPanel` identity, search cancellation, and `RevealSelectedFolderRequested`.
- `SettingsViewModel` is a thin root over `SettingsGeneralViewModel`, `FileAssociationsViewModel`, `AnnotationsSettingsViewModel`, `SafeDeletionSettingsViewModel`, and `McpServerSettingsViewModel`. Keep open/close and cross-tab save coordination in the root. MCP settings serialize rapid state changes and apply the latest state.
- `SettingsView.axaml` binds directly to settings facets. Preserve save-on-setter behavior, tag refresh, localization option rebuilds, and MCP controller lifecycle.
- New UI strings go in both `ImageCare.UI.Avalonia/Localization/Strings.resx` and `Strings.ru.resx`; XAML uses `{loc:Loc Key}` via `LocExtension`.
- `ImageCare.UI.Avalonia/tags-example.txt` is the user-facing tag dictionary and is copied to output. There is no root `tags-example.txt`.

## Adaptive help center

- Keep help centralized. `AdaptiveHelpService` owns event subscriptions and presentation decisions. Feature views publish normal domain events and must not decide when hints appear.
- `AdaptiveHelpCatalog` owns hint definitions and localized message/reason text. `AdaptiveHelpProgressStore` normalizes, mutates, and saves persisted progress. Add new hints to the catalog, their candidate rules to `AdaptiveHelpPolicy`, and tests to `AdaptiveHelpPolicyTests`.
- `AdaptiveHelpPolicy` contains deterministic eligibility, ranking, cooldown, return-threshold, fatigue, and mastery rules. Keep it free of Avalonia, Prism, Rx, and localization.
- `ShellUiActionService` is the only bridge from recommendations to the shell. Help actions may focus folder/preview panels, restore saved folder selections, or open search/settings. They must never copy, move, or delete files without a separate explicit user action.
- Drag-and-drop between the lower preview panels is the primary transfer workflow. Keep `transfer-media` above optional recommendations when both folders and a media selection are ready. Show the configured default operation and modifier key in its text.
- Automatic help shows one recommendation per session, observes a 24-hour cooldown, and stops repeating a topic after two closes. Repeated-error help requires two user-visible errors within five minutes. Manual help can still be opened from the title-bar icon.
- The return threshold is three times the median interval in the last ten sessions, clamped to 14–60 days, with a 30-day fallback. A basic workflow hint is mastered after three successful real actions and then leaves normal ranking.
- Persistent models are in `ImageCare.Core/Domain/Configuration/AdaptiveHelpConfiguration.cs` and properties on `Configuration`. Normalize collection properties after deserializing old or hand-edited JSON.
- `AdaptiveHelpContextService` carries transient surface state, including folder-overview selection and batch activity. Views may update this context but must not rank or localize hints.
- `Views/AdaptiveHelpPanel.axaml` is shared by `MainWindow` and `GlancePanelView`; the platform-aligned title-bar icon remains in `MainWindow.axaml`. Keep both macOS and Windows placement branches when changing the icon.
- Targeted checks:
  - `dotnet test Tests/ImageCare.UI.Avalonia.Tests/ImageCare.UI.Avalonia.Tests.csproj --filter "FullyQualifiedName~AdaptiveHelpPolicyTests"`
  - `dotnet test Tests/ImageCare.Core.Tests/ImageCare.Core.Tests.csproj --filter "FullyQualifiedName~JsonConfigurationServiceTests"`

## Folder overview

- `GlancePanelViewModel` is a composition root. Behavior lives in focused facets under `ViewModels/Glance/`: `GlancePreviewGridViewModel`, `GlanceSelectionViewModel`, `GlanceAnnotationEditorViewModel`, `GlanceBatchOperationsViewModel`, `GlanceOperationViewModel`, `GlanceDeletedFilesViewModel`, `GlanceFolderContentCoordinator`, and `GlanceTimelineCoordinator`. Wire new behavior into the owning facet rather than back into the root.
- `Views/GlancePanelView.axaml` composes `GlanceToolbarView`, `GlanceSelectionBarView`, `GlanceGridView`, `GlanceZoomBarView`, and `GlanceDeletedFilesView`; the selection bar embeds `GlanceAnnotationEditorView` and `GlanceOperationProgressView`.
- The overview preserves row virtualization through `PreviewRows`, `VirtualizingStackPanel`, and `FixedWrapPanel`. Collection changes are coalesced by `SchedulePreviewRowsRebuild`; do not rebuild rows for selection-only changes.
- Preview quality is adaptive: 512, 768, 1024, or 1536 pixels according to `ImagesPerLine`. Upgrade only visible/retained previews and preserve the global four-decode gate and viewport unloading.
- Selection semantics are desktop-standard: click replaces, Ctrl/Command toggles, Shift selects a range. Keep selection by view-model identity and remove missing items after watcher or file-operation updates.
- Rating and tag edits operate on every selected annotatable image through `IMediaAnnotationsService`; read a fresh snapshot before each write, preserve unrelated tags/rating, continue after per-file failures, and expose cancellation/progress.
- Copy, move, delete, and JPEG export reuse `IMediaBatchOperationsService`. Do not duplicate low-level file operations or bypass safe-delete confirmation. The opposite main-window folder is the copy/move destination.

## Remaining refactoring backlog

The major service and view-model decompositions are complete. Keep later structural work separate from behavior changes and add characterization coverage before moving code.

### Higher priority

1. Preview/Glance folder and viewport duplication.
   - `PreviewFolderContentCoordinator` and `GlanceFolderContentCoordinator` still duplicate parts of folder loading and watcher reconciliation. Extract only lifecycle-neutral behavior; their surface-specific selection and layout contracts must remain separate.
   - `PreviewImageStripViewModel` and `GlancePreviewGridViewModel` still own similar viewport range and preview-loading policy. Extend `MediaPreviewLoader` or add a small pure range helper only when a concrete change needs both paths.
   - Do not merge the view-models: the horizontal carousel and grid have different virtualization, quality, and interaction contracts.

2. `ViewModels/Domain/DirectoryViewModel.cs`.
   - Mutations already delegate to `FolderMutationCoordinator`; the node still owns lazy loading, rename UI state, subtree path projection, and icon state.
   - Move more behavior only with characterization tests. One instance exists per folder, so do not resolve heavy dependencies per node.

### Watch list

- `PreviewPanelViewModel` and `GlancePanelViewModel` are composition roots; high constructor dependency counts are acceptable there, but introduce a parameter object or factory before adding more facets.
- `FoldersView.axaml`, `SettingsView.axaml`, `MediaSearchView.axaml`, and `Controls/MediaPreviewCard.axaml` remain large. Do not create wrapper views or manual partial classes only to reduce line counts; split them only when a functional boundary justifies it.
- `MediaSearchViewModel` mixes search, tag filters/suggestions, thumbnail loading, and navigation; split it if search grows.
- `JpegExifMetadataWriter` is cohesive low-level EXIF code; leave it unless adding formats.
- `ReviewSessionService` and `ImageCareMcpHost` are moderately large but cohesive; revisit only if the MCP tool surface grows.
- `FileWatcherFolderStatisticsService` mixes scanning, watcher setup, bucket maintenance, and media filtering; split watcher ingestion from statistics if clustering changes.

### Refactoring rules

- Keep refactors separate from behavior changes.
- Preserve safe deletion, MCP destructive-operation guards, adaptive-help ownership, XMP round-trip behavior, and separate Preview/Glance layout contracts.
- Prefer focused collaborators and facades over interfaces for every class. Add an interface only for a real boundary, lifetime, or test seam.
- Do not introduce manual `partial` classes as a decomposition mechanism.
- UI tests cover extracted helpers and facets, but direct Preview/Glance view-model, selection, scroll behavior, and XAML coverage remains limited. Exercise affected views in a real GUI session after UI changes.

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
- The Core suite currently has eight pre-existing failures in `FolderTagsInheritanceTests` and `FolderTagApplicationServiceTests`: inherited folder tags are empty or incomplete. Treat a fix as a separate behavior change, not part of unrelated refactoring.
- `ImageCare.Mcp.Tests` starts a real Kestrel host on ephemeral ports with temp directories and inert service mocks; no external services are required.
- `ImageCare.UI.Avalonia.Tests` covers extracted folder/settings facets, media cache/index/loader/batch helpers, adaptive-help collaborators and policy, and `MainVideoViewModel`. It does not provide broad XAML integration coverage.
- `LibRawDotNet.Tests` needs the native libraw library in its output; failures there usually mean missing native assets.
- There are no manual code-generation steps or database migrations. The SQLite index is created at runtime.

## Existing docs

- `README.md` / `README.ru.md` - user-facing behavior and controls.
- `docs/mcp-server.ru.md` - MCP setup, tool list, security model.
- `ClearBinObjFolders.bat` - Windows cleanup helper for `bin`/`obj`.
