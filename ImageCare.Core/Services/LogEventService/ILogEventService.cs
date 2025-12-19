using ImageCare.Core.Domain.Logs;

namespace ImageCare.Core.Services.LogEventService;

public interface ILogEventService
{
    public IObservable<LogMessage> ErrorReceived { get; }

    public IObservable<LogMessage> WarningReceived { get; }

    public IObservable<bool> MessagesCleared { get; }

    public IEnumerable<LogMessage> GetLastErrors();

    public IEnumerable<LogMessage> GetLastWarnings();

    public void ClearMessages();
}