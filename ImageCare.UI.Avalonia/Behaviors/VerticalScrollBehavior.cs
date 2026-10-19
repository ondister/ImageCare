using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;

using ImageCare.UI.Avalonia.ViewModels;

namespace ImageCare.UI.Avalonia.Behaviors;

public sealed class VerticalScrollBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<VerticalScrollBehavior, ScrollViewer, bool>(
            "IsEnabled",
            defaultValue: false);

    public static readonly AttachedProperty<bool> IsScrollResetRequestedProperty =
        AvaloniaProperty.RegisterAttached<VerticalScrollBehavior, ScrollViewer, bool>(
            "IsScrollResetRequested",
            defaultValue: false);

    private static readonly AttachedProperty<State?> StateProperty =
        AvaloniaProperty.RegisterAttached<VerticalScrollBehavior, ScrollViewer, State?>("State");

    static VerticalScrollBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>(OnIsEnabledChanged);
        IsScrollResetRequestedProperty.Changed.AddClassHandler<ScrollViewer>(OnResetRequestedChanged);
    }

    public static bool GetIsEnabled(ScrollViewer scrollViewer) => scrollViewer.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ScrollViewer scrollViewer, bool value) =>
        scrollViewer.SetValue(IsEnabledProperty, value);

    public static bool GetIsScrollResetRequested(ScrollViewer scrollViewer) =>
        scrollViewer.GetValue(IsScrollResetRequestedProperty);

    public static void SetIsScrollResetRequested(ScrollViewer scrollViewer, bool value) =>
        scrollViewer.SetValue(IsScrollResetRequestedProperty, value);

    private static void OnIsEnabledChanged(ScrollViewer scrollViewer, AvaloniaPropertyChangedEventArgs args)
    {
        scrollViewer.GetValue(StateProperty)?.Dispose();
        scrollViewer.ClearValue(StateProperty);

        if (args.NewValue is true)
        {
            scrollViewer.SetValue(StateProperty, new State(scrollViewer));
        }
    }

    private static void OnResetRequestedChanged(ScrollViewer scrollViewer, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is true)
        {
            scrollViewer.GetValue(StateProperty)?.ResetLastOffset();
            SetIsScrollResetRequested(scrollViewer, false);
        }
    }

    private sealed class State : IDisposable
    {
        private readonly ScrollViewer _scrollViewer;
        private double _lastOffset;
        private bool _isProgrammaticScroll;

        public State(ScrollViewer scrollViewer)
        {
            _scrollViewer = scrollViewer;
            _scrollViewer.ScrollChanged += OnScrollChanged;
        }

        public void Dispose()
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        }

        public void ResetLastOffset()
        {
            _isProgrammaticScroll = true;
            _lastOffset = double.IsNaN(_scrollViewer.Offset.Y) ? 0 : _scrollViewer.Offset.Y;
        }

        private async void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (_scrollViewer.DataContext is not GlancePanelViewModel viewModel ||
                !viewModel.ImagePreviews.Any())
            {
                return;
            }

            if (_isProgrammaticScroll)
            {
                _isProgrammaticScroll = false;
                return;
            }

            if (Math.Abs(_lastOffset - _scrollViewer.Offset.Y) < 150)
            {
                return;
            }

            _lastOffset = _scrollViewer.Offset.Y;
            await viewModel.HandleScrollAsync(_scrollViewer.Offset.Y, _scrollViewer.Viewport.Height);
        }
    }
}
