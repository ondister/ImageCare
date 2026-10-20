using System;
using System.Collections.Generic;

using ImageCare.Core.Domain.Configuration;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal sealed class CreationDateTimeComparer : IComparer<MediaPreviewViewModel>
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private readonly MediaSortDirection _direction;

    public CreationDateTimeComparer(MediaSortDirection direction)
    {
        _direction = direction;
    }

    public int Compare(MediaPreviewViewModel? x, MediaPreviewViewModel? y)
    {
        if (x == null && y == null)
        {
            return 0;
        }

        if (x == null)
        {
            return 1;
        }

        if (y == null)
        {
            return -1;
        }

        var dateComparison = _direction == MediaSortDirection.OldestFirst
            ? x.FileDate.CompareTo(y.FileDate)
            : y.FileDate.CompareTo(x.FileDate);
        return dateComparison != 0
            ? dateComparison
            : PathComparer.Compare(x.Url, y.Url);
    }
}
