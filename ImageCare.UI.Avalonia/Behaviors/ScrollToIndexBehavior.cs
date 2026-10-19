using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ImageCare.UI.Avalonia.Behaviors;

internal sealed class ScrollToIndexBehavior
{
    public static readonly AttachedProperty<int> TargetIndexProperty =
        AvaloniaProperty.RegisterAttached<ScrollToIndexBehavior, ListBox, int>(
            "TargetIndex",
            defaultValue: -1);

    static ScrollToIndexBehavior()
    {
        TargetIndexProperty.Changed.AddClassHandler<ListBox>(OnTargetIndexChanged);
    }

    public static int GetTargetIndex(ListBox listBox) => listBox.GetValue(TargetIndexProperty);

    public static void SetTargetIndex(ListBox listBox, int value) => listBox.SetValue(TargetIndexProperty, value);

    private static void OnTargetIndexChanged(ListBox listBox, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is not int index || index < 0 || index >= listBox.Items.Count)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => listBox.ScrollIntoView(index), DispatcherPriority.Loaded);
    }
}
