using System;
using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using ImageCare.UI.Avalonia.ViewModels.Preview;

namespace ImageCare.UI.Avalonia.Views;

public partial class PreviewAnnotationEditorView : UserControl
{
	private PreviewAnnotationEditorViewModel? _viewModel;

	public PreviewAnnotationEditorView()
	{
		InitializeComponent();
	}

	protected override void OnDataContextChanged(EventArgs e)
	{
		base.OnDataContextChanged(e);

		if (_viewModel != null)
		{
			_viewModel.PropertyChanged -= OnViewModelPropertyChanged;
		}

		_viewModel = DataContext as PreviewAnnotationEditorViewModel;
		if (_viewModel != null)
		{
			_viewModel.PropertyChanged += OnViewModelPropertyChanged;
		}
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
	{
		if (DataContext is not PreviewAnnotationEditorViewModel viewModel)
		{
			return;
		}

		if (args.PropertyName != nameof(PreviewAnnotationEditorViewModel.NewTag) &&
			args.PropertyName != nameof(PreviewAnnotationEditorViewModel.HasAvailableAnnotationTags))
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

	private void OnPreviewTagInputKeyDown(object? sender, KeyEventArgs args)
	{
		if (DataContext is not PreviewAnnotationEditorViewModel viewModel)
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
		if (sender is Button { DataContext: string tag } && DataContext is PreviewAnnotationEditorViewModel viewModel)
		{
			// Suggestions live in a popup, so their commands cannot reach the view model directly.
			viewModel.AddExistingTag(tag);
		}
	}
}
