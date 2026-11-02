using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
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
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Glance;

/// <summary>
/// Runs copy/move/delete/JPEG-export over the current overview selection and exposes the
/// transfer destination shown in the selection bar.
/// </summary>
internal sealed class GlanceBatchOperationsViewModel : ViewModelBase
{
    private readonly GlanceSelectionViewModel _selection;
    private readonly IFolderService _folderService;
    private readonly IMediaBatchOperationsService _batchOperationsService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly IConfigurationService _configurationService;
    private readonly INotificationService _notificationService;
    private readonly MediaBatchOperationRunner _batchOperationRunner;
    private readonly LocalizationManager _localization;
    private readonly IMapper _mapper;
    private readonly GlanceOperationViewModel _operations;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly Func<FileManagerPanel> _overviewPanel;

    private CompositeDisposable? _subscriptions;

    public GlanceBatchOperationsViewModel(
        GlanceSelectionViewModel selection,
        IFolderService folderService,
        IMediaBatchOperationsService batchOperationsService,
        IFileDialogService fileDialogService,
        IConfirmationDialogService confirmationDialogService,
        IConfigurationService configurationService,
        INotificationService notificationService,
        LocalizationManager localization,
        IMapper mapper,
        ILogger logger,
        GlanceOperationViewModel operations,
        SynchronizationContext synchronizationContext,
        Func<FileManagerPanel> overviewPanel)
    {
        _selection = selection;
        _folderService = folderService;
        _batchOperationsService = batchOperationsService;
        _fileDialogService = fileDialogService;
        _confirmationDialogService = confirmationDialogService;
        _configurationService = configurationService;
        _notificationService = notificationService;
        _batchOperationRunner = new MediaBatchOperationRunner(notificationService, localization, logger);
        _localization = localization;
        _mapper = mapper;
        _operations = operations;
        _synchronizationContext = synchronizationContext;
        _overviewPanel = overviewPanel;

        CopySelectionCommand = CreateAsyncCommand(CopySelectionAsync);
        MoveSelectionCommand = CreateAsyncCommand(MoveSelectionAsync);
        DeleteSelectionCommand = CreateAsyncCommand(DeleteSelectionAsync);
        ExportSelectionCommand = CreateAsyncCommand(ExportSelectionAsync);

        _selection.SelectionChanged += OnSelectionChanged;
        _operations.PropertyChanged += OnOperationsPropertyChanged;
    }

    public GlanceSelectionViewModel Selection => _selection;

    public GlanceOperationViewModel Operations => _operations;

    public ICommand CopySelectionCommand { get; }

    public ICommand MoveSelectionCommand { get; }

    public ICommand DeleteSelectionCommand { get; }

    public ICommand ExportSelectionCommand { get; }

    public bool CanRunOverviewOperation =>
        _selection.HasSelection && !_operations.IsOverviewOperationRunning;

    public bool CanTransferSelection => CanRunOverviewOperation && HasTransferDestination;

    public bool CanExportSelection =>
        CanRunOverviewOperation &&
        _selection.SelectedPreviews.Any(preview => preview.MediaFormat.SupportsAnnotations);

    public string TransferDestinationPath => GetTransferDestination()?.Path ?? string.Empty;

    public bool HasTransferDestination => !string.IsNullOrWhiteSpace(TransferDestinationPath);

    public string TransferDestinationText => HasTransferDestination
        ? _localization.Get("Overview_TransferDestination", TransferDestinationPath)
        : _localization["Batch_NoTargetFolder"];

    public string CopySelectionToolTip => HasTransferDestination
        ? _localization.Get("Overview_CopyTo", TransferDestinationPath)
        : _localization["Batch_NoTargetFolder"];

    public string MoveSelectionToolTip => HasTransferDestination
        ? _localization.Get("Overview_MoveTo", TransferDestinationPath)
        : _localization["Batch_NoTargetFolder"];

    public void Start()
    {
        _subscriptions?.Dispose();
        _subscriptions = new CompositeDisposable
        {
            _folderService.FileSystemItemSelected
                          .ObserveOn(_synchronizationContext)
                          .Subscribe(OnFolderSelectionChanged)
        };
        RefreshTransferDestination();
    }

    public void Stop()
    {
        _subscriptions?.Dispose();
        _subscriptions = null;
    }

    public void RefreshTransferDestination() => RaiseTransferDestinationProperties();

    private void OnSelectionChanged(object? sender, EventArgs args) => RaiseOperationProperties();

    private void OnOperationsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(GlanceOperationViewModel.IsOverviewOperationRunning) or
            nameof(GlanceOperationViewModel.IsBatchOperationRunning) or
            nameof(GlanceOperationViewModel.IsAnnotationOperationRunning))
        {
            RaiseOperationProperties();
        }
    }

    private void OnFolderSelectionChanged(SelectedDirectory selectedDirectory)
    {
        if (selectedDirectory.FileManagerPanel == GetTransferTargetPanel())
        {
            RefreshTransferDestination();
        }
    }

    private void RaiseOperationProperties()
    {
        RaisePropertyChanged(nameof(CanRunOverviewOperation));
        RaisePropertyChanged(nameof(CanTransferSelection));
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

    private Task CopySelectionAsync() => RunTransferAsync(MediaBatchOperationKind.Copy);

    private Task MoveSelectionAsync() => RunTransferAsync(MediaBatchOperationKind.Move);

    private async Task RunTransferAsync(MediaBatchOperationKind kind)
    {
        if (!CanRunOverviewOperation)
        {
            return;
        }

        var targetDirectory = GetTransferDestination();
        if (targetDirectory == null || string.IsNullOrWhiteSpace(targetDirectory.Path))
        {
            NotifyProblem("Batch_NoTargetFolder");
            return;
        }

        var items = GetSelectedMediaPreviews();
        await RunBatchAsync(
            kind,
            items,
            kind == MediaBatchOperationKind.Copy
                ? _localization.Get("Batch_Start_Copy", items.Count, targetDirectory.Path)
                : _localization.Get("Batch_Start_Move", items.Count, targetDirectory.Path),
            (progress, token) => kind == MediaBatchOperationKind.Copy
                ? _batchOperationsService.CopyAsync(items, targetDirectory.Path, progress, token)
                : _batchOperationsService.MoveAsync(items, targetDirectory.Path, progress, token));
    }

    private async Task DeleteSelectionAsync()
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
        var destination = await _fileDialogService.ShowOpenFolderDialogAsync(
            _localization["Batch_ExportFolderTitle"]);
        if (string.IsNullOrWhiteSpace(destination))
        {
            return;
        }

        await RunBatchAsync(
            MediaBatchOperationKind.ExportJpeg,
            items,
            _localization.Get("Batch_Start_Export", items.Length, destination),
            (progress, token) => _batchOperationsService.ExportJpegAsync(
                items,
                destination,
                progress,
                token));
    }

    private async Task RunBatchAsync(
        MediaBatchOperationKind kind,
        IReadOnlyList<MediaPreview> items,
        string startTitle,
        Func<IProgress<MediaBatchOperationProgress>, CancellationToken, Task<MediaBatchOperationSummary>> operation)
    {
        if (items.Count == 0 || _operations.IsOverviewOperationRunning)
        {
            return;
        }

        var cancellation = _operations.BeginBatchOperation(items.Count, startTitle);
        try
        {
            await _batchOperationRunner.RunAsync(
                kind,
                startTitle,
                cancellation.Token,
                progress => _operations.ReportProgress(progress.Processed, progress.Total),
                operation,
                _operations.SetStatus,
                "Overview batch {Operation} failed");
        }
        finally
        {
            _operations.CompleteBatchOperation(cancellation);
        }
    }

    private IReadOnlyList<MediaPreview> GetSelectedMediaPreviews() =>
        _selection.SelectedPreviews.Select(_mapper.Map<MediaPreview>).ToArray();

    private void NotifyProblem(string key) =>
        _notificationService.SendNotification(
            new ErrorNotification(_localization["Batch_FailedTitle"], _localization[key]));

    private FileManagerPanel GetTransferTargetPanel() => _overviewPanel() switch
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
}
