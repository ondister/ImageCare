using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Windows.Input;

using AutoMapper;

using ImageCare.Core.Services.NotificationService;
using ImageCare.Modules.Logging.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Prism.Commands;
using Prism.Regions;
using Prism.Services.Dialogs;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class BottomBarViewModel : NavigatedViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly INotificationService _notificationService;
    private readonly ILogNotificationService _logNotificationService;
    private readonly IMapper _mapper;
    private readonly SynchronizationContext _synchronizationContext;
    private CompositeDisposable? _compositeDisposable;
    private int? _messagesCount;

    private int _errorsCount;
    private int _warningsCount;
    private NotificationViewModel? _notificationViewModel;

    public BottomBarViewModel(IDialogService dialogService,
                              INotificationService notificationService,
                              ILogNotificationService logNotificationService,
                              IMapper mapper,
                              SynchronizationContext synchronizationContext)
    {
        _dialogService = dialogService;
        _notificationService = notificationService;
        _logNotificationService = logNotificationService;
        _mapper = mapper;
        _synchronizationContext = synchronizationContext;

        OpenLogWindowCommand = new DelegateCommand(OpenLogWindow);
    }

    public int? MessagesCount
    {
        get => _messagesCount;
        set => SetProperty(ref _messagesCount, value);
    }

    public NotificationViewModel? NotificationViewModel
    {
        get => _notificationViewModel;
        set => SetProperty(ref _notificationViewModel, value);
    }

    public ICommand OpenLogWindowCommand { get; }

    /// <inheritdoc />
    public override void OnNavigatedTo(NavigationContext navigationContext)
    {
        base.OnNavigatedTo(navigationContext);

        _errorsCount = _logNotificationService.GetErrorsCount();
        _warningsCount = _logNotificationService.GetWarningsCount();
        UpdateMessagesCount();

        _compositeDisposable = new CompositeDisposable
        {
            _logNotificationService.ErrorsCountUpdated.Subscribe(OnErrorsMessagesCountUpdated),
            _logNotificationService.WarningsCountUpdated.Subscribe(OnWarningsMessagesCountUpdated),
            _notificationService.NotificationReceived.ObserveOn(_synchronizationContext).Subscribe(OnNotificationReceived)
        };
    }

    /// <inheritdoc />
    public override void OnNavigatedFrom(NavigationContext navigationContext)
    {
        base.OnNavigatedFrom(navigationContext);

        _compositeDisposable?.Dispose();
    }

    private void OpenLogWindow()
    {
        IDialogParameters param = new DialogParameters();
        _dialogService.Show("logViewer", param, _ => { }, "childWindow");
    }

    private void UpdateMessagesCount()
    {
        MessagesCount = _errorsCount + _warningsCount;
        if (MessagesCount == 0)
        {
            MessagesCount = null;
        }
    }

    private void OnErrorsMessagesCountUpdated(int errorsCount)
    {
        _errorsCount = errorsCount;

        UpdateMessagesCount();
    }

    private void OnWarningsMessagesCountUpdated(int warningsCount)
    {
        _warningsCount = warningsCount;

        UpdateMessagesCount();
    }

    private void OnNotificationReceived(Notification notification)
    {
        NotificationViewModel = _mapper.Map<NotificationViewModel>(notification);
    }
}