using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using DynamicData;
using DynamicData.Binding;

using ImageCare.Modules.Logging.Models;
using ImageCare.Modules.Logging.Services;
using ImageCare.Mvvm;

using Prism.Services.Dialogs;

using Serilog;

namespace ImageCare.Modules.Logging.ViewModels;

internal sealed class LogViewerViewModel : ViewModelBase, IDialogAware
{
    private readonly ILogEventService _logEventService;
    private readonly ILogger _logger;
    private readonly ReadOnlyObservableCollection<LogMessageViewModel> _messageViewModels;
    private readonly SourceList<LogMessageViewModel> _sourceList = new();

    private readonly Subject<string> _filterChanged;
    private CompositeDisposable _compositeDisposable;
    private bool _showWarnings = true;
    private bool _showErrors = true;

    public LogViewerViewModel(ILogEventService logEventService, ILogger logger)
    {
        _logEventService = logEventService;
        _logger = logger;
        _filterChanged = new Subject<string>();

        var filter = _filterChanged.AsObservable().Select(BuildFilter);

        _sourceList.Connect()
                   .Filter(filter)
                   .Sort(SortExpressionComparer<LogMessageViewModel>.Descending(m => m.Timestamp))
                   .Bind(out _messageViewModels)
                   .Subscribe();
        _filterChanged.OnNext(string.Empty);

        FilterErrors = new AsyncRelayCommand(fe);
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

    public ICommand FilterErrors { get; }

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
            _logEventService.MessagesCleared.Subscribe(OnMessagesCleared)
        };

        foreach (var message in _logEventService.GetLastErrors())
        {
            _sourceList.Add(new ErrorLogMessageViewModel(message.Timestamp, message.Message, message.ExceptionMessage));
        }

        foreach (var message in _logEventService.GetLastWarnings())
        {
            _sourceList.Add(new WarningLogMessageViewModel(message.Timestamp, message.Message, message.ExceptionMessage));
        }
    }

    /// <inheritdoc />
    public void OnDialogClosed()
    {
        _compositeDisposable.Dispose();
    }

    /// <inheritdoc />
    public event Action<IDialogResult>? RequestClose;

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

    private async Task fe()
    {
        await Task.Run(
            () =>
            {
                for (var index = 0; index < 20; index++)
                {
                    _logger.Error(new InvalidOperationException($"emessage{index}"), index.ToString());
                }
            });
        await Task.Run(
            () =>
            {
                for (var index = 0; index < 20; index++)
                {
                    _logger.Warning(new InvalidOperationException($"wmessage{index}"), index.ToString());
                }
            });
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
}