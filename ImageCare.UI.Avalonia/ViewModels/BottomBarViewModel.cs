using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Domain;
using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Services.LogEventService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using MapsterMapper;

using Prism.Commands;
using Prism.Dialogs;
using Prism.Navigation.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class BottomBarViewModel : NavigatedViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly SettingsWindowService _settingsWindowService;
    private readonly MediaSearchWindowService _mediaSearchWindowService;
    private readonly INotificationService _notificationService;
    private readonly ILogNotificationService _logNotificationService;
    private readonly IMediaPreviewOperationsService _mediaPreviewOperationsService;
    private readonly FileTransferOperationState _fileTransferOperationState;
    private readonly LocalizationManager _localization;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private CompositeDisposable? _compositeDisposable;
    private int? _messagesCount;
    private bool _isDisposed;
    private bool _canUndoDelete;
    private FileTransferOperation _currentFileTransferOperation;
    private bool _isFileTransferModifierActive;

    private int _errorsCount;
    private int _warningsCount;
    private NotificationViewModel? _notificationViewModel;

    public BottomBarViewModel(
        IDialogService dialogService,
        SettingsWindowService settingsWindowService,
        MediaSearchWindowService mediaSearchWindowService,
        INotificationService notificationService,
        ILogNotificationService logNotificationService,
        IMediaPreviewOperationsService mediaPreviewOperationsService,
        FileTransferOperationState fileTransferOperationState,
        LocalizationManager localization,
        IMapper mapper,
        ILogger logger,
        SynchronizationContext synchronizationContext)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _settingsWindowService = settingsWindowService ?? throw new ArgumentNullException(nameof(settingsWindowService));
        _mediaSearchWindowService = mediaSearchWindowService ?? throw new ArgumentNullException(nameof(mediaSearchWindowService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _logNotificationService = logNotificationService ?? throw new ArgumentNullException(nameof(logNotificationService));
        _mediaPreviewOperationsService = mediaPreviewOperationsService ??
            throw new ArgumentNullException(nameof(mediaPreviewOperationsService));
        _fileTransferOperationState = fileTransferOperationState ??
            throw new ArgumentNullException(nameof(fileTransferOperationState));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));

        ApplyFileTransferStatus(_fileTransferOperationState.CurrentStatus);
        OpenLogWindowCommand = new DelegateCommand(OpenLogWindow, CanOpenLogWindow)
            .ObservesProperty(() => MessagesCount);
        OpenSettingsWindowCommand = new DelegateCommand(OpenSettingsWindow);
        OpenMediaSearchWindowCommand = new DelegateCommand(OpenMediaSearchWindow);
        UndoDeleteCommand = CreateAsyncCommand(UndoDeleteAsync, () => CanUndoDelete)
            .ObservesProperty(() => CanUndoDelete);
    }

    public int? MessagesCount
    {
        get => _messagesCount;
        private set => SetProperty(ref _messagesCount, value);
    }

    public NotificationViewModel? NotificationViewModel
    {
        get => _notificationViewModel;
        private set => SetProperty(ref _notificationViewModel, value);
    }

    public bool CanUndoDelete
    {
        get => _canUndoDelete;
        private set => SetProperty(ref _canUndoDelete, value);
    }

    public bool IsCopyFileTransferOperation =>
        _currentFileTransferOperation == FileTransferOperation.Copy;

    public bool IsMoveFileTransferOperation =>
        _currentFileTransferOperation == FileTransferOperation.Move;

    public bool IsFileTransferModifierActive
    {
        get => _isFileTransferModifierActive;
        private set => SetProperty(ref _isFileTransferModifierActive, value);
    }

    public ICommand OpenLogWindowCommand { get; }

    public ICommand OpenSettingsWindowCommand { get; }

    public ICommand OpenMediaSearchWindowCommand { get; }

    public ICommand UndoDeleteCommand { get; }

    public override void OnNavigatedTo(NavigationContext navigationContext)
    {
        ThrowIfDisposed();

        try
        {
            base.OnNavigatedTo(navigationContext);

            _errorsCount = _logNotificationService.GetErrorsCount();
            _warningsCount = _logNotificationService.GetWarningsCount();
            UpdateMessagesCount();
            CanUndoDelete = _mediaPreviewOperationsService.CanUndoDelete;

            _compositeDisposable = new CompositeDisposable
            {
                _logNotificationService.ErrorsCountUpdated
                    .Subscribe(OnErrorsMessagesCountUpdated, OnError),
                _logNotificationService.WarningsCountUpdated
                    .Subscribe(OnWarningsMessagesCountUpdated, OnError),
                _notificationService.NotificationReceived
                    .ObserveOn(_synchronizationContext)
                    .Subscribe(OnNotificationReceived, OnError),
                _mediaPreviewOperationsService.DeletedFilesChanged
                    .ObserveOn(_synchronizationContext)
                    .Subscribe(files => CanUndoDelete = files.Count > 0, OnError),
                _fileTransferOperationState.StatusChanged
                    .ObserveOn(_synchronizationContext)
                    .Subscribe(ApplyFileTransferStatus, OnError)
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to navigate to BottomBarViewModel");
        }
    }

    public override void OnNavigatedFrom(NavigationContext navigationContext)
    {
        try
        {
            base.OnNavigatedFrom(navigationContext);
            SafeDispose(ref _compositeDisposable);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to navigate from BottomBarViewModel");
        }
    }

    private async Task UndoDeleteAsync()
    {
        try
        {
            var result = await _mediaPreviewOperationsService.UndoLastDeleteAsync();
            CanUndoDelete = _mediaPreviewOperationsService.CanUndoDelete;

            _notificationService.SendNotification(
                result == OperationResult.Success
                    ? new SuccessNotification(_localization["Delete_Restored"], string.Empty)
                    : new ErrorNotification(_localization["Delete_RestoreFailed"], _localization["Delete_RestoreConflict"]));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to undo the last deletion");
            CanUndoDelete = _mediaPreviewOperationsService.CanUndoDelete;
            _notificationService.SendNotification(
                new ErrorNotification(_localization["Delete_RestoreFailed"], ex.Message));
        }
    }

    private void OpenLogWindow()
    {
        try
        {
            var parameters = new DialogParameters();
            _dialogService.Show(
                "logViewer",
                parameters,
                null,
                "childWindow");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open log window");
        }
    }

    private void OpenMediaSearchWindow()
    {
        try
        {
            _mediaSearchWindowService.Show();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open media search window");
        }
    }

    private void OpenSettingsWindow()
    {
        try
        {
            _settingsWindowService.Show();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open settings window");
        }
    }

    private bool CanOpenLogWindow()
    {
        try
        {
            return MessagesCount > 0;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in CanOpenLogWindow");
            return false;
        }
    }

    private void UpdateMessagesCount()
    {
        try
        {
            var total = _errorsCount + _warningsCount;
            MessagesCount = total > 0 ? total : null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update messages count");
            MessagesCount = null;
        }
    }

    private void OnErrorsMessagesCountUpdated(int errorsCount)
    {
        try
        {
            _errorsCount = errorsCount;
            UpdateMessagesCount();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to process errors count update");
        }
    }

    private void OnWarningsMessagesCountUpdated(int warningsCount)
    {
        try
        {
            _warningsCount = warningsCount;
            UpdateMessagesCount();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to process warnings count update");
        }
    }

    private void OnNotificationReceived(Notification? notification)
    {
        try
        {
            if (notification == null)
            {
                _logger.Warning("Received null notification");
                return;
            }

            NotificationViewModel = _mapper.Map<NotificationViewModel>(notification);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to process notification");
        }
    }

    private void ApplyFileTransferStatus(FileTransferOperationStatus status)
    {
        if (_currentFileTransferOperation != status.Operation)
        {
            _currentFileTransferOperation = status.Operation;
            RaisePropertyChanged(nameof(IsCopyFileTransferOperation));
            RaisePropertyChanged(nameof(IsMoveFileTransferOperation));
        }

        IsFileTransferModifierActive = status.IsModifierActive;
    }

    private void OnFileOperationCompleted(MediaFileOperationCompleted operation)
    {
        if (operation.Kind is MediaFileOperationKind.Delete or MediaFileOperationKind.Restore)
        {
            CanUndoDelete = _mediaPreviewOperationsService.CanUndoDelete;
        }
    }

    private void OnError(Exception exception)
    {
        _logger.Error(exception, "Error in observable subscription");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private void SafeDispose<T>(ref T? disposable) where T : IDisposable?
    {
        try
        {
            disposable?.Dispose();
            disposable = default;
            _isDisposed = true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to dispose {Type}", typeof(T).Name);
        }
    }
}
