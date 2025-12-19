using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain.Logs;

using Serilog.Core;
using Serilog.Events;

namespace ImageCare.Core.Services.LogEventService;

public sealed class SinkLogEventService : ILogEventSink, ILogEventService, ILogNotificationService, IDisposable
{
	private readonly Subject<LogMessage> _errorReceivedSubject;
	private readonly Subject<LogMessage> _warningReceivedSubject;
	private readonly Subject<bool> _messagesClearedSubject;
	private readonly Subject<int> _errorsCountSubject;
	private readonly Subject<int> _warningsCountSubject;

	private readonly ConcurrentQueue<LogMessage> _warningMessages;
	private readonly ConcurrentQueue<LogMessage> _errorMessages;
	private readonly int _maxMessagesCapacity;
	private bool _isDisposed;

	public SinkLogEventService(int maxMessagesCapacity = 1000)
	{
		_maxMessagesCapacity = maxMessagesCapacity;
		_warningMessages = new ConcurrentQueue<LogMessage>();
		_errorMessages = new ConcurrentQueue<LogMessage>();

		_errorReceivedSubject = new Subject<LogMessage>();
		_warningReceivedSubject = new Subject<LogMessage>();
		_messagesClearedSubject = new Subject<bool>();
		_errorsCountSubject = new Subject<int>();
		_warningsCountSubject = new Subject<int>();
	}

	public IObservable<LogMessage> ErrorReceived => _errorReceivedSubject.AsObservable();

	public IObservable<LogMessage> WarningReceived => _warningReceivedSubject.AsObservable();

	public IObservable<bool> MessagesCleared => _messagesClearedSubject.AsObservable();

	public IObservable<int> ErrorsCountUpdated => _errorsCountSubject.AsObservable();

	public IObservable<int> WarningsCountUpdated => _warningsCountSubject.AsObservable();

	public void Dispose()
	{
		if (!_isDisposed)
		{
			_errorReceivedSubject.OnCompleted();
			_warningReceivedSubject.OnCompleted();
			_messagesClearedSubject.OnCompleted();
			_errorsCountSubject.OnCompleted();
			_warningsCountSubject.OnCompleted();

			_errorReceivedSubject.Dispose();
			_warningReceivedSubject.Dispose();
			_messagesClearedSubject.Dispose();
			_errorsCountSubject.Dispose();
			_warningsCountSubject.Dispose();

			_isDisposed = true;
		}
	}

	public IEnumerable<LogMessage> GetLastErrors()
	{
		ThrowIfDisposed();
		return _errorMessages.ToArray();
	}

	public IEnumerable<LogMessage> GetLastWarnings()
	{
		ThrowIfDisposed();
		return _warningMessages.ToArray();
	}

	public void ClearMessages()
	{
		ThrowIfDisposed();

		_warningMessages.Clear();
		_errorMessages.Clear();

		_errorsCountSubject.OnNext(0);
		_warningsCountSubject.OnNext(0);
		_messagesClearedSubject.OnNext(true);
	}

	public void Emit(LogEvent logEvent)
	{
		ThrowIfDisposed();
		HandleLogEvent(logEvent);
	}

	public int GetErrorsCount()
	{
		ThrowIfDisposed();
		return _errorMessages.Count;
	}

	public int GetWarningsCount()
	{
		ThrowIfDisposed();
		return _warningMessages.Count;
	}

	private void HandleLogEvent(LogEvent logEvent)
	{
		var logMessage = new LogMessage(
			logEvent.Timestamp,
			logEvent.MessageTemplate.Text,
			logEvent.Exception?.Message);

		switch (logEvent.Level)
		{
			case LogEventLevel.Warning:
				AddMessageWithCapacity(_warningMessages, logMessage, _warningsCountSubject);
				_warningReceivedSubject.OnNext(logMessage);
				break;
			case LogEventLevel.Error:
			case LogEventLevel.Fatal:
				AddMessageWithCapacity(_errorMessages, logMessage, _errorsCountSubject);
				_errorReceivedSubject.OnNext(logMessage);
				break;
		}
	}

	private void AddMessageWithCapacity(ConcurrentQueue<LogMessage> queue, LogMessage message, Subject<int> countSubject)
	{
		queue.Enqueue(message);

		while (queue.Count > _maxMessagesCapacity && queue.TryDequeue(out _)) { }

		countSubject.OnNext(queue.Count);
	}

	private void ThrowIfDisposed()
	{
		if (_isDisposed)
		{
			throw new ObjectDisposedException(nameof(SinkLogEventService));
		}
	}
}