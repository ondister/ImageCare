using System.Threading.Tasks;

namespace ImageCare.UI.Avalonia.Services;

internal interface IConfirmationDialogService
{
    Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText,
        bool isDestructive);
}
