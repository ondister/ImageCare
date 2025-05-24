using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Windows.Input;

using ImageCare.Core.Domain.Folders;
using ImageCare.Mvvm;
using ImageCare.Mvvm.Collections;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Prism.Commands;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class TimelineViewModel : ViewModelBase, IDisposable
{
	private readonly SynchronizationContext _synchronizationContext;
	private readonly Subject<DateTime> _dateSelectedSubject;

	public TimelineViewModel()
		: this(SynchronizationContext.Current ?? new SynchronizationContext()) { }

	public TimelineViewModel(SynchronizationContext synchronizationContext)
	{
		_synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));
		DateStatViewModels = new SortedObservableCollection<DateStatViewModel>(new DateStatViewModelDescendingComparer());
		_dateSelectedSubject = new Subject<DateTime>();
	}

	public ICommand ColumnClickCommand => new DelegateCommand<DateStatViewModel>(
		item =>
		{
			_dateSelectedSubject.OnNext(item.Date);
		});

	public IObservable<DateTime> DateSelected => _dateSelectedSubject.AsObservable();

	public SortedObservableCollection<DateStatViewModel> DateStatViewModels { get; }

	public void AddFile(FileModel file)
	{
		if (file.CreatedDateTime == null)
		{
			return;
		}

		var date = file.CreatedDateTime.Value.Date;

		_synchronizationContext.Post(
			_ =>
			{
				var existingItem = DateStatViewModels.FirstOrDefault(x => x.Date == date);

				if (existingItem != null)
				{
					existingItem.Count++;
				}
				else
				{
					DateStatViewModels.InsertItem(
						new DateStatViewModel
						{
							Date = date,
							Count = 1
						});
				}

				UpdateDateFlags();
				UpdateNormalizedHeights();
			},
			null);
	}

	public void AddFiles(IEnumerable<FileModel> files)
	{
		_synchronizationContext.Post(
			_ =>
			{
				foreach (var file in files)
				{
					if (file.CreatedDateTime == null)
					{
						continue;
					}

					var date = file.CreatedDateTime.Value.Date;
					var existingItem = DateStatViewModels.FirstOrDefault(x => x.Date == date);

					if (existingItem != null)
					{
						existingItem.Count++;
					}
					else
					{
						DateStatViewModels.InsertItem(
							new DateStatViewModel
							{
								Date = date,
								Count = 1
							});
					}
				}

				UpdateDateFlags();
				UpdateNormalizedHeights();
			},
			null);
	}

	public void RemoveFile(FileModel file)
	{
		if (file.CreatedDateTime == null)
		{
			return;
		}

		var date = file.CreatedDateTime.Value.Date;

		_synchronizationContext.Post(
			_ =>
			{
				var existingItem = DateStatViewModels.FirstOrDefault(x => x.Date == date);

				if (existingItem != null)
				{
					existingItem.Count--;

					if (existingItem.Count <= 0)
					{
						DateStatViewModels.Remove(existingItem);
					}

					UpdateNormalizedHeights();
				}
			},
			null);
	}

	public void Clear()
	{
		_synchronizationContext.Post(
			_ => { DateStatViewModels.Clear(); },
			null);
	}

	public static double Normalize(double x, double min, double max, double a, double b)
	{
		if (Math.Abs(max - min) < double.Epsilon)
		{
			return (a + b) / 2d;
		}

		var result = a + (x - min) * (b - a) / (max - min);

		if (double.IsNaN(result) || double.IsInfinity(result))
		{
			return a;
		}

		return result;
	}

	private void UpdateDateFlags()
	{
		foreach (var item in DateStatViewModels)
		{
			item.UpdateMonthYearFlag(DateStatViewModels);
		}
	}

	private void UpdateNormalizedHeights()
	{
		if (DateStatViewModels.Count == 0)
		{
			return;
		}

		foreach (var item in DateStatViewModels)
		{
			var normalizedHeight = Normalize(item.Count, DateStatViewModels.Min(s => s.Count), DateStatViewModels.Max(s => s.Count), 0, 40); //40 - max bar height

			item.NormalizedHeight = Math.Max(normalizedHeight, 4);
		}
	}

	public void Dispose()
	{
		_dateSelectedSubject.Dispose();
	}
}