using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.MediaBatchOperationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using MapsterMapper;

using Serilog;

using Notification = ImageCare.Core.Domain.Notification.Notification;

namespace ImageCare.UI.Avalonia.ViewModels.Preview;

/// <summary>
/// Runs copy/move/delete/export over the current panel selection and exposes the
/// progress state shown in the selection bar.
/// </summary>
internal sealed class PreviewBatchOperationsViewModel : ViewModelBase
{
    private readonly PreviewSelectionViewModel _selection;
    private readonly PreviewAnnotationEditorViewModel _annotationEditor;
    private readonly IFolderService _folderService;
    private readonly IMediaBatchOperationsService _batchOperationsService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly IConfigurationService _configurationService;
    private readonly INotificationService _notificationService;
    private readonly FileTransferCoordinator _fileTransferCoordinator;
    private readonly LocalizationManager _localization;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly Func<FileManagerPanel> _fileManagerPanel;

    private CancellationTokenSource? _batchOperationCancellation;
    private bool _isBatchOperationRunning;
    private int _batchProcessed;
    private int _batchTotal;

    public PreviewBatchOperationsViewModel(
        PreviewSelectionViewModel selection,
        PreviewAnnotationEditorViewModel annotationEditor,
        IFolderService folderService,
        IMediaBatchOperationsService batchOperationsService,
        IFileDialogService fileDialogService,
        IConfirmationDialogService confirmationDialogService,
        IConfigurationService configurationService,
        INotificationService notificationService,
        FileTransferCoordinator fileTransferCoordinator,
        LocalizationManager localization,
        IMapper mapper,
        ILogger logger,
        SynchronizationContext synchronizationContext,
        Func<FileManagerPanel> fileManagerPanel)
    {
        _selection = selection;
        _annotationEditor = annotationEditor;
        _folderService = folderService;
        _batchOperationsService = batchOperationsService;
        _fileDialogService = fileDialogService;
        _confirmationDialogService = confirmationDialogService;
        _configurationService = configurationService;
        _notificationService = notificationService;
        _fileTransferCoordinator = fileTransferCoordinator;
        _localization = localization;
        _mapper = mapper;
        _logger = logger;
        _synchronizationContext = synchronizationContext;
        _fileManagerPanel = fileManagerPanel;

        CopyImagePreviewCommand = CreateAsyncCommand(CopySelectionAsync);
        MoveImagePreviewCommand = CreateAsyncCommand(MoveSelectionAsync);
        DeleteImagePreviewCommand = CreateAsyncCommand(DeleteSelectionAsync);
        ExportSelectionCommand = CreateAsyncCommand(ExportSelectionAsync, () => CanExportSelection)
            .ObservesProperty(() => CanExportSelection);
        CancelBatchOperationCommand = CreateCommand(CancelBatchOperation, () => IsBatchOperationRunning)
            .ObservesProperty(() => IsBatchOperationRunning);
        ClearSelectionCommand = CreateCommand(ClearSelection, () => !IsBatchOperationRunning)
            .ObservesProperty(() => IsBatchOperationRunning);

        _selection.SelectionChanged += OnSelectionChanged;
        _annotationEditor.PropertyChanged += OnAnnotationEditorPropertyChanged;
    }

    public PreviewSelectionViewModel Selection => _selection;

    public ICommand CopyImagePreviewCommand { get; }

    public ICommand MoveImagePreviewCommand { get; }

    public ICommand DeleteImagePreviewCommand { get; }

    public ICommand ExportSelectionCommand { get; }

    public ICommand CancelBatchOperationCommand { get; }

    public ICommand ClearSelectionCommand { get; }

    public bool IsBatchOperationRunning
    {
        get => _isBatchOperationRunning;
        private set
        {
            if (!SetProperty(ref _isBatchOperationRunning, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(CanRunBatchOperation));
            RaisePropertyChanged(nameof(CanExportSelection));
        }
    }

    public bool CanRunBatchOperation => _selection.HasSelection && !IsBatchOperationRunning;

    public bool CanExportSelection => _selection.HasSelection &&
                                      !IsBatchOperationRunning &&
                                      !_annotationEditor.IsSavingAnnotations &&
                                      !_annotationEditor.IsAnnotationLoading &&
                                      _selection.SelectedPreviews.Any(preview =>
                                          preview.MediaFormat.SupportsAnnotations);

    public int BatchProcessed
    {
        get => _batchProcessed;
        private set
        {
            if (SetProperty(ref _batchProcessed, value))
            {
                RaisePropertyChanged(nameof(BatchProgressPercent));
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
                RaisePropertyChanged(nameof(BatchProgressPercent));
                RaisePropertyChanged(nameof(BatchProgressText));
            }
        }
    }

    public double BatchProgressPercent => BatchTotal == 0
        ? 0
        : 100.0 * BatchProcessed / BatchTotal;

    public string BatchProgressText => _localization.Get("Batch_Progress", BatchProcessed, BatchTotal);

    public async Task CopySelectionAsync()
    {
        if (!_selection.HasSelection || IsBatchOperationRunning)
        {
            return;
        }

        if (_selection.SelectionCount == 1)
        {
            await CopySinglePreviewAsync(_selection.SelectedPreviews[0]);
            return;
        }

        var targetDirectory = GetOppositeSelectedDirectory();
        if (targetDirectory == null)
        {
            NotifyBatchProblem("Batch_NoTargetFolder");
            return;
        }

        var items = GetSelectedMediaPreviews();
        await RunBatchAsync(
            MediaBatchOperationKind.Copy,
            items,
            _localization.Get("Batch_Start_Copy", items.Count, targetDirectory.Path),
            (progress, token) => _batchOperationsService.CopyAsync(
                items,
                targetDirectory.Path,
                progress,
                token));
    }

    public async Task MoveSelectionAsync()
    {
        if (!_selection.HasSelection || IsBatchOperationRunning)
        {
            return;
        }

        if (_selection.SelectionCount == 1)
        {
            await MoveSinglePreviewAsync(_selection.SelectedPreviews[0]);
            return;
        }

        var targetDirectory = GetOppositeSelectedDirectory();
        if (targetDirectory == null)
        {
            NotifyBatchProblem("Batch_NoTargetFolder");
            return;
        }

        var items = GetSelectedMediaPreviews();
        await RunBatchAsync(
            MediaBatchOperationKind.Move,
            items,
            _localization.Get("Batch_Start_Move", items.Count, targetDirectory.Path),
            (progress, token) => _batchOperationsService.MoveAsync(
                items,
                targetDirectory.Path,
                progress,
                token));
    }

    private void OnSelectionChanged(object? sender, EventArgs args) => NotifyCommandStateChanged();

    private void OnAnnotationEditorPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(PreviewAnnotationEditorViewModel.IsSavingAnnotations) or
            nameof(PreviewAnnotationEditorViewModel.IsAnnotationLoading))
        {
            NotifyCommandStateChanged();
        }
    }

    private void NotifyCommandStateChanged()
    {
        RaisePropertyChanged(nameof(CanRunBatchOperation));
        RaisePropertyChanged(nameof(CanExportSelection));
    }

    private void ClearSelection()
    {
        if (IsBatchOperationRunning)
        {
            return;
        }

        _selection.Clear();
    }

    private async Task CopySinglePreviewAsync(MediaPreviewViewModel preview)
    {
        try
        {
            var targetDirectory = GetOppositeSelectedDirectory();
            if (targetDirectory == null)
            {
                NotifyBatchProblem("Batch_NoTargetFolder");
                return;
            }

            var notificationTitle = $"{preview.Url} => {targetDirectory.Path}";
            _notificationService.SendNotification(new Notification(notificationTitle, string.Empty));
            var progress = new Progress<OperationInfo>();
            progress.ProgressChanged += (_, info) =>
                _notificationService.SendNotification(
                    new Notification(notificationTitle, info.Percentage.ToString("F1")));

            var result = await _fileTransferCoordinator.CopyAsync(
                _mapper.Map<MediaPreview>(preview),
                targetDirectory.Path,
                progress);

            switch (result)
            {
                case OperationResult.Success:
                    _notificationService.SendNotification(new SuccessNotification(notificationTitle, string.Empty));
                    break;
                case OperationResult.Failed:
                    _notificationService.SendNotification(new ErrorNotification(notificationTitle, string.Empty));
                    _logger.Error(
                        "Failed to copy image preview from {Source} to {Target}",
                        preview.Url,
                        targetDirectory.Path);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error copying image preview from {Source}", preview.Url);
            _notificationService.SendNotification(
                new ErrorNotification(_localization["Batch_FailedTitle"], ex.Message));
        }
    }

    private async Task MoveSinglePreviewAsync(MediaPreviewViewModel preview)
    {
        try
        {
            var targetDirectory = GetOppositeSelectedDirectory();
            if (targetDirectory == null)
            {
                NotifyBatchProblem("Batch_NoTargetFolder");
                return;
            }

            var notificationTitle = $"{preview.Url} => {targetDirectory.Path}";
            _notificationService.SendNotification(new Notification(notificationTitle, string.Empty));
            var progress = new Progress<OperationInfo>();
            progress.ProgressChanged += (_, info) =>
                _notificationService.SendNotification(
                    new Notification(notificationTitle, info.Percentage.ToString("F1")));

            var result = await _fileTransferCoordinator.MoveAsync(
                _mapper.Map<MediaPreview>(preview),
                targetDirectory.Path,
                progress);

            switch (result)
            {
                case OperationResult.Success:
                    _notificationService.SendNotification(new SuccessNotification(notificationTitle, string.Empty));
                    break;
                case OperationResult.Failed:
                    _notificationService.SendNotification(new ErrorNotification(notificationTitle, string.Empty));
                    _logger.Error(
                        "Failed to move image preview from {Source} to {Target}",
                        preview.Url,
                        targetDirectory.Path);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error moving image preview from {Source}", preview.Url);
            _notificationService.SendNotification(
                new ErrorNotification(_localization["Batch_FailedTitle"], ex.Message));
        }
    }

    private async Task DeleteSelectionAsync()
    {
        if (!_selection.HasSelection || IsBatchOperationRunning)
        {
            return;
        }

        var items = GetSelectedMediaPreviews();
        var safeDeletionEnabled = _configurationService.Configuration.Value.SafeDeletionEnabled;
        var message = _localization.Get("Batch_DeleteConfirmMessage", items.Count) +
                      Environment.NewLine +
                      Environment.NewLine +
                      _localization[safeDeletionEnabled
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

        await RunBatchAsync(
            MediaBatchOperationKind.Delete,
            items,
            _localization.Get("Batch_Start_Delete", items.Count),
            (progress, token) => _batchOperationsService.DeleteAsync(items, progress, token));
    }

    private async Task ExportSelectionAsync()
    {
        if (!CanExportSelection)
        {
            return;
        }

        var items = GetSelectedMediaPreviews()
            .Where(preview => preview.MediaFormat.SupportsAnnotations)
            .ToArray();
        if (items.Length == 0)
        {
            NotifyBatchProblem("Batch_NoExportableImages");
            return;
        }

        var destinationDirectory = await _fileDialogService.ShowOpenFolderDialogAsync(
            _localization["Batch_ExportFolderTitle"]);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return;
        }

        await RunBatchAsync(
            MediaBatchOperationKind.ExportJpeg,
            items,
            _localization.Get("Batch_Start_Export", items.Length, destinationDirectory),
            (progress, token) => _batchOperationsService.ExportJpegAsync(
                items,
                destinationDirectory,
                progress,
                token));
    }

    private async Task RunBatchAsync(
        MediaBatchOperationKind kind,
        IReadOnlyList<MediaPreview> items,
        string startTitle,
        Func<IProgress<MediaBatchOperationProgress>, CancellationToken, Task<MediaBatchOperationSummary>> operation)
    {
        if (items.Count == 0 || IsBatchOperationRunning)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _batchOperationCancellation = cancellation;
        BatchProcessed = 0;
        BatchTotal = items.Count;
        IsBatchOperationRunning = true;
        _notificationService.SendNotification(new Notification(startTitle, string.Empty));

        try
        {
            var progress = new Progress<MediaBatchOperationProgress>(OnBatchProgress);
            var summary = await operation(progress, cancellation.Token);
            ReportBatchSummary(kind, summary);
        }
        catch (OperationCanceledException)
        {
            _notificationService.SendNotification(
                new Notification(_localization["Batch_CancelledTitle"], string.Empty));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Batch {Operation} failed", kind);
            _notificationService.SendNotification(
                new ErrorNotification(_localization["Batch_FailedTitle"], ex.Message));
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

    private void OnBatchProgress(MediaBatchOperationProgress progress)
    {
        if (SynchronizationContext.Current == _synchronizationContext)
        {
            ApplyBatchProgress(progress);
            return;
        }

        _synchronizationContext.Post(_ => ApplyBatchProgress(progress), null);
    }

    private void ApplyBatchProgress(MediaBatchOperationProgress progress)
    {
        BatchProcessed = progress.Processed;
        BatchTotal = progress.Total;
    }

    private void CancelBatchOperation() => _batchOperationCancellation?.Cancel();

    private void ReportBatchSummary(
        MediaBatchOperationKind kind,
        MediaBatchOperationSummary summary)
    {
        var completedTitle = _localization.Get(
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

        if (summary.Cancelled)
        {
            _notificationService.SendNotification(
                new Notification(_localization["Batch_CancelledTitle"], completedTitle));
            return;
        }

        if (summary.Failed > 0)
        {
            var detail = summary.Failures.Count == 0
                ? _localization["Batch_FailedTitle"]
                : _localization.Get(
                    "Batch_FailedDetail",
                    summary.Failed,
                    summary.Failures[0].Error);
            _notificationService.SendNotification(new ErrorNotification(completedTitle, detail));
            return;
        }

        _notificationService.SendNotification(new SuccessNotification(completedTitle, string.Empty));
    }

    private void NotifyBatchProblem(string messageKey)
    {
        _notificationService.SendNotification(
            new ErrorNotification(_localization["Batch_FailedTitle"], _localization[messageKey]));
    }

    private IReadOnlyList<MediaPreview> GetSelectedMediaPreviews()
    {
        return _selection.SelectedPreviews
            .Select(_mapper.Map<MediaPreview>)
            .ToArray();
    }

    private DirectoryModel? GetOppositeSelectedDirectory()
    {
        return _folderService.GetSelectedDirectory(
            _fileManagerPanel() == FileManagerPanel.Left
                ? FileManagerPanel.Right
                : FileManagerPanel.Left);
    }
}
