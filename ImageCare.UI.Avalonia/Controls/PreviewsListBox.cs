using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ImageCare.UI.Avalonia.Controls;

public class PreviewsListBox : ListBox
{
    private const double CenteringTolerance = 0.5;

    private object? _lastCenteredSelection;
    private long _centeringRequest;

    public PreviewsListBox()
    {
        PointerWheelChanged += OnPointerWheelChanged;
        SelectionChanged += OnSelectionChanged;
        SizeChanged += OnSizeChanged;
    }

    public double CarouselLeadingInset => Padding.Left;

    public void CenterItem(object item, bool focus = false)
    {
        var request = ++_centeringRequest;
        ScrollIntoView(item);

        Dispatcher.UIThread.Post(
            () => PrepareCentering(item, focus, request),
            DispatcherPriority.Loaded);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedItem == null)
        {
            _lastCenteredSelection = null;
            return;
        }

        if (ReferenceEquals(_lastCenteredSelection, SelectedItem))
        {
            return;
        }

        _lastCenteredSelection = SelectedItem;
        CenterItem(SelectedItem, focus: true);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (SelectedItem is { } selectedItem)
        {
            CenterItem(selectedItem);
        }
    }

    private void PrepareCentering(object item, bool focus, long request)
    {
        if (request != _centeringRequest)
        {
            return;
        }

        ScrollIntoView(item);
        Dispatcher.UIThread.Post(
            () => CenterRealizedItem(item, focus, request),
            DispatcherPriority.Background);
    }

    private void CenterRealizedItem(object item, bool focus, long request)
    {
        if (request != _centeringRequest || ContainerFromItem(item) is not Control container)
        {
            return;
        }

        var scrollViewer = this.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer == null || scrollViewer.Viewport.Width <= 0)
        {
            return;
        }

        if (UpdateCarouselPadding(scrollViewer, container))
        {
            Dispatcher.UIThread.Post(
                () => CenterRealizedItem(item, focus, request),
                DispatcherPriority.Background);
            return;
        }

        if (focus)
        {
            container.Focus();
        }

        var position = container.TransformToVisual(scrollViewer)?.Transform(new Point(0, 0));
        if (!position.HasValue)
        {
            return;
        }

        var containerCenter = position.Value.X + container.Bounds.Width / 2;
        var desiredOffset = scrollViewer.Offset.X + containerCenter - scrollViewer.Viewport.Width / 2;
        var maximumOffset = Math.Max(0, scrollViewer.Extent.Width - scrollViewer.Viewport.Width);
        desiredOffset = Math.Clamp(desiredOffset, 0, maximumOffset);

        if (Math.Abs(scrollViewer.Offset.X - desiredOffset) <= CenteringTolerance)
        {
            return;
        }

        scrollViewer.Offset = new Vector(desiredOffset, scrollViewer.Offset.Y);
    }

    private bool UpdateCarouselPadding(ScrollViewer scrollViewer, Control container)
    {
        var itemWidth = container.Bounds.Width + container.Margin.Left + container.Margin.Right;
        var horizontalPadding = Math.Max(0, (scrollViewer.Viewport.Width - itemWidth) / 2);
        if (Math.Abs(Padding.Left - horizontalPadding) <= CenteringTolerance &&
            Math.Abs(Padding.Right - horizontalPadding) <= CenteringTolerance)
        {
            return false;
        }

        Padding = new Thickness(horizontalPadding, Padding.Top, horizontalPadding, Padding.Bottom);
        return true;
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
