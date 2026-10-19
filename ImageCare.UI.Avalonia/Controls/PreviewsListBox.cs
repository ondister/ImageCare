using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ImageCare.UI.Avalonia.Controls;

public class PreviewsListBox : ListBox
{
    public PreviewsListBox()
    {
        PointerWheelChanged += OnPointerWheelChanged;
        SelectionChanged += OnSelectionChanged;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedItem == null)
        {
            return;
        }

        var container = ContainerFromItem(SelectedItem);
        if (container == null)
        {
            return;
        }

        container.Focus();

        if (Parent is not ScrollViewer scrollViewer)
        {
            return;
        }

        var containerPosition = container.TransformToVisual(scrollViewer)?.Transform(new Point(0, 0));
        if (!containerPosition.HasValue)
        {
            return;
        }

        var containerCenter = containerPosition.Value.X + container.Bounds.Width / 2;
        var scrollViewerCenter = scrollViewer.Bounds.Width / 2;
        var offset = containerCenter - scrollViewerCenter;

        scrollViewer.Offset = new Vector(scrollViewer.Offset.X + offset, scrollViewer.Offset.Y);
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (Items.Count == 0)
        {
            return;
        }

        var delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y)
            ? e.Delta.X
            : e.Delta.Y;
        if (Math.Abs(delta) < double.Epsilon)
        {
            return;
        }

        // SelectionChanged performs the only scroll by centering the selected item.
        // Prevent the parent ScrollViewer from moving without changing selection.
        e.Handled = true;

        var index = SelectedIndex < 0
            ? 0
            : SelectedIndex + (delta < 0 ? 1 : -1);
        var validatedIndex = ValidateIndex(index);
        if (validatedIndex == -1 || validatedIndex == SelectedIndex)
        {
            return;
        }

        SelectedIndex = validatedIndex;
    }

    private int ValidateIndex(int index)
    {
        if (Items.Count == 0)
        {
            return -1;
        }

        var lastIndex = Items.Count - 1;
        if (index < 0)
        {
            return 0;
        }

        return index > lastIndex ? lastIndex : index;
    }
}
