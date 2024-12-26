using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using ImageCare.Core.Domain;
using ImageCare.Mvvm;

using Prism.Regions;

namespace ImageCare.UI.Avalonia.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IRegionManager _regionManager;

    public MainWindowViewModel(IRegionManager regionManager)
    {
        _regionManager = regionManager;

        OnViewLoadedCommand = new RelayCommand(OnViewLoaded);
    }

    public ICommand OnViewLoadedCommand { get; }

    private void OnViewLoaded()
    {
        _regionManager.RequestNavigate(RegionNames.SourceFoldersRegion, "FoldersView", OnNavigationResult, new NavigationParameters { { "panel", FileManagerPanel.Left } });
        _regionManager.RequestNavigate(RegionNames.TargetFoldersRegion, "FoldersView", new NavigationParameters { { "panel", FileManagerPanel.Right } });
        _regionManager.RequestNavigate(RegionNames.MainImageViewRegion, "MainImageView");
        _regionManager.RequestNavigate(RegionNames.SourcePreviewImageRegion, "PreviewPanelView", new NavigationParameters { { "panel", FileManagerPanel.Left } });
        _regionManager.RequestNavigate(RegionNames.TargetPreviewImageRegion, "PreviewPanelView", new NavigationParameters { { "panel", FileManagerPanel.Right} });
        _regionManager.RequestNavigate(RegionNames.BottomBarRegion, "BottomBarView");
    }

    private void OnNavigationResult(NavigationResult result)
    {
        if (result.Error?.InnerException != null)
        {
            throw result.Error.InnerException;
        }
    }
}