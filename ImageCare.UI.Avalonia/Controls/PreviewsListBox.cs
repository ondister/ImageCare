using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ImageCare.UI.Avalonia.Controls;

public class PreviewsListBox : ListBox
{
    public PreviewsListBox()
    {
        PointerWheelChanged += OnPointerWheelChanged;
        SelectionChanged += OnSelectionChanged;
    }

    private  void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
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

        var scrollViewer = this.Parent as ScrollViewer;
        if (scrollViewer == null)
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
        var index = SelectedIndex + (e.Delta.Y < 0 ? 1 : -1);

        var validatedIndex = ValidateIndex(index);
        if (validatedIndex == -1)
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

        var indicesCount = Items.Count - 1;

        if (index < 0)
        {
            return 0;
        }

        return index > indicesCount ? indicesCount : index;
    }
}