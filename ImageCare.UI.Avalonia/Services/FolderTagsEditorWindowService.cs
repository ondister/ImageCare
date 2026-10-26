using System;
using System.Collections.Generic;
using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using ImageCare.Core.Domain.Annotations;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.ViewModels;
using ImageCare.UI.Avalonia.Views;

using Serilog;

namespace ImageCare.UI.Avalonia.Services;

/// <summary>
/// Opens one folder-tags editor window per folder. Re-invoking the menu item activates the
/// existing window instead of opening a duplicate.
/// </summary>
internal sealed class FolderTagsEditorWindowService
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly IFolderTagsStore _folderTagsStore;
    private readonly LocalizationManager _localization;
    private readonly ILogger _logger;
    private readonly Dictionary<string, Window> _openEditors = new(PathComparer);

    public FolderTagsEditorWindowService(
        IFolderTagsStore folderTagsStore,
        LocalizationManager localization,
        ILogger logger)
    {
        _folderTagsStore = folderTagsStore ?? throw new ArgumentNullException(nameof(folderTagsStore));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Show(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            _logger.Warning(exception, "Cannot open folder tags editor for {Path}", folderPath);
            return;
        }

        if (_openEditors.TryGetValue(normalizedPath, out var existingWindow))
        {
            if (existingWindow.WindowState == WindowState.Minimized)
            {
                existingWindow.WindowState = WindowState.Normal;
            }

            existingWindow.Activate();
            return;
        }

        var viewModel = new FolderTagsEditorViewModel(normalizedPath, _folderTagsStore, _localization, _logger);
        var window = new Window
        {
            Title = viewModel.Title,
            Width = 660,
            Height = 600,
            MinWidth = 520,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new FolderTagsEditorView { DataContext = viewModel }
        };

        _openEditors[normalizedPath] = window;

        window.Opened += OnOpened;
        window.Closed += OnClosed;
        _localization.CultureChanged += OnCultureChanged;
        viewModel.CloseRequested += OnCloseRequested;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { } owner
            })
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }

        return;

        void OnOpened(object? sender, EventArgs args)
        {
            window.Opened -= OnOpened;
            viewModel.Load();
        }

        void OnCultureChanged(object? sender, EventArgs args)
        {
            window.Title = viewModel.Title;
        }

        void OnCloseRequested(object? sender, EventArgs args)
        {
            window.Close();
        }

        void OnClosed(object? sender, EventArgs args)
        {
            window.Closed -= OnClosed;
            _localization.CultureChanged -= OnCultureChanged;
            viewModel.CloseRequested -= OnCloseRequested;
            _openEditors.Remove(normalizedPath);
        }
    }
}
