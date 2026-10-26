using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.UI.Avalonia.ViewModels;
using ImageCare.UI.Avalonia.Views;

using Prism.Navigation.Regions;

namespace ImageCare.UI.Avalonia.Services;

/// <summary>
/// Provides safe, non-destructive shell actions for centralized help recommendations.
/// </summary>
internal sealed class ShellUiActionService
{
    private readonly IRegionManager _regionManager;
    private readonly IConfigurationService _configurationService;
    private readonly SettingsWindowService _settingsWindowService;
    private readonly MediaSearchWindowService _mediaSearchWindowService;

    public ShellUiActionService(
        IRegionManager regionManager,
        IConfigurationService configurationService,
        SettingsWindowService settingsWindowService,
        MediaSearchWindowService mediaSearchWindowService)
    {
        _regionManager = regionManager;
        _configurationService = configurationService;
        _settingsWindowService = settingsWindowService;
        _mediaSearchWindowService = mediaSearchWindowService;
    }

    public void OpenSettings() => _settingsWindowService.Show();

    public void OpenMediaSearch() => _mediaSearchWindowService.Show();

    public bool FocusFolders(FileManagerPanel panel)
    {
        var view = GetRegionView<FoldersView>(GetFoldersRegionName(panel));
        return view?.FocusPanel() == true;
    }

    public bool FocusPreviews(FileManagerPanel panel)
    {
        var view = GetRegionView<PreviewPanelView>(GetPreviewsRegionName(panel));
        return view?.FocusPanel() == true;
    }

    public async Task<bool> RestoreLastFoldersAsync(CancellationToken cancellationToken = default)
    {
        var configuration = _configurationService.Configuration.Value;
        var restored = false;
        restored |= await RestoreAsync(
            FileManagerPanel.Left,
            configuration.LastSourceDirectoryPath,
            cancellationToken);
        restored |= await RestoreAsync(
            FileManagerPanel.Right,
            configuration.LastTargetDirectoryPath,
            cancellationToken);
        return restored;
    }

    private async Task<bool> RestoreAsync(
        FileManagerPanel panel,
        string? path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        var viewModel = GetRegionView<FoldersView>(GetFoldersRegionName(panel))?.DataContext as FoldersViewModel;
        return viewModel != null && await viewModel.SelectFolderInTreeAsync(path, cancellationToken);
    }

    private TView? GetRegionView<TView>(string regionName)
        where TView : class
    {
        var region = _regionManager.Regions.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, regionName, StringComparison.Ordinal));
        return region?.Views.OfType<TView>().FirstOrDefault();
    }

    private static string GetFoldersRegionName(FileManagerPanel panel) =>
        panel == FileManagerPanel.Right
            ? RegionNames.TargetFoldersRegion
            : RegionNames.SourceFoldersRegion;

    private static string GetPreviewsRegionName(FileManagerPanel panel) =>
        panel == FileManagerPanel.Right
            ? RegionNames.TargetPreviewImageRegion
            : RegionNames.SourcePreviewImageRegion;
}
