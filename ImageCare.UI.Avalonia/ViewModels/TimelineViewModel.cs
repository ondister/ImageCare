using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Windows.Input;

using DynamicData;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FolderStatisticsService;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class TimelineViewModel : ViewModelBase, IDisposable
{
    private readonly SynchronizationContext _synchronizationContext;
    private readonly Subject<DateTime> _dateSelectedSubject;
    private readonly Subject<Unit> _statisticsClickSubject;
    private readonly CompositeDisposable _disposables = new();
    private readonly CompositeDisposable _dateStatDisposables = new();
    private readonly IFolderStatisticsService _folderStatisticsService;
    private readonly IConfigurationService _configurationService;
    private readonly TrayAnalysisProgressService _trayAnalysisProgressService;
    private readonly Guid _scanId = Guid.NewGuid();
    private readonly Dictionary<DateTime, string> _representativeFilePaths = [];
    private readonly SourceCache<DateStatViewModel, DateTime> _dateStatsCache;
    private int _totalFilesCount;
    private int _analysisProcessedFiles;
    private int _analysisCandidateFiles;
    private bool _isLoading;
    private bool _isInitialScanComplete;
    private bool _isExpanded;

    public TimelineViewModel(
        IFolderStatisticsService folderStatisticsService,
        IConfigurationService configurationService,
        TrayAnalysisProgressService trayAnalysisProgressService,
        SynchronizationContext synchronizationContext)
    {
        _folderStatisticsService = folderStatisticsService ?? throw new ArgumentNullException(nameof(folderStatisticsService));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _trayAnalysisProgressService = trayAnalysisProgressService ?? throw new ArgumentNullException(nameof(trayAnalysisProgressService));
        _synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));
        _isExpanded = _configurationService.Configuration.Value.TimelineExpanded;

        _dateStatsCache = new SourceCache<DateStatViewModel, DateTime>(x => x.Date);

        _disposables.Add(
            _dateStatsCache.Connect()
                           .SortAndBind(out ReadOnlyObservableCollection<DateStatViewModel> dateStatViewModels,
                                        new DateStatViewModelDescendingComparer())
                           .Subscribe());

        DateStatViewModels = dateStatViewModels;
        _dateSelectedSubject = new Subject<DateTime>();
        _statisticsClickSubject = new Subject<Unit>();

        _disposables.Add(
            _folderStatisticsService.BucketChanged
                                    .Buffer(TimeSpan.FromMilliseconds(250))
                                    .Where(bufferedBuckets => bufferedBuckets.Count > 0)
                                    .Subscribe(OnBucketsChanged));

        _disposables.Add(
            _folderStatisticsService.ScanProgress
                                    .Subscribe(OnScanProgressChanged));
        _disposables.Add(
            _folderStatisticsService.TotalFilesCount
                                    .Sample(TimeSpan.FromMilliseconds(250))
                                    .ObserveOn(_synchronizationContext)
                                    .Subscribe(totalFiles => TotalFilesCount = totalFiles));
    }

    public ICommand StatisticsClickCommand => CreateCommand(() => _statisticsClickSubject.OnNext(Unit.Default));

    public ICommand ToggleExpandedCommand => CreateCommand(ToggleExpanded);

    public bool IsExpanded
    {
        get => _isExpanded;
        private set => SetProperty(ref _isExpanded, value);
    }

    public IObservable<DateTime> DateSelected => _dateSelectedSubject.AsObservable();

    public string? GetRepresentativeFilePath(DateTime date) =>
        _representativeFilePaths.GetValueOrDefault(date.Date);

    public IObservable<Unit> StatisticsClick => _statisticsClickSubject.AsObservable();

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

    public bool IsInitialScanComplete
    {
        get => _isInitialScanComplete;
        private set => SetProperty(ref _isInitialScanComplete, value);
    }

    public string AnalysisProgressText => _analysisCandidateFiles > 0
        ? $"{_analysisProcessedFiles}/{_analysisCandidateFiles}"
        : string.Empty;

    public void Dispose()
    {
        _dateSelectedSubject.Dispose();
        _statisticsClickSubject.Dispose();
        _disposables.Dispose();
        _dateStatDisposables.Dispose();
        DisposeDateStats();

        _dateStatsCache.Dispose();
    }

    private void DisposeDateStats()
    {
        foreach (var dateStat in _dateStatsCache.Items)
        {
            dateStat.Dispose();
        }
    }

    public void Clear()
    {
        _synchronizationContext.Post(
            _ =>
            {
                _dateStatDisposables.Dispose();
                DisposeDateStats();
                _dateStatsCache.Clear();
                _representativeFilePaths.Clear();
                TotalFilesCount = 0;
                IsLoading = false;
                IsInitialScanComplete = false;
                _analysisProcessedFiles = 0;
                _analysisCandidateFiles = 0;
                RaisePropertyChanged(nameof(AnalysisProgressText));
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
        var latestBuckets = bufferedBuckets
            .GroupBy(bucket => bucket.Date)
            .Select(group => group.Last())
            .ToArray();

        _synchronizationContext.Post(
            _ =>
            {
                _dateStatsCache.Edit(innerCache =>
                {
                    foreach (var bucket in latestBuckets)
                    {
                        var existing = innerCache.Lookup(bucket.Date);
                        if (bucket.FilesCount == 0)
                        {
                            _representativeFilePaths.Remove(bucket.Date.Date);
                            if (existing.HasValue)
                            {
                                existing.Value.Dispose();
                                innerCache.RemoveKey(bucket.Date);
                            }

                            continue;
                        }

                        var representativeFilePath = bucket.Files.Keys.FirstOrDefault();
                        if (representativeFilePath != null)
                        {
                            _representativeFilePaths[bucket.Date.Date] = representativeFilePath;
                        }

                        if (existing.HasValue)
                        {
                            existing.Value.Count = bucket.FilesCount;
                            continue;
                        }

                        var dateStat = new DateStatViewModel
                        {
                            Date = bucket.Date,
                            Count = bucket.FilesCount
                        };
                        _disposables.Add(dateStat.DateSelected.Subscribe(OnDateStatDateSelected));
                        innerCache.AddOrUpdate(dateStat);
                    }
                });

                UpdateDateFlags();
                UpdateNormalizedHeights();
            },
            null);
    }

    private void OnDateStatDateSelected(DateTime date)
    {
        _dateSelectedSubject.OnNext(date);
    }

    private void OnScanProgressChanged(ScanProgress progress)
    {
        _trayAnalysisProgressService.Update(_scanId, progress);
        _synchronizationContext.Post(
            _ =>
            {
                _analysisProcessedFiles = progress.ProcessedFiles;
                _analysisCandidateFiles = progress.CandidateFiles;
                RaisePropertyChanged(nameof(AnalysisProgressText));

                switch (progress.Status)
                {
                    case ScanStatus.InitialScanStarted:
                        IsLoading = true;
                        IsInitialScanComplete = false;
                        break;
                    case ScanStatus.InitialScanCompleted:
                        IsLoading = false;
                        IsInitialScanComplete = true;
                        break;
                    case ScanStatus.ErrorOccurred:
                    case ScanStatus.Idle:
                        IsLoading = false;
                        IsInitialScanComplete = false;
                        break;
                }
            },
            null);
    }

    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
        _configurationService.Configuration.Value.TimelineExpanded = IsExpanded;
        _configurationService.SaveConfiguration();
    }

    private void UpdateDateFlags()
    {
        var allItems = _dateStatsCache.Items.ToList();
        var lastDatesByMonth = allItems
            .GroupBy(item => (item.Date.Year, item.Date.Month))
            .ToDictionary(group => group.Key, group => group.Max(item => item.Date));

        foreach (var item in allItems)
        {
            item.IsLastInMonthYear = lastDatesByMonth[(item.Date.Year, item.Date.Month)] == item.Date;
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