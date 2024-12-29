using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using ImageCare.Core.Domain;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.Mvvm;
using ImageCare.UI.Avalonia.Views;

using Prism.Regions;

namespace ImageCare.UI.Avalonia.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IRegionManager _regionManager;
    private readonly IFileOperationsService _fileOperationsService;
    private readonly SynchronizationContext _synchronizationContext;
    private CompositeDisposable _subscriptions;

    public MainWindowViewModel(IRegionManager regionManager,
                               IFileOperationsService fileOperationsService,
                               SynchronizationContext synchronizationContext)
    {
        _regionManager = regionManager;
        _fileOperationsService = fileOperationsService;
        _synchronizationContext = synchronizationContext;

        OnViewLoadedCommand = new RelayCommand(OnViewLoaded);
        OnViewUnloadedCommand = new RelayCommand(OnViewUnloaded);
    }

    public ICommand OnViewLoadedCommand { get; }

    public ICommand OnViewUnloadedCommand { get; }

    private void OnViewLoaded()
    {
        _regionManager.RequestNavigate(RegionNames.SourceFoldersRegion, "FoldersView", OnNavigationResult, new NavigationParameters { { "panel", FileManagerPanel.Left } });
        _regionManager.RequestNavigate(RegionNames.TargetFoldersRegion, "FoldersView", new NavigationParameters { { "panel", FileManagerPanel.Right } });
        _regionManager.RequestNavigate(RegionNames.MainImageViewRegion, "MainImageView");
        _regionManager.RequestNavigate(RegionNames.SourcePreviewImageRegion, "PreviewPanelView", new NavigationParameters { { "panel", FileManagerPanel.Left } });
        _regionManager.RequestNavigate(RegionNames.TargetPreviewImageRegion, "PreviewPanelView", new NavigationParameters { { "panel", FileManagerPanel.Right } });
        _regionManager.RequestNavigate(RegionNames.BottomBarRegion, "BottomBarView");

        _subscriptions = new CompositeDisposable
        {
            _fileOperationsService.ImagePreviewSelected
                                  .ObserveOn(_synchronizationContext)
                                  .Subscribe(OnImagePreviewSelected)
        };
    }

    private void OnImagePreviewSelected(SelectedImagePreview preview)
    {
        if (preview.MediaFormat.MediaType == MediaType.Video &&
            !_regionManager.Regions[RegionNames.MainImageViewRegion].ActiveViews.Any(v=>v is MainVideoView))
        {
            _regionManager.RequestNavigate(RegionNames.MainImageViewRegion, "MainVideoView", new NavigationParameters { { "imagePreview", preview }});
        }

        if (preview.MediaFormat.MediaType == MediaType.Image &&
            !_regionManager.Regions[RegionNames.MainImageViewRegion].ActiveViews.Any(v => v is MainImageView))
        {
            _regionManager.RequestNavigate(RegionNames.MainImageViewRegion, "MainImageView", new NavigationParameters { { "imagePreview", preview } });

        }
    }

    private void OnViewUnloaded()
    {
        _subscriptions?.Dispose();
    }

    private void OnNavigationResult(NavigationResult result)
    {
        if (result.Error?.InnerException != null)
        {
            throw result.Error.InnerException;
        }
    }
}