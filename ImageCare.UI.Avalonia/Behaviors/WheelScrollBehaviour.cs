using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ImageCare.UI.Avalonia.Behaviors;

public sealed class WheelScrollBehaviour
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<WheelScrollBehaviour, ScrollViewer, bool>(
            "IsEnabled",
            defaultValue: false);

    static WheelScrollBehaviour()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(ScrollViewer scrollViewer) => scrollViewer.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ScrollViewer scrollViewer, bool value) =>
        scrollViewer.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(ScrollViewer scrollViewer, AvaloniaPropertyChangedEventArgs args)
    {
        scrollViewer.RemoveHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged);
        if (args.NewValue is true)
        {
            scrollViewer.AddHandler(
                InputElement.PointerWheelChangedEvent,
                OnPointerWheelChanged,
                RoutingStrategies.Tunnel);
        }
    }

    private static void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        var wheelDelta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y)
            ? e.Delta.X
            : e.Delta.Y;
        if (Math.Abs(wheelDelta) < double.Epsilon)
        {
            return;
        }

        var maximumOffset = Math.Max(0, scrollViewer.Extent.Width - scrollViewer.Viewport.Width);
        if (maximumOffset <= 0)
        {
            return;
        }

        var targetOffset = Math.Clamp(scrollViewer.Offset.X - wheelDelta * 40, 0, maximumOffset);
        if (Math.Abs(targetOffset - scrollViewer.Offset.X) >= 0.01)
        {
            scrollViewer.Offset = new Vector(targetOffset, scrollViewer.Offset.Y);
        }

        e.Handled = true;
    }
}
