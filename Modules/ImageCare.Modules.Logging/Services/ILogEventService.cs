using ImageCare.Modules.Logging.Models;

namespace ImageCare.Modules.Logging.Services;

interface ILogEventService
{
    public IObservable<LogMessage> ErrorReceived { get; }

    public IObservable<LogMessage> WarningReceived { get; }

    public IObservable<bool> MessagesCleared { get; }

    public IEnumerable<LogMessage> GetLastErrors();

    public IEnumerable<LogMessage> GetLastWarnings();

    public void ClearMessages();
}