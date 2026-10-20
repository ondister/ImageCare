using System;
using System.Collections.Generic;

using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.Folders;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal sealed class FileModelCreationDateTimeComparer : IComparer<FileModel>
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private readonly MediaSortDirection _direction;

    public FileModelCreationDateTimeComparer(MediaSortDirection direction)
    {
        _direction = direction;
    }

    public int Compare(FileModel? x, FileModel? y)
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

        if (!x.CreatedDateTime.HasValue || !y.CreatedDateTime.HasValue)
        {
            if (!x.CreatedDateTime.HasValue && y.CreatedDateTime.HasValue)
            {
                return 1;
            }

            if (x.CreatedDateTime.HasValue && !y.CreatedDateTime.HasValue)
            {
                return -1;
            }
        }

        var dateComparison = _direction == MediaSortDirection.OldestFirst
            ? Nullable.Compare(x.CreatedDateTime, y.CreatedDateTime)
            : Nullable.Compare(y.CreatedDateTime, x.CreatedDateTime);
        return dateComparison != 0
            ? dateComparison
            : PathComparer.Compare(x.FullName, y.FullName);
    }
}
