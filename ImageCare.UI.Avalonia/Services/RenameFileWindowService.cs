using System;
using System.IO;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using ImageCare.Core.Services.FileSystemService;
using ImageCare.UI.Avalonia.Localization;

namespace ImageCare.UI.Avalonia.Services;

public sealed class RenameFileWindowService(LocalizationManager localization)
{
    public async Task<string?> RequestFileNameAsync(string sourcePath, string targetDirectoryPath)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { } owner
            })
        {
            return null;
        }

        var originalFileName = Path.GetFileName(sourcePath);
        var fileNameTextBox = new TextBox
        {
            Text = originalFileName,
            MinWidth = 420
        };
        var validationMessage = new TextBlock
        {
            Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };
        var confirmButton = new Button
        {
            Content = localization["Common_Confirm"],
            IsDefault = true,
            MinWidth = 100
        };
        var cancelButton = new Button
        {
            Content = localization["Common_Cancel"],
            IsCancel = true,
            MinWidth = 100
        };
        var window = new Window
        {
            Title = localization["RenameFile_Title"],
            Width = 520,
            Height = 230,
            MinWidth = 460,
            MinHeight = 210,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = localization["RenameFile_Prompt"],
                        TextWrapping = TextWrapping.Wrap
                    },
                    fileNameTextBox,
                    validationMessage,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, confirmButton }
                    }
                }
            }
        };

        void Validate()
        {
            var requestedName = fileNameTextBox.Text?.Trim();
            var isPortableName = PortableFileNameValidator.IsValid(requestedName);
            var destinationExists = isPortableName &&
                                    File.Exists(Path.Combine(targetDirectoryPath, requestedName!));

            confirmButton.IsEnabled = isPortableName && !destinationExists;
            validationMessage.Text = destinationExists
                ? localization["RenameFile_AlreadyExists"]
                : localization["RenameFile_InvalidName"];
            validationMessage.IsVisible = !confirmButton.IsEnabled;
        }

        fileNameTextBox.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty)
            {
                Validate();
            }
        };
        confirmButton.Click += (_, _) =>
        {
            if (confirmButton.IsEnabled)
            {
                window.Close(fileNameTextBox.Text?.Trim());
            }
        };
        cancelButton.Click += (_, _) => window.Close(null);
        window.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                window.Close(null);
            }
        };
        window.Opened += (_, _) =>
        {
            fileNameTextBox.Focus();
            fileNameTextBox.SelectionStart = 0;
            fileNameTextBox.SelectionEnd = Path.GetFileNameWithoutExtension(originalFileName).Length;
        };

        Validate();
        return await window.ShowDialog<string?>(owner);
    }
}
