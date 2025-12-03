using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ImageCare.UI.Avalonia.Controls;

public class FixedWrapPanel : Panel, INavigableContainer
{
    public static readonly StyledProperty<int> ItemsPerLineProperty =
        AvaloniaProperty.Register<FixedWrapPanel, int>(nameof(ItemsPerLine), 3);

    static FixedWrapPanel()
    {
        AffectsMeasure<FixedWrapPanel>(ItemsPerLineProperty);
    }

    public int ItemsPerLine
    {
        get => GetValue(ItemsPerLineProperty);
        set => SetValue(ItemsPerLineProperty, value);
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
        var itemWidth = constraint.Width / ItemsPerLine;
        MutableSize currentLineSize = new();
        MutableSize panelSize = new();
        Size lineConstraint = new(constraint.Width, constraint.Height);
        Size childConstraint = new(itemWidth, constraint.Height);

        // Список для хранения высот элементов в текущей строке
        List<double> lineHeights = new();

        for (int i = 0, count = Children.Count; i < count; i++)
        {
            var child = Children[i];
            if (child is null)
            {
                continue;
            }

            child.Measure(childConstraint);
            Size childSize = new(itemWidth, child.DesiredSize.Height);

            if (MathUtilities.GreaterThan(currentLineSize.Width + childSize.Width, lineConstraint.Width))
            {
                // Переход на новую строку
                if (lineHeights.Count > 0)
                {
                    // Высота строки = минимальная высота в строке (горизонтальные изображения)
                    currentLineSize.Height = lineHeights.Min();

                    // Обновляем высоту всех элементов в строке
                    for (int j = i - lineHeights.Count; j < i; j++)
                    {
                        Children[j].Measure(new Size(itemWidth, currentLineSize.Height));
                    }
                }

                panelSize.Width = Math.Max(currentLineSize.Width, panelSize.Width);
                panelSize.Height += currentLineSize.Height;

                // Начинаем новую строку
                currentLineSize = new MutableSize(childSize);
                lineHeights.Clear();
                lineHeights.Add(childSize.Height);
            }
            else
            {
                // Продолжаем накапливать строку
                currentLineSize.Width += childSize.Width;
                lineHeights.Add(childSize.Height);

                // Временно используем максимальную высоту для расчетов
                currentLineSize.Height = Math.Max(childSize.Height, currentLineSize.Height);
            }
        }

        // Обработка последней строки
        if (lineHeights.Count > 0)
        {
            currentLineSize.Height = lineHeights.Min();

            // Обновляем высоту всех элементов в последней строке
            int lastLineStart = Children.Count - lineHeights.Count;
            for (int j = lastLineStart; j < Children.Count; j++)
            {
                Children[j].Measure(new Size(itemWidth, currentLineSize.Height));
            }
        }

        panelSize.Width = Math.Max(currentLineSize.Width, panelSize.Width);
        panelSize.Height += currentLineSize.Height;

        return panelSize.ToSize();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var itemWidth = finalSize.Width / ItemsPerLine;
        var firstInLine = 0;
        double accumulatedHeight = 0;
        var currentLineSize = new MutableSize();

        // Список для хранения высот элементов в текущей строке
        List<double> lineHeights = new();

        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            if (child == null)
            {
                continue;
            }

            MutableSize itemSize = new(itemWidth, child.DesiredSize.Height);

            if (MathUtilities.GreaterThan(currentLineSize.Width + itemSize.Width, finalSize.Width))
            {
                // Переход на новую строку
                if (lineHeights.Count > 0)
                {
                    // Высота строки = минимальная высота в строке
                    currentLineSize.Height = lineHeights.Min();

                    // Аранжируем строку с общей высотой
                    ArrangeLine(accumulatedHeight, currentLineSize.Height, firstInLine, i, itemWidth);
                    accumulatedHeight += currentLineSize.Height;
                }

                // Начинаем новую строку
                currentLineSize = itemSize;
                lineHeights.Clear();
                lineHeights.Add(itemSize.Height);
                firstInLine = i;
            }
            else
            {
                // Продолжаем накапливать строку
                currentLineSize.Width += itemSize.Width;
                lineHeights.Add(itemSize.Height);
                currentLineSize.Height = Math.Max(itemSize.Height, currentLineSize.Height);
            }
        }

        if (firstInLine < Children.Count && lineHeights.Count > 0)
        {
            // Аранжируем последнюю строку
            currentLineSize.Height = lineHeights.Min();
            ArrangeLine(accumulatedHeight, currentLineSize.Height, firstInLine, Children.Count, itemWidth);
        }

        return finalSize;
    }

    private void ArrangeLine(double y, double height, int start, int end, double width)
    {
        double x = 0;
        for (var i = start; i < end; i++)
        {
            var child = Children[i];
            if (child == null)
            {
                continue;
            }

            child.Arrange(new Rect(x, y, width, height));
            x += width;
        }
    }

    private struct MutableSize
    {
        internal MutableSize(double width, double height)
        {
            Width = width;
            Height = height;
        }

        internal MutableSize(Size size)
        {
            Width = size.Width;
            Height = size.Height;
        }

        internal double Width;
        internal double Height;

        internal Size ToSize()
        {
            return new Size(Width, Height);
        }
    }
}