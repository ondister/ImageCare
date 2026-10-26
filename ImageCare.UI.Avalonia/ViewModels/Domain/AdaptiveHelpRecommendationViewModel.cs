using System;
using System.Threading.Tasks;
using System.Windows.Input;

using Prism.Commands.Ex;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal sealed class AdaptiveHelpRecommendationViewModel
{
    public AdaptiveHelpRecommendationViewModel(
        string id,
        string title,
        string message,
        string reason,
        string actionText,
        string dismissText,
        Func<string, Task> executeAction,
        Action<string> dismiss)
    {
        Id = id;
        Title = title;
        Message = message;
        Reason = reason;
        ActionText = actionText;
        DismissText = dismissText;
        HasAction = !string.IsNullOrWhiteSpace(actionText);
        ActionCommand = new AsyncDelegateCommand(
            async (_, cancellationToken) => await executeAction(id).WaitAsync(cancellationToken));
        DismissCommand = new DelegateCommand(() => dismiss(id));
    }

    public string Id { get; }

    public string Title { get; }

    public string Message { get; }

    public string Reason { get; }

    public string ActionText { get; }

    public string DismissText { get; }

    public bool HasAction { get; }

    public ICommand ActionCommand { get; }

    public ICommand DismissCommand { get; }
}
