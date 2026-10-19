using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace ImageCare.UI.Avalonia.Services;

internal sealed class ClipboardService : IClipboardService
{
    public async Task CopyToClipboardAsync(string text)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow.Clipboard: { } clipboard
            })
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
