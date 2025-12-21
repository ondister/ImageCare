using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Input;

using DynamicData;
using DynamicData.Binding;
using ImageCare.Core.Domain.Logs;
using ImageCare.Core.Services.LogEventService;
using ImageCare.UI.Avalonia.ViewModels.Domain.Logs;

using MapsterMapper;

using Prism.Dialogs;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal sealed class LogViewerViewModel : ViewModelBase, IDialogAware, IDisposable
{
    private readonly ILogEventService _logEventService;
    private readonly ILogNotificationService _logNotificationService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly ReadOnlyObservableCollection<LogMessageViewModel> _messageViewModels;
    private readonly SourceList<LogMessageViewModel> _sourceList = new();
    private readonly Subject<string> _filterChanged;
    private CompositeDisposable _compositeDisposable;
    private bool _isDisposed;

    private bool _showWarnings = true;
    private bool _showErrors = true;
    private int _errorsCount;
    private int _warningsCount;

    public LogViewerViewModel(ILogEventService logEventService,
                              ILogNotificationService logNotificationService,
                              IMapper mapper,
                              ILogger logger)
    {
        _logEventService = logEventService ?? throw new ArgumentNullException(nameof(logEventService));
        _logNotificationService = logNotificationService ?? throw new ArgumentNullException(nameof(logNotificationService));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _filterChanged = new Subject<string>();

        var filter = _filterChanged.AsObservable().Select(BuildFilter);

        _sourceList.Connect()
                   .Filter(filter)
                   .Sort(SortExpressionComparer<LogMessageViewModel>.Descending(m => m.Timestamp))
                   .Bind(out _messageViewModels)
                   .Subscribe();
        _filterChanged.OnNext(string.Empty);

        ClearMessagesCommand = CreateCommand(ClearMessages, CanClearMessages)
            .ObservesProperty(() => MessageViewModels.Count);
    }

    public bool ShowWarnings
    {
        get => _showWarnings;
        set
        {
            if (SetProperty(ref _showWarnings, value))
            {
                SafeFilterUpdate(nameof(ShowWarnings));
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
                SafeFilterUpdate(nameof(ShowErrors));
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

    public string Title { get; } = "Log messages";

    /// <inheritdoc />
    DialogCloseListener IDialogAware.RequestClose { get; }

    public bool CanCloseDialog()
    {
        return true;
    }

    public void OnDialogOpened(IDialogParameters parameters)
    {
        ThrowIfDisposed();

        try
        {
            _compositeDisposable = new CompositeDisposable
            {
                _logEventService.ErrorReceived.Subscribe(OnErrorReceived),
                _logEventService.WarningReceived.Subscribe(OnWarningReceived),
                _logEventService.MessagesCleared.Subscribe(OnMessagesCleared),

                _logNotificationService.ErrorsCountUpdated.Subscribe(OnErrorsCountUpdated),
                _logNotificationService.WarningsCountUpdated.Subscribe(OnWarningsCountUpdated)
            };

            LoadExistingMessages();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open log viewer dialog");
            throw;
        }
    }

    public void OnDialogClosed()
    {
        SafeDispose(ref _compositeDisposable);
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _sourceList?.Dispose();
            _filterChanged?.Dispose();
            SafeDispose(ref _compositeDisposable);
            _isDisposed = true;
        }
    }

    public event Action<IDialogResult>? RequestClose;

    private void LoadExistingMessages()
    {
        try
        {
            var errors = _logEventService.GetLastErrors().ToList();
            foreach (var message in errors)
            {
                AddMessageSafe(() => _mapper.Map<ErrorLogMessageViewModel>(message));
            }

            ErrorsCount = errors.Count;

            var warnings = _logEventService.GetLastWarnings().ToList();
            foreach (var message in warnings)
            {
                AddMessageSafe(() => _mapper.Map<WarningLogMessageViewModel>(message));
            }

            WarningsCount = warnings.Count;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load existing log messages");
        }
    }

    private void AddMessageSafe(Func<LogMessageViewModel> messageFactory)
    {
        try
        {
            var viewModel = messageFactory();
            _sourceList.Add(viewModel);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add log message to view model");
        }
    }

    private void SafeFilterUpdate(string filterName)
    {
        try
        {
            _filterChanged.OnNext(filterName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update filter: {FilterName}", filterName);
        }
    }

    private Func<LogMessageViewModel, bool> BuildFilter(string filterPropertyName)
    {
        return m =>
        {
            try
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
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Filter evaluation failed for message: {MessageType}", m.GetType().Name);
                return false;
            }
        };
    }

    private void OnErrorReceived(LogMessage message)
    {
        AddMessageSafe(() => _mapper.Map<ErrorLogMessageViewModel>(message));
    }

    private void OnWarningReceived(LogMessage message)
    {
        AddMessageSafe(() => _mapper.Map<WarningLogMessageViewModel>(message));
    }

    private void OnMessagesCleared(bool obj)
    {
        try
        {
            _sourceList.Clear();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to clear messages");
        }
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
        try
        {
            _logEventService.ClearMessages();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to clear log messages");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(LogViewerViewModel));
        }
    }

    private void SafeDispose<T>(ref T disposable) where T : IDisposable?
    {
        try
        {
            disposable?.Dispose();
            disposable = default;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to dispose {Type}", typeof(T).Name);
        }
    }
}