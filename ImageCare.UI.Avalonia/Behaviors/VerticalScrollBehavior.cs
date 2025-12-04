using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;

using ImageCare.UI.Avalonia.ViewModels;

namespace ImageCare.UI.Avalonia.Behaviors;

public class VerticalScrollBehavior : Behavior<ScrollViewer>
{
	public static readonly StyledProperty<bool> IsScrollResetRequestedProperty =
		AvaloniaProperty.Register<VerticalScrollBehavior, bool>(nameof(IsScrollResetRequested));
	private ScrollViewer? _scrollViewer;
	private double _lastOffset;
	private bool _isProgrammaticScroll;

	public bool IsScrollResetRequested
	{
		get => GetValue(IsScrollResetRequestedProperty);
		set => SetValue(IsScrollResetRequestedProperty, value);
	}

	protected override void OnAttached()
	{
		base.OnAttached();
		_scrollViewer = AssociatedObject;
		if (_scrollViewer != null)
		{
			_scrollViewer.ScrollChanged += OnScrollChanged;
		}

		this.GetObservable(IsScrollResetRequestedProperty)
		    .Subscribe(
			    isResetRequested =>
			    {
				    if (isResetRequested)
				    {
					    _isProgrammaticScroll = true;
					    ResetLastOffset();
					    IsScrollResetRequested = false;
				    }
			    });
	}

	protected override void OnDetaching()
	{
		if (_scrollViewer != null)
		{
			_scrollViewer.ScrollChanged -= OnScrollChanged;
		}

		base.OnDetaching();
	}

	private void ResetLastOffset()
	{
		if (_scrollViewer == null || double.IsNaN(_scrollViewer.Offset.Y))
		{
			_lastOffset = 0;
			return;
		}
		_lastOffset = _scrollViewer.Offset.Y;
	}

	private async void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
	{
		if (_scrollViewer == null)
		{
			return;
		}

		if (_scrollViewer == null ||
		    _scrollViewer.DataContext is not GlancePanelViewModel pvm ||
		    !pvm.ImagePreviews.Any())
		{
			return;
		}

		// Ignore if program scroll
		if (_isProgrammaticScroll)
		{
			_isProgrammaticScroll = false;
			return;
		}

		// Debounce
		if (Math.Abs(_lastOffset - _scrollViewer.Offset.Y) < 324)
		{
			return;
		}

		_lastOffset = _scrollViewer.Offset.Y;

		if (_scrollViewer.DataContext is GlancePanelViewModel vm)
		{
			await vm.HandleScrollAsync(_scrollViewer.Offset.Y, _scrollViewer.Viewport.Height);
		}
	}
}