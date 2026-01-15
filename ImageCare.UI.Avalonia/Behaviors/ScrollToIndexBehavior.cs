using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;

namespace ImageCare.UI.Avalonia.Behaviors;

internal class ScrollToIndexBehavior : Behavior<ListBox>
{
    public static readonly StyledProperty<int> TargetIndexProperty =
        AvaloniaProperty.Register<ScrollToIndexBehavior, int>(
            nameof(TargetIndex),
            defaultValue: -1);

    public int TargetIndex
    {
        get => GetValue(TargetIndexProperty);
        set => SetValue(TargetIndexProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        TargetIndexProperty.Changed.AddClassHandler<ScrollToIndexBehavior>((x, e) => x.OnTargetIndexChanged(e));
    }

    private void OnTargetIndexChanged(AvaloniaPropertyChangedEventArgs e)
    {
        var newValue = (int)(e.NewValue ?? -1);

        if (AssociatedObject == null || newValue < 0 || newValue >= AssociatedObject.Items.Count)
        {
            return;
        }

        // Ждем отрисовки элементов
        AssociatedObject.LayoutUpdated += OnLayoutUpdated;
    }

    private void OnLayoutUpdated(object sender, EventArgs e)
    {
        if (AssociatedObject == null)
        {
            return;
        }

        AssociatedObject.LayoutUpdated -= OnLayoutUpdated;

        if (TargetIndex >= 0 && TargetIndex < AssociatedObject.Items.Count)
        {
            var container = AssociatedObject.ItemContainerGenerator.ContainerFromIndex(TargetIndex);
            if (container is Control control)
            {
                control.BringIntoView();
            }
        }
    }
}