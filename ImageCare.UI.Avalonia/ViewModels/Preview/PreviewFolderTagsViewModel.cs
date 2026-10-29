using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;

using Serilog;

using Notification = ImageCare.Core.Domain.Notification.Notification;

namespace ImageCare.UI.Avalonia.ViewModels.Preview;

/// <summary>
/// Applies the folder-tag dictionary to every media file in the current folder and
/// exposes the busy state of that operation.
/// </summary>
internal sealed class PreviewFolderTagsViewModel : ViewModelBase, IDisposable
{
    private readonly IFolderTagApplicationService _folderTagApplicationService;
    private readonly PreviewImageStripViewModel _mediaStrip;
    private readonly INotificationService _notificationService;
    private readonly LocalizationManager _localization;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly Func<CancellationToken> _folderToken;
    private readonly Func<string, CancellationToken, Task> _reconcileAsync;

    private CompositeDisposable? _subscriptions;
    private CancellationTokenSource? _folderTagCancellation;
    private bool _isApplyingFolderTags;

    public PreviewFolderTagsViewModel(
        IFolderTagApplicationService folderTagApplicationService,
        PreviewImageStripViewModel mediaStrip,
        INotificationService notificationService,
        LocalizationManager localization,
        ILogger logger,
        SynchronizationContext synchronizationContext,
        Func<CancellationToken> folderToken,
        Func<string, CancellationToken, Task> reconcileAsync)
    {
        _folderTagApplicationService = folderTagApplicationService;
        _mediaStrip = mediaStrip;
        _notificationService = notificationService;
        _localization = localization;
        _logger = logger;
        _synchronizationContext = synchronizationContext;
        _folderToken = folderToken;
        _reconcileAsync = reconcileAsync;

        ApplyFolderTagsCommand = CreateAsyncCommand(ApplyFolderTagsAsync, () => CanApplyFolderTags)
            .ObservesProperty(() => CanApplyFolderTags);

        _mediaStrip.PropertyChanged += OnMediaStripPropertyChanged;
        _mediaStrip.ImagePreviews.CollectionChanged += OnPreviewsChanged;
    }

    public ICommand ApplyFolderTagsCommand { get; }

    public bool IsApplyingFolderTags
    {
        get => _isApplyingFolderTags;
        private set
        {
            if (SetProperty(ref _isApplyingFolderTags, value))
            {
                RaisePropertyChanged(nameof(CanApplyFolderTags));
            }
        }
    }

    public bool CanApplyFolderTags => !IsApplyingFolderTags &&
                                      !_mediaStrip.FilesLoading &&
                                      !string.IsNullOrWhiteSpace(_mediaStrip.SelectedFolderPath) &&
                                      _mediaStrip.ImagePreviews.Count > 0;

    public void Start()
    {
        _subscriptions?.Dispose();
        _subscriptions = new CompositeDisposable
        {
            _folderTagApplicationService.ApplicationStatus
                                        .ObserveOn(_synchronizationContext)
                                        .Subscribe(OnFolderTagApplicationStatusChanged)
        };
    }

    public void Stop()
    {
        _subscriptions?.Dispose();
        _subscriptions = null;
        _folderTagCancellation?.Cancel();
        _folderTagCancellation?.Dispose();
        _folderTagCancellation = null;
    }

    public void Dispose()
    {
        _mediaStrip.PropertyChanged -= OnMediaStripPropertyChanged;
        _mediaStrip.ImagePreviews.CollectionChanged -= OnPreviewsChanged;
        Stop();
    }

    private void OnMediaStripPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(PreviewImageStripViewModel.FilesLoading) or
            nameof(PreviewImageStripViewModel.SelectedFolderPath))
        {
            RaisePropertyChanged(nameof(CanApplyFolderTags));
        }
    }

    private void OnPreviewsChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        RaisePropertyChanged(nameof(CanApplyFolderTags));

    private async Task ApplyFolderTagsAsync()
    {
        var folderPath = _mediaStrip.SelectedFolderPath;
        if (string.IsNullOrWhiteSpace(folderPath) || !CanApplyFolderTags)
        {
            return;
        }

        _folderTagCancellation?.Cancel();
        _folderTagCancellation?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_folderToken());
        _folderTagCancellation = cancellation;

        IsApplyingFolderTags = true;
        try
        {
            await _folderTagApplicationService.ApplyToDirectoryAsync(folderPath, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            await _reconcileAsync(folderPath, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is surfaced through the tray tooltip, which clears itself.
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to apply folder tags to {Folder}", folderPath);
            _notificationService.SendNotification(new Notification(
                _localization["FolderTags_Failed"],
                exception.Message));
        }
        finally
        {
            if (ReferenceEquals(_folderTagCancellation, cancellation))
            {
                _folderTagCancellation = null;
            }

            cancellation.Dispose();
            IsApplyingFolderTags = false;
        }
    }

    private void OnFolderTagApplicationStatusChanged(FolderTagApplicationStatus status)
    {
        // Folder-tag progress and the completion summary are surfaced in the bottom information
        // bar (the same place copy/move/delete notifications appear), so the user always sees
        // the outcome without hovering anything.
        switch (status.State)
        {
            case FolderTagApplicationState.Running:
                IsApplyingFolderTags = true;
                _notificationService.SendNotification(new Notification(
                    _localization["FolderTags_ProgressTitle"],
                    string.Format(
                        _localization["FolderTags_ProgressDescriptionFormat"],
                        status.ProcessedFiles,
                        status.TotalFiles)));
                break;
            case FolderTagApplicationState.Completed:
                IsApplyingFolderTags = false;
                if (status.ProcessedFiles > 0)
                {
                    var description = string.Format(
                        _localization["FolderTags_CompletedDescriptionFormat"],
                        status.UpdatedFiles,
                        status.SkippedFiles,
                        status.FailedFiles);
                    _notificationService.SendNotification(status.FailedFiles > 0
                        ? new ErrorNotification(_localization["FolderTags_CompletedTitle"], description)
                        : new SuccessNotification(_localization["FolderTags_CompletedTitle"], description));
                }

                break;
            case FolderTagApplicationState.Cancelled:
                IsApplyingFolderTags = false;
                break;
            case FolderTagApplicationState.Failed:
                IsApplyingFolderTags = false;
                _notificationService.SendNotification(new ErrorNotification(
                    _localization["FolderTags_Failed"],
                    status.Error ?? _mediaStrip.SelectedFolderPath));
                break;
        }
    }
}
