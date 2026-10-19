using System;
using System.Threading;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.ViewModels;
using ImageCare.UI.Avalonia.Views;

using Serilog;

namespace ImageCare.UI.Avalonia.Services;

internal sealed class ImageViewerWindowService
{
    private readonly IMediaPreviewService _imageService;
    private readonly IMediaPreviewOperationsService _fileOperationsService;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly LocalizationManager _localization;

    public ImageViewerWindowService(IMediaPreviewService imageService,
                                    IMediaPreviewOperationsService fileOperationsService,
                                    ILogger logger,
                                    SynchronizationContext synchronizationContext,
                                    LocalizationManager localization)
    {
        _imageService = imageService;
        _fileOperationsService = fileOperationsService;
        _logger = logger;
        _synchronizationContext = synchronizationContext;
        _localization = localization;
    }

    public void Show()
    {
        var viewModel = new MainImageSeparateViewModel(
            _imageService,
            _fileOperationsService,
            _logger,
            _synchronizationContext);
        var window = new Window
        {
            Title = _localization["ImageViewer_Title"],
            MinWidth = 800,
            MinHeight = 600,
            WindowState = WindowState.Maximized,
            Content = new MainImageSeparateView { DataContext = viewModel }
        };

        window.Opened += OnOpened;
        window.Closed += OnClosed;
        _localization.CultureChanged += OnCultureChanged;

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
            viewModel.Open();
        }

        void OnCultureChanged(object? sender, EventArgs args)
        {
            window.Title = _localization["ImageViewer_Title"];
        }

        void OnClosed(object? sender, EventArgs args)
        {
            window.Closed -= OnClosed;
            _localization.CultureChanged -= OnCultureChanged;
            viewModel.Close();
        }
    }
}
