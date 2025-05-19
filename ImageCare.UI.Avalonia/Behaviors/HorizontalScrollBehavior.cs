using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ImageCare.UI.Avalonia.ViewModels;

namespace ImageCare.UI.Avalonia.Behaviors
{
	public class HorizontalScrollBehavior : Behavior<ScrollViewer>
	{
		private ScrollViewer? _scrollViewer;
		private double _lastOffset;

		protected override void OnAttached()
		{
			base.OnAttached();

			if (AssociatedObject is ScrollViewer scrollViewer)
			{
				_scrollViewer = scrollViewer;
				_scrollViewer.ScrollChanged += OnScrollChanged;
			}
		}

		protected override void OnDetaching()
		{
			if (_scrollViewer != null)
			{
				_scrollViewer.ScrollChanged -= OnScrollChanged;
			}

			base.OnDetaching();
		}

		private async void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
		{
			if (_scrollViewer == null)
				return;

			// Дебаунсинг, чтобы избежать слишком частых вызовов
			if (Math.Abs(_lastOffset - _scrollViewer.Offset.X) < 300)
				return;

			_lastOffset = _scrollViewer.Offset.X;

			if (_scrollViewer.DataContext is PreviewPanelViewModel vm)
			{
				await vm.HandleScroll(_scrollViewer.Offset.X, _scrollViewer.Viewport.Width);
			}
		}
	}
}
