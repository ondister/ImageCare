using System;
using System.Globalization;

using Avalonia.Data.Converters;

using IconPacks.Avalonia.Lucide;

using ImageCare.UI.Avalonia.ViewModels.Domain;

namespace ImageCare.UI.Avalonia.Converters;

internal class SortingByToIconKindConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SortingBy sortingBy)
        {
            return null;
        }

        switch (sortingBy)
        {
            case SortingBy.NameDescending:
                return PackIconLucideKind.ArrowUpZA;
            case SortingBy.NameAscending:
                return PackIconLucideKind.ArrowDownAZ;
            case SortingBy.DateTimeDescending:
                return PackIconLucideKind.ArrowUp10;
            case SortingBy.DateTimeAscending:
                return PackIconLucideKind.ArrowDown01;
            default:
                return null;
        }

        return null;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}