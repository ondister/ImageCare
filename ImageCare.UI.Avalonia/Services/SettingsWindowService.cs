using System;
using System.Threading;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.ViewModels;
using ImageCare.UI.Avalonia.Views;

using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.Services;

internal sealed class SettingsWindowService
{
    private readonly IConfigurationService _configurationService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IMediaAnnotationsService _annotationsService;
    private readonly ImageCareMcpServerController _mcpServerController;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly LocalizationManager _localization;
    private Window? _window;

    public SettingsWindowService(IConfigurationService configurationService,
                                 IFileDialogService fileDialogService,
                                 IMediaAnnotationsService annotationsService,
                                 ImageCareMcpServerController mcpServerController,
                                 SynchronizationContext synchronizationContext,
                                 IMapper mapper,
                                 ILogger logger,
                                 LocalizationManager localization)
    {
        _configurationService = configurationService;
        _fileDialogService = fileDialogService;
        _annotationsService = annotationsService;
        _mcpServerController = mcpServerController;
        _synchronizationContext = synchronizationContext;
        _mapper = mapper;
        _logger = logger;
        _localization = localization;
    }

    public void Show()
    {
        if (_window != null)
        {
            _window.Activate();
            return;
        }

        var viewModel = new SettingsViewModel(
            _configurationService,
            _fileDialogService,
            _annotationsService,
            _mcpServerController,
            _synchronizationContext,
            _mapper,
            _logger,
            _localization);
        var window = new Window
        {
            Title = viewModel.Title,
            Width = 900,
            Height = 700,
            MinWidth = 700,
            MinHeight = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new SettingsView { DataContext = viewModel }
        };

        _window = window;
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
            window.Title = viewModel.Title;
        }

        void OnClosed(object? sender, EventArgs args)
        {
            window.Closed -= OnClosed;
            _localization.CultureChanged -= OnCultureChanged;
            viewModel.Close();
            _window = null;
        }
    }
}
