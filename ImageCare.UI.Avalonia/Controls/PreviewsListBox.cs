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

        if (container?.Bounds != null && Scroll is not null)
        {
            Scroll.Offset = new Vector(SelectedIndex * container.Bounds.Width - container.Bounds.Width, Scroll.Offset.Y);
        }

        container?.Focus();
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