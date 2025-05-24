using System;
using System.Collections.Generic;
using System.Linq;

using ImageCare.Mvvm;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

public class DateStatViewModel:ViewModelBase
{
	private DateTime _date;
	private int _count;
	private double _normalizedHeight;

	public DateTime Date
	{
		get => _date;
		set => SetProperty(ref _date, value);
	}

	public int Count	
	{
		get => _count;
		set => SetProperty(ref _count, value);
	}

	public double NormalizedHeight
	{
		get => _normalizedHeight;
		set => SetProperty(ref _normalizedHeight, value);
	}

	public string DisplayText =>
		IsLastInMonthYear ? Date.ToString("MM.yy") : Date.Day.ToString();

	public bool IsLastInMonthYear { get; private set; }

	public void UpdateMonthYearFlag(IEnumerable<DateStatViewModel> allItems)
	{
		var lastInMonth = allItems
			.FirstOrDefault(x => x.Date.Year == Date.Year && x.Date.Month == Date.Month);

		IsLastInMonthYear = (lastInMonth?.Date == Date);
	}
}