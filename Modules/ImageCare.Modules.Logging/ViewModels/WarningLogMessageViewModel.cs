namespace ImageCare.Modules.Logging.ViewModels;

internal sealed class WarningLogMessageViewModel : LogMessageViewModel
{
    /// <inheritdoc />
    public WarningLogMessageViewModel(DateTimeOffset timestamp, string message, string? exceptionMessage)
        : base(timestamp, message, exceptionMessage) { }
}