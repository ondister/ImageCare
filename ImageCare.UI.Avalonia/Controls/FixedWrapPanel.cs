using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ImageCare.UI.Avalonia.Controls;

public enum AspectRatio
{
    Ratio4x3,
    Ratio3x2,
    Ratio16x9,
    Ratio1x1
}

public class FixedWrapPanel : Panel, INavigableContainer
{
    public static readonly StyledProperty<int> ItemsPerLineProperty =
        AvaloniaProperty.Register<FixedWrapPanel, int>(nameof(ItemsPerLine), 3);

    public static readonly StyledProperty<AspectRatio> AspectRatioProperty =
        AvaloniaProperty.Register<FixedWrapPanel, AspectRatio>(nameof(AspectRatio));

    public static readonly DirectProperty<FixedWrapPanel, double> RowHeightProperty =
        AvaloniaProperty.RegisterDirect<FixedWrapPanel, double>(
            nameof(RowHeight),
            o => o.RowHeight);

    private double _rowHeight;

    static FixedWrapPanel()
    {
        AffectsMeasure<FixedWrapPanel>(ItemsPerLineProperty, AspectRatioProperty);
        AffectsArrange<FixedWrapPanel>(ItemsPerLineProperty, AspectRatioProperty);
    }

    public FixedWrapPanel()
    {

        _rowHeight = CalculateRowHeight(800);
    }

    public int ItemsPerLine
    {
        get => GetValue(ItemsPerLineProperty);
        set => SetValue(ItemsPerLineProperty, value);
    }

    public AspectRatio AspectRatio
    {
        get => GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    public double RowHeight
    {
        get => _rowHeight;
        private set => SetAndRaise(RowHeightProperty, ref _rowHeight, value);
    }

    IInputElement INavigableContainer.GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        var index = from is not null ? Children.IndexOf((Control)from) : -1;
        switch (direction)
        {
            case NavigationDirection.First:
                index = 0;
                break;
            case NavigationDirection.Last:
                index = Children.Count - 1;
                break;
            case NavigationDirection.Next:
                ++index;
                break;
            case NavigationDirection.Previous:
                --index;
                break;
            case NavigationDirection.Left:
                index -= 1;
                break;
            case NavigationDirection.Right:
                index += 1;
                break;
            case NavigationDirection.Up:
                index = -1;
                break;
            case NavigationDirection.Down:
                index = -1;
                break;
        }

        if (index >= 0 && index < Children.Count)
        {
            return Children[index];
        }

        return this;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (double.IsInfinity(constraint.Width) || double.IsNaN(constraint.Width))
        {
            // Use default
            constraint = new Size(800, constraint.Height);
        }

        var itemWidth = constraint.Width / ItemsPerLine;
        var aspectRatio = GetAspectRatioValue();
        var itemHeight = itemWidth * aspectRatio;

        RowHeight = itemHeight;

        var panelWidth = constraint.Width;
        var totalRows = (int)Math.Ceiling((double)Children.Count / ItemsPerLine);
        var panelHeight = totalRows * itemHeight;

        var childSize = new Size(itemWidth, itemHeight);
        foreach (var child in Children)
        {
            if (child != null)
            {
                child.Measure(childSize);
            }
        }

        return new Size(panelWidth, panelHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
        {
            return finalSize;
        }

        var itemWidth = finalSize.Width / ItemsPerLine;
        var aspectRatio = GetAspectRatioValue();
        var itemHeight = itemWidth * aspectRatio;

        RowHeight = itemHeight;

        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];

            var row = i / ItemsPerLine;
            var column = i % ItemsPerLine;

            var x = column * itemWidth;
            var y = row * itemHeight;

            child.Arrange(new Rect(x, y, itemWidth, itemHeight));
        }

        var totalRows = (int)Math.Ceiling((double)Children.Count / ItemsPerLine);
        var totalHeight = totalRows * itemHeight;

        return new Size(finalSize.Width, totalHeight);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsPerLineProperty || change.Property == AspectRatioProperty)
        {
            InvalidateMeasure();
            InvalidateArrange();
        }
    }

    private double GetAspectRatioValue()
    {
        return AspectRatio switch
        {
            AspectRatio.Ratio4x3 => 3.0 / 4.0,
            AspectRatio.Ratio3x2 => 2.0 / 3.0,
            AspectRatio.Ratio16x9 => 9.0 / 16.0,
            AspectRatio.Ratio1x1 => 1.0,
            _ => 3.0 / 4.0
        };
    }

    private double CalculateRowHeight(double availableWidth)
    {
        if (double.IsInfinity(availableWidth) || double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            availableWidth = 800;
        }

        var itemWidth = availableWidth / ItemsPerLine;
        var aspectRatio = GetAspectRatioValue();
        return itemWidth * aspectRatio;
    }
}