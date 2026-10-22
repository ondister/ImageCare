using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace ImageCare.UI.Avalonia.Services;

internal sealed class ClipboardService : IClipboardService
{
    public async Task CopyToClipboardAsync(string text)
    {
        // The Windows clipboard uses OLE, which must be accessed from the UI thread where
        // it was initialized. Callers (for example log-viewer commands) may run on a
        // background thread, so always marshal the write onto the dispatcher.
        await Dispatcher.UIThread.InvokeAsync(
            async () =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
                    {
                        MainWindow.Clipboard: { } clipboard
                    })
                {
                    await clipboard.SetTextAsync(text);
                }
            });
    }
}
