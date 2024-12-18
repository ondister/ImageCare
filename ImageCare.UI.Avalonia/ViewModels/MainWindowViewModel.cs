using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

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

    public string Greeting { get; } = "Welcome to Avalonia!";

    public ICommand OnViewLoadedCommand { get; }

    private void OnViewLoaded()
    {
        _regionManager.RequestNavigate(RegionNames.SourceFoldersRegion, "FoldersView", new NavigationParameters{{"mode", "Source"}});
        _regionManager.RequestNavigate(RegionNames.TargetFoldersRegion, "FoldersView", new NavigationParameters {{ "mode", "Target"}});
        _regionManager.RequestNavigate(RegionNames.MainImageViewRegion, "MainImageView");
        _regionManager.RequestNavigate(RegionNames.SourcePreviewImageRegion, "PreviewImageView", new NavigationParameters { { "mode", "Source" } });
        _regionManager.RequestNavigate(RegionNames.TargetPreviewImageRegion, "PreviewImageView", new NavigationParameters { { "mode", "Target" } });
    }
}