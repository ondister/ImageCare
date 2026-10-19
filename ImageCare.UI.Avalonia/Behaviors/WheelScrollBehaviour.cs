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
        if (sender is ScrollViewer scrollViewer)
        {
            scrollViewer.Offset += new Vector(e.Delta.Y * 30, 0);
            e.Handled = true;
        }
    }
}
