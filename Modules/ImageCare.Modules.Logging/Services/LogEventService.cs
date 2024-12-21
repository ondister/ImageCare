using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Modules.Logging.Models;

using Serilog.Core;
using Serilog.Events;

namespace ImageCare.Modules.Logging.Services;

internal sealed class LogEventService : ILogEventSink, ILogEventService, ILogNotificationService, IDisposable
{
    private readonly Subject<LogMessage> _errorReceivedSubject;
    private readonly Subject<LogMessage> _warningReceivedSubject;
    private readonly Subject<bool> _messagesClearedSubject;
    private readonly Subject<int> _errorsCountSubject;
    private readonly Subject<int> _warningsCountSubject;

    private readonly ConcurrentQueue<LogMessage> _warningMessages;
    private readonly ConcurrentQueue<LogMessage> _errorMessages;

    public LogEventService()
    {
        _warningMessages = new ConcurrentQueue<LogMessage>();
        _errorMessages = new ConcurrentQueue<LogMessage>();

        _errorReceivedSubject = new Subject<LogMessage>();
        _warningReceivedSubject = new Subject<LogMessage>();
        _messagesClearedSubject = new Subject<bool>();
        _errorsCountSubject = new Subject<int>();
        _warningsCountSubject = new Subject<int>();
    }

    /// <inheritdoc />
    public IObservable<LogMessage> ErrorReceived => _errorReceivedSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<LogMessage> WarningReceived => _warningReceivedSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<bool> MessagesCleared => _messagesClearedSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<int> ErrorsCountUpdated => _errorsCountSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<int> WarningsCountUpdated => _warningsCountSubject.AsObservable();

    /// <inheritdoc />
    public void Dispose()
    {
        _errorReceivedSubject.Dispose();
        _warningReceivedSubject.Dispose();
        _messagesClearedSubject.Dispose();
        _errorsCountSubject.Dispose();
        _warningsCountSubject.Dispose();
    }

    /// <inheritdoc />
    public IEnumerable<LogMessage> GetLastErrors()
    {
        return [.. _errorMessages];
    }

    /// <inheritdoc />
    public IEnumerable<LogMessage> GetLastWarnings()
    {
        return [.. _warningMessages];
    }

    /// <inheritdoc />
    public void ClearMessages()
    {
        _warningMessages.Clear();
        _errorMessages.Clear();

        _errorsCountSubject.OnNext(0);
        _warningsCountSubject.OnNext(0);

        _messagesClearedSubject.OnNext(true);
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        HandleLogEvent(logEvent);
    }

    private void HandleLogEvent(LogEvent logEvent)
    {
        var logMessage = new LogMessage(logEvent.Timestamp, logEvent.MessageTemplate.Text, logEvent.Exception?.Message);
        switch (logEvent.Level)
        {
            case LogEventLevel.Verbose:
                break;
            case LogEventLevel.Debug:
                break;
            case LogEventLevel.Information:
                break;
            case LogEventLevel.Warning:
                _warningMessages.Enqueue(logMessage);
                _warningReceivedSubject.OnNext(logMessage);
                _warningsCountSubject.OnNext(_warningMessages.Count);
                break;
            case LogEventLevel.Error:
                _errorMessages.Enqueue(logMessage);
                _errorReceivedSubject.OnNext(logMessage);
                _errorsCountSubject.OnNext(_errorMessages.Count);
                break;
            case LogEventLevel.Fatal:
                break;
        }
    }

    public int GetErrorsCount()
    {
        return _errorMessages.Count;
    }

    public int GetWarningsCount()
    {
        return _warningMessages.Count;
    }
}