using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using ImageCare.Core.Services.ConfigurationService;
using ImageCare.UI.Avalonia.ViewModels;
using ImageCare.UI.Avalonia.Views;

using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.Services;

internal sealed class SettingsWindowService
{
    private readonly IConfigurationService _configurationService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private Window? _window;

    public SettingsWindowService(IConfigurationService configurationService,
                                 IFileDialogService fileDialogService,
                                 IMapper mapper,
                                 ILogger logger)
    {
        _configurationService = configurationService;
        _fileDialogService = fileDialogService;
        _mapper = mapper;
        _logger = logger;
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
            _mapper,
            _logger);
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

        void OnClosed(object? sender, EventArgs args)
        {
            window.Closed -= OnClosed;
            viewModel.Close();
            _window = null;
        }
    }
}
