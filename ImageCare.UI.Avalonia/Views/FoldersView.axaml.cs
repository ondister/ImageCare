using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using ImageCare.UI.Avalonia.ViewModels;
using ImageCare.UI.Avalonia.ViewModels.Domain;

namespace ImageCare.UI.Avalonia.Views;

public partial class FoldersView : UserControl
{
    private int _centerRequestVersion;
    private FoldersViewModel? _viewModel;

    public FoldersView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        AttachViewModel(DataContext as FoldersViewModel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        AttachViewModel(DataContext as FoldersViewModel);
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachViewModel(DataContext as FoldersViewModel);
        if (_viewModel?.SelectedFileSystemItem is { } selectedItem)
        {
            OnRevealSelectedFolderRequested(selectedItem);
        }
    }

    private void AttachViewModel(FoldersViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel != null)
        {
            _viewModel.RevealSelectedFolderRequested -= OnRevealSelectedFolderRequested;
        }

        _viewModel = viewModel;
        if (_viewModel != null)
        {
            _viewModel.RevealSelectedFolderRequested += OnRevealSelectedFolderRequested;
        }
    }

    private void OnRevealSelectedFolderRequested(DirectoryViewModel selectedItem)
    {
        var requestVersion = ++_centerRequestVersion;
        var path = GetPathFromRoot(selectedItem);
        Dispatcher.UIThread.Post(
            () => RevealPathLevel(path, 0, requestVersion, 0),
            DispatcherPriority.Loaded);
    }

    private void RevealPathLevel(
        IReadOnlyList<DirectoryViewModel> path,
        int level,
        int requestVersion,
        int attempt)
    {
        if (requestVersion != _centerRequestVersion || level >= path.Count)
        {
            return;
        }

        var item = path[level];
        FolderTree.ScrollIntoView(item);
        if (FolderTree.TreeContainerFromItem(item) is not TreeViewItem container)
        {
            if (attempt < 120)
            {
                DispatcherTimer.RunOnce(
                    () => RevealPathLevel(path, level, requestVersion, attempt + 1),
                    TimeSpan.FromMilliseconds(25),
                    DispatcherPriority.Loaded);
            }

            return;
        }

        if (level < path.Count - 1)
        {
            if (!container.IsExpanded)
            {
                container.IsExpanded = true;
            }

            DispatcherTimer.RunOnce(
                () => RevealPathLevel(path, level + 1, requestVersion, 0),
                TimeSpan.FromMilliseconds(16),
                DispatcherPriority.Loaded);
            return;
        }

        container.IsSelected = true;
        FolderTree.SelectedItem = item;
        container.BringIntoView();
        container.Focus();
    }

    private static IReadOnlyList<DirectoryViewModel> GetPathFromRoot(DirectoryViewModel selectedItem)
    {
        var path = new List<DirectoryViewModel>();
        for (var current = selectedItem; current != null; current = current.Parent)
        {
            path.Add(current);
        }

        path.Reverse();
        return path;
    }
    private void OnChoosePopularFolderClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.AddRecentFolderCommand.CanExecute(null) == true)
        {
            _viewModel.AddRecentFolderCommand.Execute(null);
        }
    }

    private void OnAddFolderToRecentClick(object? sender, RoutedEventArgs e)
    {
        var menuItem = sender as MenuItem;
        var directory = menuItem?.Tag as DirectoryViewModel
                        ?? menuItem?.DataContext as DirectoryViewModel;
        if (directory != null && _viewModel?.AddFolderToRecentCommand.CanExecute(directory) == true)
        {
            _viewModel.AddFolderToRecentCommand.Execute(directory);
        }
    }

    private static void OnNameTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: DirectoryViewModel viewModel } &&
            viewModel.NameTextBoxLostFocusCommand.CanExecute(null))
        {
            viewModel.NameTextBoxLostFocusCommand.Execute(null);
        }
    }
}
