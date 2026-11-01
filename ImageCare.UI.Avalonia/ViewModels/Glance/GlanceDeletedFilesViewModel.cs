using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Glance;

/// <summary>
/// Owns the "deleted files" tab: the safe-delete buffer list, preview loading and restore
/// commands. Restoring reuses <see cref="IMediaPreviewOperationsService"/> so the deletion
/// manifest stays authoritative.
/// </summary>
internal sealed class GlanceDeletedFilesViewModel : ViewModelBase
{
    private readonly IMediaPreviewOperationsService _fileOperationsService;
    private readonly IMediaPreviewService _imageService;
    private readonly INotificationService _notificationService;
    private readonly LocalizationManager _localization;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;

    private CompositeDisposable? _subscriptions;
    private bool _hasDeletedFiles;
    private bool _isRestoringAll;

    public GlanceDeletedFilesViewModel(
        IMediaPreviewOperationsService fileOperationsService,
        IMediaPreviewService imageService,
        INotificationService notificationService,
        LocalizationManager localization,
        ILogger logger,
        SynchronizationContext synchronizationContext)
    {
        _fileOperationsService = fileOperationsService;
        _imageService = imageService;
        _notificationService = notificationService;
        _localization = localization;
        _logger = logger;
        _synchronizationContext = synchronizationContext;

        RestoreAllCommand = CreateAsyncCommand(
                RestoreAllAsync,
                () => HasDeletedFiles && !IsRestoringAll)
            .ObservesProperty(() => HasDeletedFiles)
            .ObservesProperty(() => IsRestoringAll);
    }

    public ObservableCollection<DeletedMediaFileViewModel> DeletedFiles { get; } = new();

    public bool HasDeletedFiles
    {
        get => _hasDeletedFiles;
        private set => SetProperty(ref _hasDeletedFiles, value);
    }

    public bool IsRestoringAll
    {
        get => _isRestoringAll;
        private set => SetProperty(ref _isRestoringAll, value);
    }

    public ICommand RestoreAllCommand { get; }

    public void Start()
    {
        _subscriptions?.Dispose();
        _subscriptions = new CompositeDisposable
        {
            _fileOperationsService.DeletedFilesChanged
                                  .ObserveOn(_synchronizationContext)
                                  .Subscribe(RefreshDeletedFiles)
        };
    }

    public void Stop()
    {
        _subscriptions?.Dispose();
        _subscriptions = null;
        ClearDeletedFiles();
    }

    private void RefreshDeletedFiles(IReadOnlyList<DeletedMediaFile> deletedFiles)
    {
        ClearDeletedFiles();
        foreach (var deletedFile in deletedFiles)
        {
            var viewModel = new DeletedMediaFileViewModel(
                deletedFile,
                RestoreDeletedFileAsync,
                _imageService,
                _logger);
            DeletedFiles.Add(viewModel);
            _ = viewModel.LoadPreviewAsync();
        }

        HasDeletedFiles = DeletedFiles.Count > 0;
    }

    private void ClearDeletedFiles()
    {
        foreach (var deletedFile in DeletedFiles)
        {
            deletedFile.Dispose();
        }

        DeletedFiles.Clear();
        HasDeletedFiles = false;
    }

    private async Task RestoreDeletedFileAsync(DeletedMediaFile deletedFile)
    {
        try
        {
            var result = await _fileOperationsService.RestoreDeletedFileAsync(deletedFile.BufferPath);
            if (result == OperationResult.Success)
            {
                _notificationService.SendNotification(
                    new SuccessNotification(
                        _localization["Delete_Restored"],
                        System.IO.Path.GetFileName(deletedFile.OriginalPath)));
            }
            else
            {
                _notificationService.SendNotification(
                    new ErrorNotification(
                        _localization["Delete_RestoreFailed"],
                        _localization["Delete_RestoreConflict"]));
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to restore deleted file {Path}", deletedFile.OriginalPath);
            _notificationService.SendNotification(
                new ErrorNotification(_localization["Delete_RestoreFailed"], ex.Message));
        }
    }

    private async Task RestoreAllAsync()
    {
        try
        {
            IsRestoringAll = true;
            foreach (var deletedFile in _fileOperationsService.GetDeletedFiles().ToArray())
            {
                await RestoreDeletedFileAsync(deletedFile);
            }
        }
        finally
        {
            IsRestoringAll = false;
        }
    }
}
