using System;

using Avalonia.Controls;
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
        if (DataContext is MainWindowViewModel viewModel &&
            viewModel.OnViewUnloadedCommand.CanExecute(null))
        {
            viewModel.OnViewUnloadedCommand.Execute(null);
        }
    }
}
