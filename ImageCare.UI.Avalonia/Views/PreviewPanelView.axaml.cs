using System;
using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using ImageCare.UI.Avalonia.ViewModels;

namespace ImageCare.UI.Avalonia.Views;

public partial class PreviewPanelView : UserControl
{
	public PreviewPanelView()
	{
		InitializeComponent();
	}

	protected override void OnDataContextChanged(EventArgs e)
	{
		base.OnDataContextChanged(e);
		if (DataContext is PreviewPanelViewModel viewModel)
		{
			viewModel.PropertyChanged -= OnViewModelPropertyChanged;
			viewModel.PropertyChanged += OnViewModelPropertyChanged;
		}
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
	{
		if (args.PropertyName != nameof(PreviewPanelViewModel.NewTag) ||
			DataContext is not PreviewPanelViewModel viewModel)
		{
			return;
		}

		// The popup floats above the tag field and only appears while the user is typing.
		TagSuggestionsPopup.IsOpen = viewModel.HasAvailableAnnotationTags &&
									 !string.IsNullOrWhiteSpace(viewModel.NewTag);
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
