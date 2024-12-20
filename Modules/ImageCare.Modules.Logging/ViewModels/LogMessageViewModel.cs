using ImageCare.Mvvm;

namespace ImageCare.Modules.Logging.ViewModels;

internal class LogMessageViewModel : ViewModelBase, IComparable<LogMessageViewModel>
{
    public LogMessageViewModel(DateTimeOffset timestamp, string message, string? exceptionMessage)
    {
        Timestamp = timestamp;
        Message = message;
        ExceptionMessage = exceptionMessage;
    }

    public DateTimeOffset Timestamp { get; }

    public string Message { get; }

    public string? ExceptionMessage { get; }

    /// <inheritdoc />
    public int CompareTo(LogMessageViewModel? other)
    {
        if (ReferenceEquals(this, other))
        {
            return 0;
        }

        if (ReferenceEquals(null, other))
        {
            return 1;
        }

        var timestampComparison = Timestamp.CompareTo(other.Timestamp);
        if (timestampComparison != 0)
        {
            return timestampComparison;
        }

        return string.Compare(Message, other.Message, StringComparison.Ordinal);
    }
}