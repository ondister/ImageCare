using System;
using System.Collections.Generic;

using ImageCare.Core.Domain.Folders;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal class FileModelCreationDateTimeDescendingComparer : IComparer<FileModel>
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

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

        var dateComparison = Nullable.Compare(y.CreatedDateTime, x.CreatedDateTime);
        return dateComparison != 0
            ? dateComparison
            : PathComparer.Compare(x.FullName, y.FullName);
    }
}
