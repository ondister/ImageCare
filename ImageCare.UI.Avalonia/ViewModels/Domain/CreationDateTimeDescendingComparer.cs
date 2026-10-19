using System;
using System.Collections.Generic;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal class CreationDateTimeDescendingComparer : IComparer<MediaPreviewViewModel>
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

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

        var dateComparison = y.FileDate.CompareTo(x.FileDate);
        return dateComparison != 0
            ? dateComparison
            : PathComparer.Compare(x.Url, y.Url);
    }
}
