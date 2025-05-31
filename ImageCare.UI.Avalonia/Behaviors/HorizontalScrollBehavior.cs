using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;

using ImageCare.UI.Avalonia.ViewModels;

namespace ImageCare.UI.Avalonia.Behaviors;

public class HorizontalScrollBehavior : Behavior<ScrollViewer>
{
	public static readonly StyledProperty<bool> IsScrollResetRequestedProperty =
		AvaloniaProperty.Register<HorizontalScrollBehavior, bool>(nameof(IsScrollResetRequested));
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
		if (_scrollViewer == null || double.IsNaN(_scrollViewer.Offset.X))
		{
			_lastOffset = 0;
			return;
		}
		_lastOffset = _scrollViewer.Offset.X;
	}

	private async void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
	{
		if (_scrollViewer == null)
		{
			return;
		}

		if (_scrollViewer == null ||
		    _scrollViewer.DataContext is not PreviewPanelViewModel pvm ||
		    !pvm.ImagePreviews.Any())
		{
			return;
		}

		// Игнорируем событие, если это программный скролл
		if (_isProgrammaticScroll)
		{
			_isProgrammaticScroll = false;
			return;
		}

		// Дебаунсинг
		if (Math.Abs(_lastOffset - _scrollViewer.Offset.X) < 324)
		{
			return;
		}

		_lastOffset = _scrollViewer.Offset.X;

		if (_scrollViewer.DataContext is PreviewPanelViewModel vm)
		{
			await vm.HandleScroll(_scrollViewer.Offset.X, _scrollViewer.Viewport.Width);
		}
	}
}