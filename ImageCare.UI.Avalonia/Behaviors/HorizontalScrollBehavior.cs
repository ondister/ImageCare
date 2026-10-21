using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using ImageCare.UI.Avalonia.ViewModels;

namespace ImageCare.UI.Avalonia.Behaviors;

public sealed class HorizontalScrollBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<HorizontalScrollBehavior, Control, bool>(
            "IsEnabled",
            defaultValue: false);

    public static readonly AttachedProperty<bool> IsScrollResetRequestedProperty =
        AvaloniaProperty.RegisterAttached<HorizontalScrollBehavior, Control, bool>(
            "IsScrollResetRequested",
            defaultValue: false);

    private static readonly AttachedProperty<State?> StateProperty =
        AvaloniaProperty.RegisterAttached<HorizontalScrollBehavior, Control, State?>("State");

    static HorizontalScrollBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
        IsScrollResetRequestedProperty.Changed.AddClassHandler<Control>(OnResetRequestedChanged);
    }

    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

    public static bool GetIsScrollResetRequested(Control control) =>
        control.GetValue(IsScrollResetRequestedProperty);

    public static void SetIsScrollResetRequested(Control control, bool value) =>
        control.SetValue(IsScrollResetRequestedProperty, value);

    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        control.GetValue(StateProperty)?.Dispose();
        control.ClearValue(StateProperty);

        if (args.NewValue is true)
        {
            control.SetValue(StateProperty, new State(control));
        }
    }

    private static void OnResetRequestedChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is true)
        {
            control.GetValue(StateProperty)?.ResetLastOffset();
            SetIsScrollResetRequested(control, false);
        }
    }

    private sealed class State : IDisposable
    {
        private readonly Control _owner;
        private ScrollViewer? _scrollViewer;
        private double _lastOffset;
        private bool _isProgrammaticScroll;

        public State(Control owner)
        {
            _owner = owner;
            _owner.Loaded += OnLoaded;
            _owner.Unloaded += OnUnloaded;
            AttachScrollViewer();
        }

        public void Dispose()
        {
            _owner.Loaded -= OnLoaded;
            _owner.Unloaded -= OnUnloaded;
            DetachScrollViewer();
        }

        public void ResetLastOffset()
        {
            _isProgrammaticScroll = true;
            _lastOffset = _scrollViewer == null || double.IsNaN(_scrollViewer.Offset.X)
                ? 0
                : _scrollViewer.Offset.X;
        }

        private void OnLoaded(object? sender, RoutedEventArgs args) => AttachScrollViewer();

        private void OnUnloaded(object? sender, RoutedEventArgs args) => DetachScrollViewer();

        private void AttachScrollViewer()
        {
            var scrollViewer = _owner as ScrollViewer
                ?? _owner.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (ReferenceEquals(scrollViewer, _scrollViewer))
            {
                return;
            }

            DetachScrollViewer();
            _scrollViewer = scrollViewer;
            if (_scrollViewer != null)
            {
                _scrollViewer.ScrollChanged += OnScrollChanged;
            }
        }

        private void DetachScrollViewer()
        {
            if (_scrollViewer != null)
            {
                _scrollViewer.ScrollChanged -= OnScrollChanged;
                _scrollViewer = null;
            }
        }

        private async void OnScrollChanged(object? sender, ScrollChangedEventArgs args)
        {
            if (_scrollViewer == null ||
                _owner.DataContext is not PreviewPanelViewModel viewModel ||
                viewModel.ImagePreviews.Count == 0)
            {
                return;
            }

            if (_isProgrammaticScroll)
            {
                _isProgrammaticScroll = false;
                return;
            }

            if (Math.Abs(_lastOffset - _scrollViewer.Offset.X) < 150)
            {
                return;
            }

            _lastOffset = _scrollViewer.Offset.X;
            await viewModel.HandleScrollAsync(_scrollViewer.Offset.X, _scrollViewer.Viewport.Width);
        }
    }
}
