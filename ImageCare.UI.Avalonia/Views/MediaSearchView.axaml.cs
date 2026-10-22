using System;

using Avalonia.Controls;
using Avalonia.Input;

using ImageCare.UI.Avalonia.ViewModels;
using ImageCare.UI.Avalonia.ViewModels.Domain;

namespace ImageCare.UI.Avalonia.Views;

public partial class MediaSearchView : UserControl
{
    public MediaSearchView()
    {
        InitializeComponent();
        ResultsGrid.LoadingRow += OnLoadingRow;
        ResultsGrid.UnloadingRow += OnUnloadingRow;
    }

    private void OnTagInputKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || DataContext is not MediaSearchViewModel viewModel)
        {
            return;
        }

        // Enter commits the tag even if it was not picked from the suggestion list.
        viewModel.AddTagInput();
        args.Handled = true;
    }

    private void OnLoadingRow(object? sender, DataGridRowEventArgs args)
    {
        var row = args.Row;
        row.DataContextChanged -= OnRowDataContextChanged;
        row.DataContextChanged += OnRowDataContextChanged;
        RequestThumbnail(row);
    }

    private void OnUnloadingRow(object? sender, DataGridRowEventArgs args)
    {
        args.Row.DataContextChanged -= OnRowDataContextChanged;
    }

    private static void OnRowDataContextChanged(object? sender, EventArgs args)
    {
        if (sender is DataGridRow row)
        {
            RequestThumbnail(row);
        }
    }

    private static void RequestThumbnail(DataGridRow row)
    {
        if (row.DataContext is MediaSearchResultViewModel result)
        {
            // The grid virtualizes rows, so thumbnails are decoded only for visible results.
            result.RequestThumbnail();
        }
    }
}
