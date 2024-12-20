namespace ImageCare.Modules.Logging.ViewModels;

internal sealed class ErrorLogMessageViewModel : LogMessageViewModel
{
    /// <inheritdoc />
    public ErrorLogMessageViewModel(DateTimeOffset timestamp, string message, string? exceptionMessage)
        : base(timestamp, message, exceptionMessage) { }
}