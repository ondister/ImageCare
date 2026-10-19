using System;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace ImageCare.UI.Avalonia.Services;

public sealed class AvaloniaFileDialogService : IFileDialogService
{
    public Task<string?> ShowOpenApplicationDialogAsync(string title)
    {
        if (OperatingSystem.IsMacOS())
        {
            return ShowOpenFileDialogAsync(
                title,
                ("macOS applications", new[] { "*.app" }),
                ("All files", new[] { "*" }));
        }

        return ShowOpenFileDialogAsync(
            title,
            ("Windows applications", new[] { "*.exe" }),
            ("All files", new[] { "*.*" }));
    }

    public async Task<string?> ShowOpenFolderDialogAsync(string title)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return null;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });

        return folders.Count >= 1 ? folders[0].Path.LocalPath : null;
    }

    public async Task<string?> ShowOpenFileDialogAsync(
        string title,
        params (string Name, string[] Patterns)[] filters)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return null;
        }

        var filePickerFilters = filters.Select(filter => new FilePickerFileType(filter.Name)
            {
                Patterns = filter.Patterns
            })
            .ToArray();

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = filePickerFilters
            });

        return files.Count >= 1 ? files[0].Path.LocalPath : null;
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return TopLevel.GetTopLevel(null);
    }
}
