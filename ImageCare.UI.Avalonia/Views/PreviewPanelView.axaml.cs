using System;
using System.ComponentModel;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using ImageCare.UI.Avalonia.ViewModels;
using ImageCare.UI.Avalonia.ViewModels.Domain;

namespace ImageCare.UI.Avalonia.Views;

public partial class PreviewPanelView : UserControl
{
	private PreviewPanelViewModel? _viewModel;
	private bool _isSynchronizingSelection;

	public PreviewPanelView()
	{
		InitializeComponent();
	}

    internal bool FocusPanel()
    {
        if (PreviewsList.SelectedItem is { } selected)
        {
            PreviewsList.ScrollIntoView(selected);
            if (PreviewsList.ContainerFromItem(selected) is Control container)
            {
                return container.Focus();
            }
        }

        return PreviewsList.Focus();
    }

	protected override void OnDataContextChanged(EventArgs e)
	{
		base.OnDataContextChanged(e);

		if (_viewModel != null)
		{
			_viewModel.PropertyChanged -= OnViewModelPropertyChanged;
		}

		_viewModel = DataContext as PreviewPanelViewModel;
		if (_viewModel != null)
		{
			_viewModel.PropertyChanged += OnViewModelPropertyChanged;
			SynchronizeSelectionFromViewModel();
		}
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
	{
		if (DataContext is not PreviewPanelViewModel viewModel)
		{
			return;
		}

		if (args.PropertyName == nameof(PreviewPanelViewModel.SelectedPreview))
		{
			SynchronizeSelectionFromViewModel();
			return;
		}

		if (args.PropertyName != nameof(PreviewPanelViewModel.NewTag))
		{
			return;
		}

		if (TagSuggestionsPopup == null)
		{
			return;
		}

		// The popup floats above the tag field and only appears while the user is typing.
		TagSuggestionsPopup.IsOpen = viewModel.HasAvailableAnnotationTags &&
									 !string.IsNullOrWhiteSpace(viewModel.NewTag);
	}

	private void OnPreviewsSelectionChanged(object? sender, SelectionChangedEventArgs args)
	{
		if (_isSynchronizingSelection ||
			DataContext is not PreviewPanelViewModel viewModel)
		{
			return;
		}

		var selectedPreviews = PreviewsList.SelectedItems?
			.OfType<MediaPreviewViewModel>()
			.ToArray() ?? Array.Empty<MediaPreviewViewModel>();
		var primary = PreviewsList.SelectedItem as MediaPreviewViewModel;

		_isSynchronizingSelection = true;
		try
		{
			viewModel.UpdateSelection(selectedPreviews, primary);
		}
		finally
		{
			_isSynchronizingSelection = false;
		}
	}

	private void SynchronizeSelectionFromViewModel()
	{
		if (PreviewsList == null ||
			_isSynchronizingSelection ||
			DataContext is not PreviewPanelViewModel viewModel)
		{
			return;
		}

		_isSynchronizingSelection = true;
		try
		{
			var selectedItems = PreviewsList.SelectedItems;
			selectedItems?.Clear();

			var target = viewModel.SelectedPreview;
			if (target != null && viewModel.ImagePreviews.Contains(target))
			{
				PreviewsList.SelectedItem = target;
				if (selectedItems != null && !selectedItems.Contains(target))
				{
					selectedItems.Add(target);
				}
			}
			else
			{
				PreviewsList.SelectedItem = null;
			}

			var selectedPreviews = selectedItems?
				.OfType<MediaPreviewViewModel>()
				.ToArray() ?? Array.Empty<MediaPreviewViewModel>();
			viewModel.UpdateSelection(selectedPreviews, target);
		}
		finally
		{
			_isSynchronizingSelection = false;
		}
	}

	private void OnPreviewTagInputKeyDown(object? sender, KeyEventArgs args)
	{
		if (DataContext is not PreviewPanelViewModel viewModel)
		{
			return;
		}

		if (args.Key == Key.Escape)
		{
			TagSuggestionsPopup.IsOpen = false;
			args.Handled = true;
			return;
		}

		if (args.Key != Key.Enter)
		{
			return;
		}

		// Enter commits the typed tag, mirroring the media search window behaviour.
		TagSuggestionsPopup.IsOpen = false;
		viewModel.AddTagInput();
		args.Handled = true;
	}

	private void OnTagSuggestionClick(object? sender, RoutedEventArgs args)
	{
		TagSuggestionsPopup.IsOpen = false;
		if (sender is Button { DataContext: string tag } && DataContext is PreviewPanelViewModel viewModel)
		{
			// Suggestions live in a popup, so their commands cannot reach the view model directly.
			viewModel.AddExistingTag(tag);
		}
	}
}
