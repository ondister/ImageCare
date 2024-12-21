using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Input;

using DynamicData;
using DynamicData.Binding;

using ImageCare.Modules.Logging.Models;
using ImageCare.Modules.Logging.Services;
using ImageCare.Mvvm;

using Prism.Commands;
using Prism.Services.Dialogs;

namespace ImageCare.Modules.Logging.ViewModels;

internal sealed class LogViewerViewModel : ViewModelBase, IDialogAware, IDisposable
{
    private readonly ILogEventService _logEventService;
    private readonly ILogNotificationService _logNotificationService;
    private readonly ReadOnlyObservableCollection<LogMessageViewModel> _messageViewModels;
    private readonly SourceList<LogMessageViewModel> _sourceList = new();

    private readonly Subject<string> _filterChanged;
    private CompositeDisposable _compositeDisposable;

    private bool _showWarnings = true;
    private bool _showErrors = true;
    private int _errorsCount;
    private int _warningsCount;

    public LogViewerViewModel(ILogEventService logEventService, ILogNotificationService logNotificationService)
    {
        _logEventService = logEventService;
        _logNotificationService = logNotificationService;

        _filterChanged = new Subject<string>();

        var filter = _filterChanged.AsObservable().Select(BuildFilter);

        _sourceList.Connect()
                   .Filter(filter)
                   .Sort(SortExpressionComparer<LogMessageViewModel>.Descending(m => m.Timestamp))
                   .Bind(out _messageViewModels)
                   .Subscribe();
        _filterChanged.OnNext(string.Empty);

        ClearMessagesCommand = new DelegateCommand(ClearMessages, CanClearMessages).ObservesProperty(() => MessageViewModels.Count);
    }

    public bool ShowWarnings
    {
        get => _showWarnings;
        set
        {
            if (SetProperty(ref _showWarnings, value))
            {
                _filterChanged.OnNext(nameof(ShowWarnings));
            }
        }
    }

    public bool ShowErrors
    {
        get => _showErrors;
        set
        {
            if (SetProperty(ref _showErrors, value))
            {
                _filterChanged.OnNext(nameof(ShowErrors));
            }
        }
    }

    public int ErrorsCount
    {
        get => _errorsCount;
        set => SetProperty(ref _errorsCount, value);
    }

    public int WarningsCount
    {
        get => _warningsCount;
        set => SetProperty(ref _warningsCount, value);
    }

    public ICommand ClearMessagesCommand { get; }

    public ReadOnlyObservableCollection<LogMessageViewModel> MessageViewModels => _messageViewModels;

    /// <inheritdoc />
    public string Title { get; } = "Log messages";

    /// <inheritdoc />
    public bool CanCloseDialog()
    {
        return true;
    }

    /// <inheritdoc />
    public void OnDialogOpened(IDialogParameters parameters)
    {
        _compositeDisposable = new CompositeDisposable
        {
            _logEventService.ErrorReceived.Subscribe(OnErrorReceived),
            _logEventService.WarningReceived.Subscribe(OnWarningReceived),
            _logEventService.MessagesCleared.Subscribe(OnMessagesCleared),

            _logNotificationService.ErrorsCountUpdated.Subscribe(OnErrorsCountUpdated),
            _logNotificationService.WarningsCountUpdated.Subscribe(OnWarningsCountUpdated)
        };

        var errors = _logEventService.GetLastErrors().ToList();
        foreach (var message in errors)
        {
            _sourceList.Add(new ErrorLogMessageViewModel(message.Timestamp, message.Message, message.ExceptionMessage));
        }

        ErrorsCount = errors.Count;

        var warnings = _logEventService.GetLastWarnings().ToList();
        foreach (var message in warnings)
        {
            _sourceList.Add(new WarningLogMessageViewModel(message.Timestamp, message.Message, message.ExceptionMessage));
        }

        WarningsCount = warnings.Count;
    }

    /// <inheritdoc />
    public void OnDialogClosed()
    {
        _compositeDisposable.Dispose();
    }

    /// <inheritdoc />
    public event Action<IDialogResult>? RequestClose;

    /// <inheritdoc />
    public void Dispose()
    {
        _sourceList.Dispose();
        _filterChanged.Dispose();
        _compositeDisposable.Dispose();
    }

    private Func<LogMessageViewModel, bool> BuildFilter(string filterPropertyName)
    {
        return m =>
        {
            if (ShowErrors && ShowWarnings)
            {
                return true;
            }

            if (ShowErrors && !ShowWarnings)
            {
                return m is ErrorLogMessageViewModel;
            }

            if (!ShowErrors && ShowWarnings)
            {
                return m is WarningLogMessageViewModel;
            }

            return false;
        };
    }

    private void OnErrorReceived(LogMessage message)
    {
        _sourceList.Add(new ErrorLogMessageViewModel(message.Timestamp, message.Message, message.ExceptionMessage));
    }

    private void OnWarningReceived(LogMessage message)
    {
        _sourceList.Add(new WarningLogMessageViewModel(message.Timestamp, message.Message, message.ExceptionMessage));
    }

    private void OnMessagesCleared(bool obj)
    {
        _sourceList.Clear();
    }

    private void OnErrorsCountUpdated(int errorsCount)
    {
        ErrorsCount = errorsCount;
    }

    private void OnWarningsCountUpdated(int warningsCount)
    {
        WarningsCount = warningsCount;
    }

    private bool CanClearMessages()
    {
        return MessageViewModels.Count != 0;
    }

    private void ClearMessages()
    {
        _logEventService.ClearMessages();
    }
}