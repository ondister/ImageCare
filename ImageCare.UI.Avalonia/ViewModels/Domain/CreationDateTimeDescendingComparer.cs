using System.Collections.Generic;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal class CreationDateTimeDescendingComparer : IComparer<MediaPreviewViewModel>
{
	public int Compare(MediaPreviewViewModel? x, MediaPreviewViewModel? y)
	{
		if (x?.Metadata == null && y?.Metadata == null)
		{
			return 0;
		}

		if (x?.Metadata == null)
		{
			return 1;
		}

		if (y?.Metadata == null)
		{
			return -1;
		}

		return y.Metadata.CreationDateTime.CompareTo(x.Metadata.CreationDateTime);
	}
}