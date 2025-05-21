using System.Collections.Generic;

using ImageCare.Core.Domain.Folders;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal class FileModelCreationDateTimeDescendingComparer : IComparer<FileModel>
{
	public int Compare(FileModel? x, FileModel? y)
	{
		if (x?.CreatedDateTime == null && y?.CreatedDateTime == null)
		{
			return 0;
		}

		if (x?.CreatedDateTime == null)
		{
			return 1;
		}

		if (y?.CreatedDateTime == null)
		{
			return -1;
		}

		return y.CreatedDateTime.Value.CompareTo(x.CreatedDateTime.Value);
	}
}