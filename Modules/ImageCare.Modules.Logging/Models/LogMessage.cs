namespace ImageCare.Modules.Logging.Models;

internal sealed class LogMessage
{
    public LogMessage(DateTimeOffset timestamp, string message, string? exceptionMessage)
    {
        Timestamp = timestamp;
        Message = message;
        ExceptionMessage = exceptionMessage;
    }

    public DateTimeOffset Timestamp { get; }

    public string Message { get; }

    public string? ExceptionMessage { get; }
}