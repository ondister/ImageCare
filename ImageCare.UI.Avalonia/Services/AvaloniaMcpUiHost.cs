using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.MediaPreviewOperationsService;

using ImageCare.Mcp.Ui;
using ImageCare.UI.Avalonia.Controls;
using ImageCare.UI.Avalonia.ViewModels;
using ImageCare.UI.Avalonia.ViewModels.Domain;
using ImageCare.UI.Avalonia.Views;

using Prism.Navigation.Regions;

namespace ImageCare.UI.Avalonia.Services;

public sealed class AvaloniaMcpUiHost : IMcpUiHost, IDisposable
{
    private readonly IFolderService _folderService;
    private readonly IRegionManager _regionManager;
    private readonly IMediaPreviewOperationsService _previewOperations;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly IDisposable _folderSelectionSubscription;
    private FileManagerPanel? _lastActivePanel;

    public AvaloniaMcpUiHost(
        IFolderService folderService,
        IRegionManager regionManager,
        IMediaPreviewOperationsService previewOperations,
        SynchronizationContext synchronizationContext)
    {
        _folderService = folderService;
        _regionManager = regionManager;
        _previewOperations = previewOperations;
        _synchronizationContext = synchronizationContext;
        _folderSelectionSubscription = folderService.FileSystemItemSelected.Subscribe(
            selectedDirectory => _lastActivePanel = selectedDirectory.FileManagerPanel);
    }

    public bool IsAvailable => true;

    public void Dispose() => _folderSelectionSubscription.Dispose();

    public async Task<McpUiOpenResult> OpenFolderAsync(
        string folderPath,
        McpPanelSide side,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var panel = side == McpPanelSide.Right
            ? FileManagerPanel.Right
            : FileManagerPanel.Left;
        var folderName = Path.GetFileName(folderPath);
        if (string.IsNullOrWhiteSpace(folderName))
        {
            folderName = folderPath;
        }

        try
        {
            var selectedInTree = await InvokeOnUiAsync(
                    () => SelectFolderInTreeAsync(panel, folderPath, cancellationToken))
                .ConfigureAwait(false);

            if (!selectedInTree)
            {
                // The folder may live outside the loaded drive tree (removable media, virtual
                // folders). Keep the previous behaviour and at least switch the preview panel.
                await InvokeOnUiAsync(() =>
                    {
                        _folderService.SetSelectedDirectory(
                            new SelectedDirectory(folderName, folderPath, panel));
                        return true;
                    })
                    .ConfigureAwait(false);
            }

            return new McpUiOpenResult(
                true,
                $"Opened {folderPath} in the {side.ToString().ToLowerInvariant()} panel.",
                folderPath);
        }
        catch (Exception exception)
        {
            return new McpUiOpenResult(false, exception.Message, null);
        }
    }

    public async Task<McpUiState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await InvokeOnUiAsync(() => BuildState(cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return BuildFolderOnlyState();
        }
    }

    private async Task<bool> SelectFolderInTreeAsync(
        FileManagerPanel panel,
        string folderPath,
        CancellationToken cancellationToken)
    {
        var foldersViewModel = GetFoldersViewModel(panel);
        if (foldersViewModel == null)
        {
            return false;
        }

        return await foldersViewModel.SelectFolderInTreeAsync(folderPath, cancellationToken)
                                     .ConfigureAwait(true);
    }

    private McpUiState BuildState(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var leftFolder = _folderService.GetSelectedDirectory(FileManagerPanel.Left);
        var rightFolder = _folderService.GetSelectedDirectory(FileManagerPanel.Right);

        var leftPreviewViewModel = GetPreviewPanelViewModel(FileManagerPanel.Left);
        var rightPreviewViewModel = GetPreviewPanelViewModel(FileManagerPanel.Right);

        var leftSelected = leftPreviewViewModel?.SelectedPreview;
        var rightSelected = rightPreviewViewModel?.SelectedPreview;

        var focusedPanel = ResolveFocusedPanel(leftSelected, rightSelected);
        var focusedPreview = focusedPanel switch
        {
            FileManagerPanel.Left => leftSelected,
            FileManagerPanel.Right => rightSelected,
            _ => null
        };
        var visiblePanel = focusedPanel
                           ?? ResolveVisiblePanel(
                               leftPreviewViewModel,
                               rightPreviewViewModel,
                               leftFolder,
                               rightFolder);
        var visiblePanelViewModel = GetPreviewViewModelFor(visiblePanel, leftPreviewViewModel, rightPreviewViewModel);

        return new McpUiState(
            true,
            focusedPanel switch
            {
                FileManagerPanel.Left => "left",
                FileManagerPanel.Right => "right",
                _ => null
            },
            leftFolder?.Path,
            rightFolder?.Path,
            focusedPreview?.Url,
            visiblePanelViewModel == null
                ? []
                : GetVisibleFiles(visiblePanelViewModel, visiblePanel));
    }

    private FileManagerPanel ResolveVisiblePanel(
        PreviewPanelViewModel? leftPreviewViewModel,
        PreviewPanelViewModel? rightPreviewViewModel,
        DirectoryModel? leftFolder,
        DirectoryModel? rightFolder)
    {
        // Nothing is selected: follow the last folder the user (or the assistant) opened, then
        // the keyboard/mouse focus, and only then fall back to whichever panel has cards.
        if (_lastActivePanel is { } lastActive &&
            GetPreviewViewModelFor(lastActive, leftPreviewViewModel, rightPreviewViewModel) != null)
        {
            return lastActive;
        }

        var focused = GetFocusedPanel();
        if (focused is { } focusedPanel &&
            GetPreviewViewModelFor(focusedPanel, leftPreviewViewModel, rightPreviewViewModel) != null)
        {
            return focusedPanel;
        }

        if (leftPreviewViewModel is { ImagePreviews.Count: > 0 })
        {
            return FileManagerPanel.Left;
        }

        if (rightPreviewViewModel is { ImagePreviews.Count: > 0 })
        {
            return FileManagerPanel.Right;
        }

        return leftFolder != null || leftPreviewViewModel != null
            ? FileManagerPanel.Left
            : FileManagerPanel.Right;
    }

    private static PreviewPanelViewModel? GetPreviewViewModelFor(
        FileManagerPanel panel,
        PreviewPanelViewModel? leftPreviewViewModel,
        PreviewPanelViewModel? rightPreviewViewModel) =>
        panel == FileManagerPanel.Right ? rightPreviewViewModel : leftPreviewViewModel;

    private FileManagerPanel? GetFocusedPanel()
    {
        var anchor = (Visual?)GetFoldersView(FileManagerPanel.Left)
                     ?? (Visual?)GetPreviewPanelView(FileManagerPanel.Left)
                     ?? (Visual?)GetFoldersView(FileManagerPanel.Right)
                     ?? (Visual?)GetPreviewPanelView(FileManagerPanel.Right);
        if (anchor == null)
        {
            return null;
        }

        var focusedElement = TopLevel.GetTopLevel(anchor)?.FocusManager?.GetFocusedElement() as Visual;
        while (focusedElement != null)
        {
            switch (focusedElement)
            {
                case FoldersView { DataContext: FoldersViewModel foldersViewModel }:
                    return foldersViewModel.FileManagerPanel;
                case PreviewPanelView { DataContext: PreviewPanelViewModel previewViewModel }:
                    return previewViewModel.FileManagerPanel;
            }

            focusedElement = focusedElement.GetVisualParent();
        }

        return null;
    }

    private FileManagerPanel? ResolveFocusedPanel(
        MediaPreviewViewModel? leftSelected,
        MediaPreviewViewModel? rightSelected)
    {
        if (leftSelected != null && rightSelected != null)
        {
            return _previewOperations.GetLastSelectedMediaPreview() is SelectedMediaPreview lastSelected &&
                   lastSelected.FileManagerPanel == FileManagerPanel.Right
                ? FileManagerPanel.Right
                : FileManagerPanel.Left;
        }

        if (leftSelected != null)
        {
            return FileManagerPanel.Left;
        }

        return rightSelected != null ? FileManagerPanel.Right : null;
    }

    private IReadOnlyList<McpUiFile> GetVisibleFiles(
        PreviewPanelViewModel viewModel,
        FileManagerPanel panel)
    {
        var previews = viewModel.ImagePreviews;
        if (previews.Count == 0)
        {
            return [];
        }

        var view = GetPreviewPanelView(panel);
        var listBox = view?.GetVisualDescendants().OfType<PreviewsListBox>().FirstOrDefault();
        var scrollViewer = listBox?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

        var visible = new List<(double X, MediaPreviewViewModel Preview)>();
        if (listBox != null && scrollViewer != null)
        {
            var viewportWidth = scrollViewer.Viewport.Width;
            foreach (var container in listBox.GetRealizedContainers())
            {
                if (container.DataContext is not MediaPreviewViewModel preview)
                {
                    continue;
                }

                var position = container.TransformToVisual(scrollViewer)?.Transform(default(Point));
                if (position is not { } point ||
                    point.X + container.Bounds.Width <= 0 ||
                    point.X >= viewportWidth)
                {
                    continue;
                }

                visible.Add((point.X, preview));
            }
        }

        // The visual tree is not measurable right after startup (or in tests), so fall back to
        // the cards that surround the selection in the horizontal strip.
        var items = visible.Count > 0
            ? visible.OrderBy(entry => entry.X).Select(entry => entry.Preview)
            : GetFallbackWindow(viewModel);

        return items.Select(ToMcpUiFile).ToArray();
    }

    private static IEnumerable<MediaPreviewViewModel> GetFallbackWindow(PreviewPanelViewModel viewModel)
    {
        var count = viewModel.ImagePreviews.Count;
        if (count == 0)
        {
            yield break;
        }

        var center = viewModel.SelectedPreview is { } selected
            ? viewModel.ImagePreviews.IndexOf(selected)
            : 0;
        if (center < 0)
        {
            center = 0;
        }

        var first = Math.Max(0, center - 3);
        var last = Math.Min(count - 1, center + 3);
        for (var index = first; index <= last; index++)
        {
            yield return viewModel.ImagePreviews[index];
        }
    }

    private static McpUiFile ToMcpUiFile(MediaPreviewViewModel preview) =>
        new(
            preview.Url,
            Path.GetFileName(preview.Url),
            preview.Rating,
            preview.Tags.ToArray());

    private FoldersViewModel? GetFoldersViewModel(FileManagerPanel panel) =>
        GetFoldersView(panel)?.DataContext as FoldersViewModel;

    private FoldersView? GetFoldersView(FileManagerPanel panel)
    {
        var regionName = panel == FileManagerPanel.Right
            ? RegionNames.TargetFoldersRegion
            : RegionNames.SourceFoldersRegion;
        return GetRegionView<FoldersView>(regionName);
    }

    private PreviewPanelViewModel? GetPreviewPanelViewModel(FileManagerPanel panel) =>
        GetPreviewPanelView(panel)?.DataContext as PreviewPanelViewModel;

    private PreviewPanelView? GetPreviewPanelView(FileManagerPanel panel)
    {
        var regionName = panel == FileManagerPanel.Right
            ? RegionNames.TargetPreviewImageRegion
            : RegionNames.SourcePreviewImageRegion;
        return GetRegionView<PreviewPanelView>(regionName);
    }

    private TView? GetRegionView<TView>(string regionName)
        where TView : class
    {
        var region = _regionManager.Regions.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, regionName, StringComparison.Ordinal));
        return region?.Views.OfType<TView>().FirstOrDefault();
    }

    private McpUiState BuildFolderOnlyState()
    {
        var left = _folderService.GetSelectedDirectory(FileManagerPanel.Left);
        var right = _folderService.GetSelectedDirectory(FileManagerPanel.Right);
        return new McpUiState(true, null, left?.Path, right?.Path, null, []);
    }

    private Task<T> InvokeOnUiAsync<T>(Func<T> callback)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _synchronizationContext.Post(
            _ =>
            {
                try
                {
                    completion.TrySetResult(callback());
                }
                catch (OperationCanceledException exception)
                {
                    completion.TrySetCanceled(exception.CancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            },
            null);
        return completion.Task;
    }

    private Task<T> InvokeOnUiAsync<T>(Func<Task<T>> callback)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _synchronizationContext.Post(
            async _ =>
            {
                try
                {
                    completion.TrySetResult(await callback().ConfigureAwait(true));
                }
                catch (OperationCanceledException exception)
                {
                    completion.TrySetCanceled(exception.CancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            },
            null);
        return completion.Task;
    }
}
