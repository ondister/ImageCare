using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Windows.Input;

using DynamicData;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.FolderStatisticsService;
using ImageCare.Mvvm;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Prism.Commands;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class TimelineViewModel : ViewModelBase, IDisposable
{
    private readonly SynchronizationContext _synchronizationContext;
    private readonly Subject<DateTime> _dateSelectedSubject;
    private readonly CompositeDisposable _disposables = new();
    private readonly IFolderStatisticsService _folderStatisticsService;
    private readonly SourceCache<DateStatViewModel, DateTime> _dateStatsCache;
    private int _totalFilesCount;
    private bool _isLoading;

    public TimelineViewModel(IFolderStatisticsService folderStatisticsService, SynchronizationContext synchronizationContext)
    {
        _folderStatisticsService = folderStatisticsService ?? throw new ArgumentNullException(nameof(folderStatisticsService));
        _synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));

        _dateStatsCache = new SourceCache<DateStatViewModel, DateTime>(x => x.Date);

        _dateStatsCache.Connect()
                       .Sort(new DateStatViewModelDescendingComparer())
                       .Bind(out var dateStatViewModels)
                       .Subscribe()
                       .DisposeWith(_disposables);

        DateStatViewModels = dateStatViewModels;
        _dateSelectedSubject = new Subject<DateTime>();

        _folderStatisticsService.BucketChanged
                                .Buffer(TimeSpan.FromMilliseconds(250))
                                .Where(bufferedBuckets => bufferedBuckets.Count > 0)
                                .Subscribe(OnBucketsChanged)
                                .DisposeWith(_disposables);

        _folderStatisticsService.ScanProgress
                                .Subscribe(OnScanProgressChanged)
                                .DisposeWith(_disposables);
        _folderStatisticsService.TotalFilesCount
                                .Sample(TimeSpan.FromMilliseconds(250))
                                .ObserveOn(_synchronizationContext)
                                .Subscribe(totalFiles => TotalFilesCount = totalFiles)
                                .DisposeWith(_disposables);
    }

    public ICommand ColumnClickCommand => new DelegateCommand<DateStatViewModel>(item => { _dateSelectedSubject.OnNext(item.Date); });

    public IObservable<DateTime> DateSelected => _dateSelectedSubject.AsObservable();

    public IReadOnlyCollection<DateStatViewModel> DateStatViewModels { get; }

    public int TotalFilesCount
    {
        get => _totalFilesCount;
        private set => SetProperty(ref _totalFilesCount, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public void Dispose()
    {
        _dateSelectedSubject.Dispose();
        _disposables.Dispose();
        _dateStatsCache.Dispose();
    }

    public void Clear()
    {
        _synchronizationContext.Post(
            _ =>
            {
                _dateStatsCache.Clear();
                TotalFilesCount = 0;
                IsLoading = false;
            },
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

    private void OnBucketsChanged(IList<FilesBucket> bufferedBuckets)
    {
        _synchronizationContext.Post(
            _ =>
            {
                _dateStatsCache.Edit(innerCache =>
                {
                    foreach (var bucket in bufferedBuckets)
                    {
                        var dateStat = new DateStatViewModel
                        {
                            Date = bucket.Date,
                            Count = bucket.FilesCount
                        };

                        innerCache.AddOrUpdate(dateStat);
                    }
                });

                UpdateDateFlags();
                UpdateNormalizedHeights();
            },
            null);
    }

    private void OnScanProgressChanged(ScanProgress progress)
    {
        _synchronizationContext.Post(
            _ =>
            {
                switch (progress.Status)
                {
                    case ScanStatus.InitialScanStarted:
                        IsLoading = true;
                        break;
                    case ScanStatus.InitialScanCompleted:
                        IsLoading = false;
                        break;
                    case ScanStatus.ErrorOccurred:
                        IsLoading = false;
                        break;
                }
            },
            null);
    }

    private void UpdateDateFlags()
    {
        var allItems = _dateStatsCache.Items.ToList();
        foreach (var item in allItems)
        {
            item.UpdateMonthYearFlag(allItems);
        }
    }

    private void UpdateNormalizedHeights()
    {
        var items = _dateStatsCache.Items.ToList();
        if (items.Count == 0)
        {
            return;
        }

        var minCount = items.Min(s => s.Count);
        var maxCount = items.Max(s => s.Count);

        foreach (var item in items)
        {
            var normalizedHeight = Normalize(item.Count, minCount, maxCount, 0, 40);
            item.NormalizedHeight = Math.Max(normalizedHeight, 4);
        }
    }
}