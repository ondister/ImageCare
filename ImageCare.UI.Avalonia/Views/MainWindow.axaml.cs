using System;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using ImageCare.UI.Avalonia.ViewModels;

namespace ImageCare.UI.Avalonia.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _initializedViewModel;
    private bool _isLoaded;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(
            KeyDownEvent,
            OnWindowKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            KeyUpEvent,
            OnWindowKeyUp,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        TryInitializeViewModel();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        TryInitializeViewModel();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.HandleKeyDown(e);
        }
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.HandleKeyUp(e);
        }
    }

    private void TryInitializeViewModel()
    {
        if (!_isLoaded ||
            DataContext is not MainWindowViewModel viewModel ||
            ReferenceEquals(_initializedViewModel, viewModel) ||
            !viewModel.OnViewLoadedCommand.CanExecute(null))
        {
            return;
        }

        _initializedViewModel = viewModel;
        viewModel.OnViewLoadedCommand.Execute(null);
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ResetFileTransferModifiers();
            if (viewModel.OnViewUnloadedCommand.CanExecute(null))
            {
                viewModel.OnViewUnloadedCommand.Execute(null);
            }
        }
    }
}
