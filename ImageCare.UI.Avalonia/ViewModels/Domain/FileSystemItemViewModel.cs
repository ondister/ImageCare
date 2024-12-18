using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

using ImageCare.Core.Services;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal abstract class FileSystemItemViewModel : ViewModelBase
{
    protected readonly IFolderService FolderService;
    private bool _isExpanded;

    protected FileSystemItemViewModel(string name, string path, IEnumerable<FileSystemItemViewModel> children, IFolderService folderService)
    {
        FolderService = folderService;
        Name = name;
        Path = path;
        ChildFileSystemItems = new ObservableCollection<FileSystemItemViewModel>(children);
    }

    [field: ObservableProperty]
    public string Name { get; set; }

    [field: ObservableProperty]
    public string Path { get; }

    public ObservableCollection<FileSystemItemViewModel> ChildFileSystemItems { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value) && _isExpanded)
            {
                if (this is DeviceViewModel)
                {
                    return;
                }

                ChildFileSystemItems.Clear();
                SeedFileSystemItemsAsync();
            }
        }
    }

    private async Task SeedFileSystemItemsAsync()
    {
        try
        {
            var currentDirectoryModel = await FolderService.GetDirectoryModelAsync(Path);

            foreach (var directoryModel in currentDirectoryModel.DirectoryModels)
            {
                if (FoldersViewModel.FileSystemItemsFactories.TryGetValue(currentDirectoryModel.GetType(), out var factoryFunc))
                {
                    var fileSystemItemViewModel = factoryFunc.Invoke(directoryModel.Name, directoryModel.Path, FoldersViewModel.GetFileSystemItemChildren(directoryModel.DirectoryModels, directoryModel.FileModels, FolderService), FolderService);

                    ChildFileSystemItems.Add(fileSystemItemViewModel);
                }
            }

            foreach (var fileModel in currentDirectoryModel.FileModels)
            {
                if (FoldersViewModel.FileSystemItemsFactories.TryGetValue(currentDirectoryModel.GetType(), out var factoryFunc))
                {
                    var fileSystemItemViewModel = factoryFunc.Invoke(fileModel.Name, fileModel.FullName, FoldersViewModel.GetFileSystemItemChildren([], [], FolderService), FolderService);
                    ChildFileSystemItems.Add(fileSystemItemViewModel);
                }
            }
        }
        catch (Exception exception)
        {

        }
      
    }
}