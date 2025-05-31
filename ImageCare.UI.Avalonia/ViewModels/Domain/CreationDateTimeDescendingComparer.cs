using System.Collections.Generic;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal class CreationDateTimeDescendingComparer : IComparer<MediaPreviewViewModel>
{
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

		return y.FileDate.CompareTo(x.FileDate);
	}
}