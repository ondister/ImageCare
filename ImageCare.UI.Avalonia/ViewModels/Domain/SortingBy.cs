using System.Collections.Generic;

using DynamicData.Binding;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal enum SortingBy
{
    NameDescending = 0,
    NameAscending = 1,
    DateTimeDescending = 2,
    DateTimeAscending = 3
}

public static class SortingByExtensions
{
    internal static IComparer<MediaPreviewViewModel> GetComparer(this SortingBy sortingBy)
    {
        switch (sortingBy)
        {
            case SortingBy.NameDescending:
                return SortExpressionComparer<MediaPreviewViewModel>.Descending(p => p.Url);
            case SortingBy.NameAscending:
                return SortExpressionComparer<MediaPreviewViewModel>.Ascending(p => p.Url);
            case SortingBy.DateTimeDescending:
                return SortExpressionComparer<MediaPreviewViewModel>.Descending(p => p.Metadata.CreationDateTime);
            case SortingBy.DateTimeAscending:
                return SortExpressionComparer<MediaPreviewViewModel>.Ascending(p => p.Metadata.CreationDateTime);
            default:
                return SortExpressionComparer<MediaPreviewViewModel>.Ascending(p => p.Metadata.CreationDateTime);
        }
    }
}