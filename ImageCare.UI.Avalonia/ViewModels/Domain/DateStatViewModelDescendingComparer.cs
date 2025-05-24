using System.Collections.Generic;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal class DateStatViewModelDescendingComparer : IComparer<DateStatViewModel>
{
	public int Compare(DateStatViewModel? x, DateStatViewModel? y)
	{
		if (x == null && y == null)
		{
			return 0;
		}

		if (x == null)
		{
			return 1;
		}

		if (y==null)
		{
			return -1;
		}

		return y.Date.CompareTo(x.Date);
	}
}