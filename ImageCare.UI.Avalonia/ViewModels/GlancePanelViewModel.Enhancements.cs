using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia.Input;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.MediaBatchOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.UI.Avalonia.Controls;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;

namespace ImageCare.UI.Avalonia.ViewModels;

internal partial class GlancePanelViewModel
{
    private readonly IMediaBatchOperationsService _batchOperationsService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly AdaptiveHelpContextService _helpContext;
    private readonly AdaptiveHelpService _adaptiveHelpService;
    private readonly List<GlanceMediaPreviewViewModel> _selectedPreviews = [];
    private readonly ObservableCollection<string> _selectionTags = [];
    private GlanceMediaPreviewViewModel? _selectionAnchor;
    private FileManagerPanel _overviewPanel = FileManagerPanel.Undefined;
    private CancellationTokenSource? _batchOperationCancellation;
    private CancellationTokenSource? _annotationOperationCancellation;
    private bool _isBatchOperationRunning;
    private bool _isAnnotationOperationRunning;
    private int _batchProcessed;
    private int _batchTotal;
    private int _selectedRating = -1;
    private bool _hasMixedRating;
    private string _newTag = string.Empty;
    private string _overviewOperationStatus = string.Empty;
    private bool _previewRowsRebuildPending;
    private int _lastVisibleStartIndex;
    private int _lastVisibleEndIndex = -1;

    public AdaptiveHelpService AdaptiveHelp => _adaptiveHelpService;

    public IReadOnlyList<GlanceMediaPreviewViewModel> SelectedPreviews => _selectedPreviews;

    public int SelectionCount => _selectedPreviews.Count;

    public bool HasSelection => SelectionCount > 0;

    public bool HasMultipleSelection => SelectionCount > 1;

    public string SelectionSummary => _localization.Get("Batch_SelectionCount", SelectionCount);

    public int AnnotatableSelectionCount => _selectedPreviews.Count(preview => preview.MediaFormat.SupportsAnnotations);

    public string AnnotatableSelectionSummary =>
        _localization.Get("Overview_AnnotatableSelection", AnnotatableSelectionCount);

    public ObservableCollection<string> SelectionTags => _selectionTags;

    public bool HasSelectionTags => SelectionTags.Count > 0;

    public IReadOnlyList<int> RatingOptions { get; } = [0, 1, 2, 3, 4, 5];

    public int SelectedRating
    {
        get => _selectedRating;
        set
        {
            var clamped = Math.Clamp(value, -1, 5);
            if (!SetProperty(ref _selectedRating, clamped))
            {
                return;
            }

            if (clamped >= 0)
            {
                HasMixedRating = false;
                if (CanEditSelectionAnnotations)
                {
                    _ = ApplyRatingToSelectionAsync();
                }
            }

            RaisePropertyChanged(nameof(CanApplyRating));
        }
    }

    public bool HasMixedRating
    {
        get => _hasMixedRating;
        private set => SetProperty(ref _hasMixedRating, value);
    }

    public string NewTag
    {
        get => _newTag;
        set => SetProperty(ref _newTag, value ?? string.Empty);
    }

    public bool IsBatchOperationRunning
    {
        get => _isBatchOperationRunning;
        private set
        {
            if (SetProperty(ref _isBatchOperationRunning, value))
            {
                _helpContext.SetBatchOperationRunning(value);
                RaiseOverviewOperationProperties();
            }
        }
    }

    public bool IsAnnotationOperationRunning
    {
        get => _isAnnotationOperationRunning;
        private set
        {
            if (SetProperty(ref _isAnnotationOperationRunning, value))
            {
                RaiseOverviewOperationProperties();
            }
        }
    }

    public bool IsOverviewOperationRunning => IsBatchOperationRunning || IsAnnotationOperationRunning;

    public bool CanRunOverviewOperation => HasSelection && !IsOverviewOperationRunning;

    public string TransferDestinationPath => GetTransferDestination()?.Path ?? string.Empty;

    public bool HasTransferDestination => !string.IsNullOrWhiteSpace(TransferDestinationPath);

    public bool CanTransferSelection => CanRunOverviewOperation && HasTransferDestination;

    public string TransferDestinationText => HasTransferDestination
        ? _localization.Get("Overview_TransferDestination", TransferDestinationPath)
        : _localization["Batch_NoTargetFolder"];

    public string CopySelectionToolTip => HasTransferDestination
        ? _localization.Get("Overview_CopyTo", TransferDestinationPath)
        : _localization["Batch_NoTargetFolder"];

    public string MoveSelectionToolTip => HasTransferDestination
        ? _localization.Get("Overview_MoveTo", TransferDestinationPath)
        : _localization["Batch_NoTargetFolder"];

    public bool CanEditSelectionAnnotations => AnnotatableSelectionCount > 0 && !IsOverviewOperationRunning;

    public bool CanApplyRating => CanEditSelectionAnnotations && SelectedRating >= 0;

    public bool CanExportSelection =>
        CanRunOverviewOperation && _selectedPreviews.Any(preview => preview.MediaFormat.SupportsAnnotations);

    public int BatchProcessed
    {
        get => _batchProcessed;
        private set
        {
            if (SetProperty(ref _batchProcessed, value))
            {
                RaisePropertyChanged(nameof(BatchProgressText));
            }
        }
    }

    public int BatchTotal
    {
        get => _batchTotal;
        private set
        {
            if (SetProperty(ref _batchTotal, value))
            {
                RaisePropertyChanged(nameof(BatchProgressText));
            }
        }
    }

    public string BatchProgressText => _localization.Get("Batch_Progress", BatchProcessed, BatchTotal);

    public string OverviewOperationStatus
    {
        get => _overviewOperationStatus;
        private set => SetProperty(ref _overviewOperationStatus, value);
    }

    public ICommand ClearSelectionCommand { get; private set; } = null!;

    public ICommand CopySelectionCommand { get; private set; } = null!;

    public ICommand MoveSelectionCommand { get; private set; } = null!;

    public ICommand DeleteSelectionCommand { get; private set; } = null!;

    public ICommand ExportSelectionCommand { get; private set; } = null!;

    public ICommand CancelOverviewOperationCommand { get; private set; } = null!;

    public ICommand ApplyRatingCommand { get; private set; } = null!;

    public ICommand AddTagCommand { get; private set; } = null!;

    public ICommand RemoveTagCommand { get; private set; } = null!;

    private void InitializeOverviewCommands()
    {
        ClearSelectionCommand = CreateCommand(ClearOverviewSelection);
        CopySelectionCommand = CreateAsyncCommand(CopyOverviewSelectionAsync);
        MoveSelectionCommand = CreateAsyncCommand(MoveOverviewSelectionAsync);
        DeleteSelectionCommand = CreateAsyncCommand(DeleteOverviewSelectionAsync);
        ExportSelectionCommand = CreateAsyncCommand(ExportOverviewSelectionAsync);
        CancelOverviewOperationCommand = CreateCommand(CancelOverviewOperations);
        ApplyRatingCommand = CreateAsyncCommand(ApplyRatingToSelectionAsync);
        AddTagCommand = CreateAsyncCommand(AddTagToSelectionAsync);
        RemoveTagCommand = CreateAsyncCommand<string>(RemoveTagFromSelectionAsync);
    }

    private void HandlePreviewSelection(MediaPreviewSelectionRequest request)
    {
        if (request.Item is not GlanceMediaPreviewViewModel preview || IsOverviewOperationRunning)
        {
            return;
        }

        var toggle = request.Modifiers.HasFlag(KeyModifiers.Control)
                     || request.Modifiers.HasFlag(KeyModifiers.Meta);
        var range = request.Modifiers.HasFlag(KeyModifiers.Shift);

        if (range && _selectionAnchor != null)
        {
            var anchorIndex = ImagePreviews.IndexOf(_selectionAnchor);
            var targetIndex = ImagePreviews.IndexOf(preview);
            if (anchorIndex >= 0 && targetIndex >= 0)
            {
                var first = Math.Min(anchorIndex, targetIndex);
                var last = Math.Max(anchorIndex, targetIndex);
                var rangeItems = Enumerable.Range(first, last - first + 1)
                    .Select(index => ImagePreviews[index]);
                var selection = toggle
                    ? _selectedPreviews.Concat(rangeItems).Distinct().ToArray()
                    : rangeItems.ToArray();
                SetOverviewSelection(selection, preview, _selectionAnchor);
                return;
            }
        }

        if (toggle)
        {
            var selection = _selectedPreviews.ToList();
            if (!selection.Remove(preview))
            {
                selection.Add(preview);
            }

            SetOverviewSelection(selection, selection.LastOrDefault(), preview);
            return;
        }

        SetOverviewSelection([preview], preview, preview);
    }

    private void SetOverviewSelection(
        IEnumerable<GlanceMediaPreviewViewModel> selection,
        GlanceMediaPreviewViewModel? primary,
        GlanceMediaPreviewViewModel? anchor)
    {
        var valid = selection.Where(ImagePreviews.Contains).Distinct().ToArray();
        foreach (var preview in _selectedPreviews)
        {
            preview.Selected = false;
        }

        _selectedPreviews.Clear();
        _selectedPreviews.AddRange(valid);
        foreach (var preview in _selectedPreviews)
        {
            preview.Selected = true;
        }

        var nextPrimary = primary != null && _selectedPreviews.Contains(primary)
            ? primary
            : _selectedPreviews.LastOrDefault();
        SetProperty(ref _selectedPreview, nextPrimary, nameof(SelectedPreview));
        _selectionAnchor = anchor != null && ImagePreviews.Contains(anchor) ? anchor : nextPrimary;
        RefreshOverviewAnnotationEditor();
        RaiseOverviewSelectionProperties();
        _helpContext.SetFolderOverviewSelection(SelectionCount);
    }

    private void ClearOverviewSelection() =>
        SetOverviewSelection(Array.Empty<GlanceMediaPreviewViewModel>(), null, null);

    private void RemoveMissingSelections()
    {
        var existing = _selectedPreviews.Where(ImagePreviews.Contains).ToArray();
        SetOverviewSelection(existing, existing.LastOrDefault(), existing.LastOrDefault());
    }

    private void RaiseOverviewSelectionProperties()
    {
        RaisePropertyChanged(nameof(SelectedPreviews));
        RaisePropertyChanged(nameof(SelectionCount));
        RaisePropertyChanged(nameof(HasSelection));
        RaisePropertyChanged(nameof(HasMultipleSelection));
        RaisePropertyChanged(nameof(SelectionSummary));
        RaisePropertyChanged(nameof(AnnotatableSelectionCount));
        RaisePropertyChanged(nameof(AnnotatableSelectionSummary));
        RaiseOverviewOperationProperties();
    }

    private void RaiseOverviewOperationProperties()
    {
        RaisePropertyChanged(nameof(IsOverviewOperationRunning));
        RaisePropertyChanged(nameof(CanRunOverviewOperation));
        RaisePropertyChanged(nameof(CanTransferSelection));
        RaisePropertyChanged(nameof(CanEditSelectionAnnotations));
        RaisePropertyChanged(nameof(CanApplyRating));
        RaisePropertyChanged(nameof(CanExportSelection));
    }

    private void RaiseTransferDestinationProperties()
    {
        RaisePropertyChanged(nameof(TransferDestinationPath));
        RaisePropertyChanged(nameof(HasTransferDestination));
        RaisePropertyChanged(nameof(CanTransferSelection));
        RaisePropertyChanged(nameof(TransferDestinationText));
        RaisePropertyChanged(nameof(CopySelectionToolTip));
        RaisePropertyChanged(nameof(MoveSelectionToolTip));
    }

    private void OnFolderSelectionChanged(SelectedDirectory selectedDirectory)
    {
        if (selectedDirectory.FileManagerPanel == GetTransferTargetPanel())
        {
            RaiseTransferDestinationProperties();
        }
    }

    private void RefreshOverviewAnnotationEditor()
    {
        var annotatable = _selectedPreviews.Where(preview => preview.MediaFormat.SupportsAnnotations).ToArray();
        if (annotatable.Length == 0)
        {
            UpdateSelectedRatingEditor(-1, hasMixedRating: false);
            SelectionTags.Clear();
            RaisePropertyChanged(nameof(HasSelectionTags));
            return;
        }

        var firstRating = annotatable[0].Rating;
        var hasMixedRating = annotatable.Any(preview => preview.Rating != firstRating);
        UpdateSelectedRatingEditor(hasMixedRating ? -1 : firstRating, hasMixedRating);

        var tags = annotatable
            .SelectMany(preview => preview.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.Create(_localization.Culture, ignoreCase: true))
            .ThenBy(tag => tag, StringComparer.Ordinal)
            .ToArray();
        SelectionTags.Clear();
        foreach (var tag in tags)
        {
            SelectionTags.Add(tag);
        }

        RaisePropertyChanged(nameof(HasSelectionTags));
    }

    private void UpdateSelectedRatingEditor(int rating, bool hasMixedRating)
    {
        HasMixedRating = hasMixedRating;
        var normalized = Math.Clamp(rating, -1, 5);
        if (_selectedRating != normalized)
        {
            _selectedRating = normalized;
            RaisePropertyChanged(nameof(SelectedRating));
            RaisePropertyChanged(nameof(CanApplyRating));
        }
    }

    private Task ApplyRatingToSelectionAsync()
    {
        var rating = SelectedRating;
        return rating < 0
            ? Task.CompletedTask
            : ApplyAnnotationsToSelectionAsync(
                annotations => new MediaAnnotations(rating, annotations.Tags));
    }

    private async Task AddTagToSelectionAsync()
    {
        var tag = MediaAnnotations.NormalizeTag(NewTag);
        if (tag.Length == 0)
        {
            return;
        }

        await ApplyAnnotationsToSelectionAsync(
            annotations => new MediaAnnotations(annotations.Rating, annotations.Tags.Append(tag)));
        NewTag = string.Empty;
    }

    private Task RemoveTagFromSelectionAsync(string tag) =>
        ApplyAnnotationsToSelectionAsync(
            annotations => new MediaAnnotations(
                annotations.Rating,
                annotations.Tags.Where(existing => !existing.Equals(tag, StringComparison.OrdinalIgnoreCase))));

    private async Task ApplyAnnotationsToSelectionAsync(Func<MediaAnnotations, MediaAnnotations> transform)
    {
        var items = _selectedPreviews.Where(preview => preview.MediaFormat.SupportsAnnotations).ToArray();
        if (items.Length == 0 || IsOverviewOperationRunning)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _annotationOperationCancellation = cancellation;
        IsAnnotationOperationRunning = true;
        BatchProcessed = 0;
        BatchTotal = items.Length;
        var succeeded = 0;
        var failures = new List<string>();
        OverviewOperationStatus = _localization["Annotations_StateSaving"];

        try
        {
            foreach (var preview in items)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                try
                {
                    var snapshot = await _annotationsService.GetSnapshotAsync(preview.Url, cancellation.Token);
                    var updated = transform(snapshot.Annotations);
                    var saved = await _annotationsService.SaveAsync(
                        preview.Url,
                        updated,
                        snapshot.FileState,
                        cancellation.Token);
                    preview.ApplyAnnotations(saved.Annotations, _localization["Annotations_StateSaved"], saved);
                    succeeded++;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures.Add(exception.Message);
                    _logger.Error(exception, "Failed to update overview annotations for {Path}", preview.Url);
                }
                finally
                {
                    BatchProcessed++;
                }
            }

            OverviewOperationStatus = _localization.Get("Overview_AnnotationsCompleted", succeeded, items.Length);
            if (failures.Count > 0)
            {
                _notificationService.SendNotification(
                    new ErrorNotification(
                        OverviewOperationStatus,
                        _localization.Get("Overview_AnnotationsFailed", failures.Count, failures[0])));
            }
        }
        catch (OperationCanceledException)
        {
            OverviewOperationStatus = _localization["Batch_CancelledTitle"];
        }
        finally
        {
            IsAnnotationOperationRunning = false;
            if (ReferenceEquals(_annotationOperationCancellation, cancellation))
            {
                _annotationOperationCancellation = null;
            }

            cancellation.Dispose();
            RefreshOverviewAnnotationEditor();
        }
    }

    private Task CopyOverviewSelectionAsync() => RunTransferAsync(MediaBatchOperationKind.Copy);

    private Task MoveOverviewSelectionAsync() => RunTransferAsync(MediaBatchOperationKind.Move);

    private FileManagerPanel GetTransferTargetPanel() => _overviewPanel switch
    {
        FileManagerPanel.Left => FileManagerPanel.Right,
        FileManagerPanel.Right => FileManagerPanel.Left,
        _ => FileManagerPanel.Undefined
    };

    private DirectoryModel? GetTransferDestination()
    {
        var targetPanel = GetTransferTargetPanel();
        return targetPanel == FileManagerPanel.Undefined
            ? null
            : _folderService.GetSelectedDirectory(targetPanel);
    }

    private async Task RunTransferAsync(MediaBatchOperationKind kind)
    {
        if (!CanRunOverviewOperation)
        {
            return;
        }

        var targetDirectory = GetTransferDestination();
        if (targetDirectory == null || string.IsNullOrWhiteSpace(targetDirectory.Path))
        {
            NotifyOverviewProblem("Batch_NoTargetFolder");
            return;
        }

        var items = GetSelectedMediaPreviews();
        await RunOverviewBatchAsync(
            kind,
            items,
            kind == MediaBatchOperationKind.Copy
                ? _localization.Get("Batch_Start_Copy", items.Count, targetDirectory.Path)
                : _localization.Get("Batch_Start_Move", items.Count, targetDirectory.Path),
            (progress, token) => kind == MediaBatchOperationKind.Copy
                ? _batchOperationsService.CopyAsync(items, targetDirectory.Path, progress, token)
                : _batchOperationsService.MoveAsync(items, targetDirectory.Path, progress, token));
    }

    private async Task DeleteOverviewSelectionAsync()
    {
        if (!CanRunOverviewOperation)
        {
            return;
        }

        var items = GetSelectedMediaPreviews();
        var safeDeletionEnabled = _configurationService.Configuration.Value.SafeDeletionEnabled;
        var message = _localization.Get("Batch_DeleteConfirmMessage", items.Count)
                      + Environment.NewLine
                      + Environment.NewLine
                      + _localization[safeDeletionEnabled
                          ? "Batch_DeleteConfirmSafeDetail"
                          : "Batch_DeleteConfirmUnsafeDetail"];
        var confirmed = await _confirmationDialogService.ConfirmAsync(
            _localization["Batch_DeleteConfirmTitle"],
            message,
            _localization["Common_Delete"],
            isDestructive: true);
        if (!confirmed)
        {
            return;
        }

        await RunOverviewBatchAsync(
            MediaBatchOperationKind.Delete,
            items,
            _localization.Get("Batch_Start_Delete", items.Count),
            (progress, token) => _batchOperationsService.DeleteAsync(items, progress, token));
    }

    private async Task ExportOverviewSelectionAsync()
    {
        if (!CanExportSelection)
        {
            return;
        }

        var items = GetSelectedMediaPreviews()
            .Where(preview => preview.MediaFormat.SupportsAnnotations)
            .ToArray();
        var destination = await _fileDialogService.ShowOpenFolderDialogAsync(
            _localization["Batch_ExportFolderTitle"]);
        if (string.IsNullOrWhiteSpace(destination))
        {
            return;
        }

        await RunOverviewBatchAsync(
            MediaBatchOperationKind.ExportJpeg,
            items,
            _localization.Get("Batch_Start_Export", items.Length, destination),
            (progress, token) => _batchOperationsService.ExportJpegAsync(items, destination, progress, token));
    }

    private async Task RunOverviewBatchAsync(
        MediaBatchOperationKind kind,
        IReadOnlyList<MediaPreview> items,
        string startTitle,
        Func<IProgress<MediaBatchOperationProgress>, CancellationToken, Task<MediaBatchOperationSummary>> operation)
    {
        if (items.Count == 0 || IsOverviewOperationRunning)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _batchOperationCancellation = cancellation;
        BatchProcessed = 0;
        BatchTotal = items.Count;
        IsBatchOperationRunning = true;
        OverviewOperationStatus = startTitle;
        _notificationService.SendNotification(new Notification(startTitle, string.Empty));

        try
        {
            var progress = new Progress<MediaBatchOperationProgress>(batchProgress =>
            {
                BatchProcessed = batchProgress.Processed;
                BatchTotal = batchProgress.Total;
            });
            var summary = await operation(progress, cancellation.Token);
            ReportOverviewBatchSummary(kind, summary);
        }
        catch (OperationCanceledException)
        {
            OverviewOperationStatus = _localization["Batch_CancelledTitle"];
            _notificationService.SendNotification(new Notification(OverviewOperationStatus, string.Empty));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Overview batch {Operation} failed", kind);
            OverviewOperationStatus = _localization["Batch_FailedTitle"];
            _notificationService.SendNotification(
                new ErrorNotification(OverviewOperationStatus, exception.Message));
        }
        finally
        {
            IsBatchOperationRunning = false;
            if (ReferenceEquals(_batchOperationCancellation, cancellation))
            {
                _batchOperationCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void ReportOverviewBatchSummary(MediaBatchOperationKind kind, MediaBatchOperationSummary summary)
    {
        var title = _localization.Get(
            kind switch
            {
                MediaBatchOperationKind.Copy => "Batch_Completed_Copy",
                MediaBatchOperationKind.Move => "Batch_Completed_Move",
                MediaBatchOperationKind.Delete => "Batch_Completed_Delete",
                MediaBatchOperationKind.ExportJpeg => "Batch_Completed_Export",
                _ => "Batch_FailedTitle"
            },
            summary.Succeeded,
            summary.Total);
        OverviewOperationStatus = title;

        if (summary.Cancelled)
        {
            _notificationService.SendNotification(
                new Notification(_localization["Batch_CancelledTitle"], title));
        }
        else if (summary.Failed > 0)
        {
            var detail = summary.Failures.Count == 0
                ? _localization["Batch_FailedTitle"]
                : _localization.Get("Batch_FailedDetail", summary.Failed, summary.Failures[0].Error);
            _notificationService.SendNotification(new ErrorNotification(title, detail));
        }
        else
        {
            _notificationService.SendNotification(new SuccessNotification(title, string.Empty));
        }
    }

    private IReadOnlyList<MediaPreview> GetSelectedMediaPreviews() =>
        _selectedPreviews.Select(_mapper.Map<MediaPreview>).ToArray();

    private void NotifyOverviewProblem(string key) =>
        _notificationService.SendNotification(
            new ErrorNotification(_localization["Batch_FailedTitle"], _localization[key]));

    private void CancelOverviewOperations()
    {
        _batchOperationCancellation?.Cancel();
        _annotationOperationCancellation?.Cancel();
    }

    private int GetOverviewPreviewWidth() => ImagesPerLine switch
    {
        <= 1 => 1_536,
        <= 3 => 1_024,
        <= 6 => 768,
        _ => 512
    };

    private void ReloadVisiblePreviewsForCurrentScale()
    {
        if (ImagePreviews.Count == 0)
        {
            return;
        }

        var first = Math.Clamp(_lastVisibleStartIndex, 0, ImagePreviews.Count - 1);
        var fallbackEnd = Math.Min(ImagePreviews.Count - 1, ImagesPerLine * 10 - 1);
        var last = _lastVisibleEndIndex < first
            ? fallbackEnd
            : Math.Min(_lastVisibleEndIndex, ImagePreviews.Count - 1);
        for (var index = first; index <= last; index++)
        {
            if (ImagePreviews[index].LoadedPreviewWidth < GetOverviewPreviewWidth())
            {
                _ = LoadImageAsync(index, _imageLoadCts.Token);
            }
        }
    }

    private void SchedulePreviewRowsRebuild()
    {
        if (_previewRowsRebuildPending)
        {
            return;
        }

        _previewRowsRebuildPending = true;
        _synchronizationContext.Post(_ =>
        {
            _previewRowsRebuildPending = false;
            RebuildPreviewRows();
        }, null);
    }
}
