using System;
using System.ComponentModel;
using System.Linq;

using Avalonia.Controls;

using ImageCare.UI.Avalonia.ViewModels.Domain;
using ImageCare.UI.Avalonia.ViewModels.Preview;

namespace ImageCare.UI.Avalonia.Views;

public partial class PreviewStripView : UserControl
{
	private PreviewImageStripViewModel? _viewModel;
	private bool _isSynchronizingSelection;

	public PreviewStripView()
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

		_viewModel = DataContext as PreviewImageStripViewModel;
		if (_viewModel != null)
		{
			_viewModel.PropertyChanged += OnViewModelPropertyChanged;
			SynchronizeSelectionFromViewModel();
		}
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
	{
		if (args.PropertyName == nameof(PreviewImageStripViewModel.SelectedPreview))
		{
			SynchronizeSelectionFromViewModel();
		}
	}

	private void OnPreviewsSelectionChanged(object? sender, SelectionChangedEventArgs args)
	{
		if (_isSynchronizingSelection ||
			DataContext is not PreviewImageStripViewModel viewModel)
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
			DataContext is not PreviewImageStripViewModel viewModel)
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
}
