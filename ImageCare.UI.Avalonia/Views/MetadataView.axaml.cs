using System;

using Avalonia.Controls;
using Avalonia.Interactivity;

using ImageCare.UI.Avalonia.ViewModels;

namespace ImageCare.UI.Avalonia.Views;

public partial class MetadataView : UserControl
{
    private MetadataViewModel? _initializedViewModel;
    private bool _isLoaded;

    public MetadataView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnViewLoaded(object? sender, RoutedEventArgs e)
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
            DataContext is not MetadataViewModel viewModel ||
            ReferenceEquals(_initializedViewModel, viewModel) ||
            !viewModel.OnViewLoadedCommand.CanExecute(null))
        {
            return;
        }

        _initializedViewModel = viewModel;
        viewModel.OnViewLoadedCommand.Execute(null);
    }
}
