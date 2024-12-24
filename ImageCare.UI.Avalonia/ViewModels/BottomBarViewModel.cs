using System;
using System.Reactive.Disposables;
using System.Windows.Input;

using ImageCare.Modules.Logging.Services;
using ImageCare.Mvvm;

using Prism.Commands;
using Prism.Regions;
using Prism.Services.Dialogs;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class BottomBarViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly ILogNotificationService _logNotificationService;
    private CompositeDisposable? _compositeDisposable;
    private int? _messagesCount;

    private int _errorsCount;
    private int _warningsCount;

    public BottomBarViewModel(IDialogService dialogService, ILogNotificationService logNotificationService)
    {
        _dialogService = dialogService;
        _logNotificationService = logNotificationService;

        OpenLogWindowCommand = new DelegateCommand(OpenLogWindow);
    }

    public int? MessagesCount
    {
        get => _messagesCount;
        set => SetProperty(ref _messagesCount, value);
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
            _logNotificationService.WarningsCountUpdated.Subscribe(OnWarningsMessagesCountUpdated)
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
}