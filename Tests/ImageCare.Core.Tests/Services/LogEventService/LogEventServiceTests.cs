using ImageCare.Core.Domain.Logs;
using ImageCare.Core.Services.LogEventService;

using Serilog.Events;

namespace ImageCare.Core.Tests.Services.LogEventService;

[TestFixture]
public class LogEventServiceTests
{
    private SinkLogEventService _service;

    [SetUp]
    public void Setup()
    {
        _service = new SinkLogEventService(maxMessagesCapacity: 5);
    }

    [TearDown]
    public void TearDown()
    {
        _service?.Dispose();
    }

    [Test]
    public void Emit_WarningLogEvent_ShouldCreateLogMessageAndNotify()
    {
        LogMessage receivedMessage = null;
        using var subscription = _service.WarningReceived.Subscribe(msg => receivedMessage = msg);

        var timestamp = DateTimeOffset.Now;
        var logEvent = CreateLogEvent(LogEventLevel.Warning, "Test warning", timestamp);

        _service.Emit(logEvent);

        Assert.That(receivedMessage, Is.Not.Null);
        Assert.That(receivedMessage.Message, Is.EqualTo("Test warning"));
        Assert.That(receivedMessage.Timestamp, Is.EqualTo(timestamp));
        Assert.That(receivedMessage.ExceptionMessage, Is.Null);

        var warnings = _service.GetLastWarnings();
        Assert.That(warnings, Has.Exactly(1).Items);
        Assert.That(warnings.First().Message, Is.EqualTo("Test warning"));
        Assert.That(_service.GetWarningsCount(), Is.EqualTo(1));
    }

    [Test]
    public void Emit_ErrorLogEvent_ShouldCreateLogMessageWithException()
    {
        LogMessage receivedMessage = null;
        using var subscription = _service.ErrorReceived.Subscribe(msg => receivedMessage = msg);

        var exception = new InvalidOperationException("Test exception");
        var logEvent = CreateLogEvent(LogEventLevel.Error, "Test error", exception: exception);

        _service.Emit(logEvent);

        Assert.That(receivedMessage, Is.Not.Null);
        Assert.That(receivedMessage.Message, Is.EqualTo("Test error"));
        Assert.That(receivedMessage.ExceptionMessage, Is.EqualTo("Test exception"));
        Assert.That(_service.GetLastErrors(), Has.Exactly(1).Items);
        Assert.That(_service.GetErrorsCount(), Is.EqualTo(1));
    }

    [Test]
    public void Emit_FatalLogEvent_ShouldAddToErrors()
    {
        var logEvent = CreateLogEvent(LogEventLevel.Fatal, "Fatal error");

        _service.Emit(logEvent);

        var errors = _service.GetLastErrors();
        Assert.That(errors, Has.Exactly(1).Items);
        Assert.That(errors.First().Message, Is.EqualTo("Fatal error"));
    }

    [Test]
    public void Emit_InfoLogEvent_ShouldBeIgnored()
    {
        var logEvent = CreateLogEvent(LogEventLevel.Information, "Info message");

        _service.Emit(logEvent);

        Assert.That(_service.GetLastWarnings(), Is.Empty);
        Assert.That(_service.GetLastErrors(), Is.Empty);
        Assert.That(_service.GetWarningsCount(), Is.EqualTo(0));
        Assert.That(_service.GetErrorsCount(), Is.EqualTo(0));
    }

    [Test]
    public void ClearMessages_ShouldRemoveAllLogMessages()
    {
        var clearedReceived = false;
        using var clearSubscription = _service.MessagesCleared.Subscribe(_ => clearedReceived = true);

        _service.Emit(CreateLogEvent(LogEventLevel.Warning, "Warning 1"));
        _service.Emit(CreateLogEvent(LogEventLevel.Error, "Error 1"));

        _service.ClearMessages();

        Assert.That(_service.GetLastWarnings(), Is.Empty);
        Assert.That(_service.GetLastErrors(), Is.Empty);
        Assert.That(_service.GetWarningsCount(), Is.EqualTo(0));
        Assert.That(_service.GetErrorsCount(), Is.EqualTo(0));
        Assert.That(clearedReceived, Is.True);
    }

    [Test]
    public void ErrorsCountUpdated_ShouldNotifyOnErrorCountChanges()
    {
        var counts = new List<int>();
        using var subscription = _service.ErrorsCountUpdated.Subscribe(count => counts.Add(count));

        _service.Emit(CreateLogEvent(LogEventLevel.Error, "Error 1"));
        _service.Emit(CreateLogEvent(LogEventLevel.Error, "Error 2"));
        _service.ClearMessages();
        _service.Emit(CreateLogEvent(LogEventLevel.Error, "Error 3"));

        Assert.That(counts, Is.EqualTo(new[] { 1, 2, 0, 1 }));
    }

    [Test]
    public void WarningsCountUpdated_ShouldNotifyOnWarningCountChanges()
    {
        var counts = new List<int>();
        using var subscription = _service.WarningsCountUpdated.Subscribe(count => counts.Add(count));

        _service.Emit(CreateLogEvent(LogEventLevel.Warning, "Warning 1"));
        _service.Emit(CreateLogEvent(LogEventLevel.Warning, "Warning 2"));
        _service.ClearMessages();

        Assert.That(counts, Is.EqualTo(new[] { 1, 2, 0 }));
    }

    [Test]
    public void WhenCapacityExceeded_ShouldRemoveOldestLogMessages()
    {
        for (var i = 0; i < 10; i++)
        {
            _service.Emit(CreateLogEvent(LogEventLevel.Warning, $"Warning {i}"));
        }

        Assert.That(_service.GetWarningsCount(), Is.EqualTo(5));
        var warnings = _service.GetLastWarnings().ToArray();
        Assert.That(warnings[0].Message, Is.EqualTo("Warning 5"));
        Assert.That(warnings[4].Message, Is.EqualTo("Warning 9"));
    }

    [Test]
    public void GetLastMessages_ShouldReturnCopiesNotReferences()
    {
        _service.Emit(CreateLogEvent(LogEventLevel.Warning, "Warning 1"));

        var warnings1 = _service.GetLastWarnings();
        _service.Emit(CreateLogEvent(LogEventLevel.Warning, "Warning 2"));
        var warnings2 = _service.GetLastWarnings();

        Assert.That(warnings1.Count(), Is.EqualTo(1));
        Assert.That(warnings2.Count(), Is.EqualTo(2));
    }

    [Test]
    public void Dispose_ShouldCompleteAllObservables()
    {
        var errorCompleted = false;
        var warningCompleted = false;
        var clearCompleted = false;
        var errorsCountCompleted = false;
        var warningsCountCompleted = false;

        using var errorSub = _service.ErrorReceived.Subscribe(_ => { }, () => errorCompleted = true);
        using var warningSub = _service.WarningReceived.Subscribe(_ => { }, () => warningCompleted = true);
        using var clearSub = _service.MessagesCleared.Subscribe(_ => { }, () => clearCompleted = true);
        using var errorsCountSub = _service.ErrorsCountUpdated.Subscribe(_ => { }, () => errorsCountCompleted = true);
        using var warningsCountSub = _service.WarningsCountUpdated.Subscribe(_ => { }, () => warningsCountCompleted = true);

        _service.Dispose();

        Assert.That(errorCompleted, Is.True);
        Assert.That(warningCompleted, Is.True);
        Assert.That(clearCompleted, Is.True);
        Assert.That(errorsCountCompleted, Is.True);
        Assert.That(warningsCountCompleted, Is.True);
    }

    [Test]
    public void AfterDispose_ShouldThrowOnOperations()
    {
        _service.Dispose();
        var logEvent = CreateLogEvent(LogEventLevel.Error, "Test");

        Assert.Throws<ObjectDisposedException>(() => _service.Emit(logEvent));
        Assert.Throws<ObjectDisposedException>(() => _service.ClearMessages());
        Assert.Throws<ObjectDisposedException>(() => _service.GetLastErrors());
        Assert.Throws<ObjectDisposedException>(() => _service.GetLastWarnings());
    }

    [Test]
    public void MultipleSubscribers_ShouldAllReceiveLogMessages()
    {
        var receivedCount1 = 0;
        var receivedCount2 = 0;

        using var sub1 = _service.WarningReceived.Subscribe(_ => receivedCount1++);
        using var sub2 = _service.WarningReceived.Subscribe(_ => receivedCount2++);

        _service.Emit(CreateLogEvent(LogEventLevel.Warning, "Warning 1"));
        _service.Emit(CreateLogEvent(LogEventLevel.Warning, "Warning 2"));

        Assert.That(receivedCount1, Is.EqualTo(2));
        Assert.That(receivedCount2, Is.EqualTo(2));
    }

    [Test]
    public void LogMessage_ShouldContainCorrectTimestampFromLogEvent()
    {
        var expectedTimestamp = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        LogMessage receivedMessage = null;
        using var subscription = _service.ErrorReceived.Subscribe(msg => receivedMessage = msg);

        var logEvent = CreateLogEvent(LogEventLevel.Error, "Test", expectedTimestamp);

        _service.Emit(logEvent);

        Assert.That(receivedMessage.Timestamp, Is.EqualTo(expectedTimestamp));
    }

    private static LogEvent CreateLogEvent(LogEventLevel level, string message, DateTimeOffset? timestamp = null, Exception exception = null)
    {
        return new LogEvent(
            timestamp ?? DateTimeOffset.Now,
            level,
            exception,
            new MessageTemplate(message, []),
            []);
    }
}