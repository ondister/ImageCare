using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Input;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

public class DateStatViewModel : ViewModelBase,IDisposable
{
    private DateTime _date;
    private int _count;
    private double _normalizedHeight;
    private bool _isLastInMonthYear;
    private readonly Subject<DateTime> _dateSelectedSubject;

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

    public string DisplayText => IsLastInMonthYear ? Date.ToString("MM.yy") : Date.Day.ToString();

    public bool IsLastInMonthYear
    {
        get => _isLastInMonthYear;
        set
        {
            if (SetProperty(ref _isLastInMonthYear, value))
            {
                RaisePropertyChanged(nameof(DisplayText));
            }
        }
    }

    public ICommand ColumnClickCommand => CreateCommand<DateStatViewModel>(item => { _dateSelectedSubject.OnNext(item.Date); });

    public DateStatViewModel()
    {
        _dateSelectedSubject = new Subject<DateTime>();
    }

    public IObservable<DateTime> DateSelected => _dateSelectedSubject.AsObservable();

    public void UpdateMonthYearFlag(IEnumerable<DateStatViewModel> allItems)
    {
        var lastInMonth = allItems
                          .Where(x => x.Date.Year == Date.Year && x.Date.Month == Date.Month)
                          .MaxBy(x => x.Date);

        IsLastInMonthYear = lastInMonth?.Date == Date;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dateSelectedSubject.Dispose();
    }
}