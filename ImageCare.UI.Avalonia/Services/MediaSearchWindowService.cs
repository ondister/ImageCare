using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.ViewModels;
using ImageCare.UI.Avalonia.Views;

using Serilog;

namespace ImageCare.UI.Avalonia.Services;

internal sealed class MediaSearchWindowService
{
    private readonly IMediaAnnotationsService _annotationsService;
    private readonly IConfigurationService _configurationService;
    private readonly IMediaPreviewService _previewService;
    private readonly IMediaPreviewOperationsService _previewOperations;
    private readonly INotificationService _notificationService;
    private readonly LocalizationManager _localization;
    private readonly ILogger _logger;
    private Window? _window;

    public MediaSearchWindowService(
        IMediaAnnotationsService annotationsService,
        IConfigurationService configurationService,
        IMediaPreviewService previewService,
        IMediaPreviewOperationsService previewOperations,
        INotificationService notificationService,
        LocalizationManager localization,
        ILogger logger)
    {
        _annotationsService = annotationsService;
        _configurationService = configurationService;
        _previewService = previewService;
        _previewOperations = previewOperations;
        _notificationService = notificationService;
        _localization = localization;
        _logger = logger;
    }

    public void Show()
    {
        if (_window != null)
        {
            _window.Activate();
            return;
        }

        Window window = null!;
        var viewModel = new MediaSearchViewModel(
            _annotationsService,
            _configurationService,
            _previewService,
            _previewOperations,
            _notificationService,
            _localization,
            _logger,
            () => window.Close());
        window = new Window
        {
            Title = viewModel.Title,
            Width = 980,
            Height = 620,
            MinWidth = 720,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new MediaSearchView { DataContext = viewModel }
        };

        _window = window;
        _localization.CultureChanged += OnCultureChanged;
        window.Closed += OnClosed;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }

        void OnCultureChanged(object? sender, EventArgs args) => window.Title = viewModel.Title;
        void OnClosed(object? sender, EventArgs args)
        {
            window.Closed -= OnClosed;
            _localization.CultureChanged -= OnCultureChanged;
            _window = null;
        }
    }
}
