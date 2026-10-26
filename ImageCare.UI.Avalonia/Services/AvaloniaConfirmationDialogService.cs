using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;

using ImageCare.UI.Avalonia.Localization;

namespace ImageCare.UI.Avalonia.Services;

internal sealed class AvaloniaConfirmationDialogService(LocalizationManager localization)
    : IConfirmationDialogService
{
    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText,
        bool isDestructive)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { } owner
            })
        {
            return false;
        }

        var cancelButton = new Button
        {
            Content = localization["Common_Cancel"],
            MinWidth = 100,
            IsCancel = true,
            IsDefault = isDestructive
        };
        var confirmButton = new Button
        {
            Content = confirmText,
            MinWidth = 100,
            IsDefault = !isDestructive
        };
        if (isDestructive)
        {
            confirmButton.Classes.Add("Danger");
        }

        var window = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            cancelButton,
                            confirmButton
                        }
                    }
                }
            }
        };

        cancelButton.Click += (_, _) => window.Close(false);
        confirmButton.Click += (_, _) => window.Close(true);
        window.Opened += (_, _) => cancelButton.Focus();

        return await window.ShowDialog<bool>(owner);
    }
}
