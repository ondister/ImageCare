using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using ImageCare.Core.Services;
using ImageCare.Mvvm;
using ImageCare.Mvvm.Collections;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal abstract class FileSystemItemViewModel : ViewModelBase, IComparable<FileSystemItemViewModel>
{
    protected readonly IFolderService FolderService;
    private bool _isExpanded;

    protected FileSystemItemViewModel(string name, string path, IEnumerable<FileSystemItemViewModel> children, IFolderService folderService)
    {
        FolderService = folderService;
        Name = name;
        Path = path;
        ChildFileSystemItems = new SortedObservableCollection<FileSystemItemViewModel>(children);
    }

    public string Name { get; }

    public string Path { get; }

    public SortedObservableCollection<FileSystemItemViewModel> ChildFileSystemItems { get; }

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

    /// <inheritdoc />
    public int CompareTo(FileSystemItemViewModel? other)
    {
        if (ReferenceEquals(this, other))
        {
            return 0;
        }

        if (ReferenceEquals(null, other))
        {
            return 1;
        }

        return string.Compare(Path, other.Path, StringComparison.Ordinal);
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

                    ChildFileSystemItems.InsertItem(fileSystemItemViewModel);
                }
            }

            foreach (var fileModel in currentDirectoryModel.FileModels)
            {
                if (FoldersViewModel.FileSystemItemsFactories.TryGetValue(currentDirectoryModel.GetType(), out var factoryFunc))
                {
                    var fileSystemItemViewModel = factoryFunc.Invoke(fileModel.Name, fileModel.FullName, FoldersViewModel.GetFileSystemItemChildren([], [], FolderService), FolderService);
                    ChildFileSystemItems.InsertItem(fileSystemItemViewModel);
                }
            }
        }
        catch (Exception exception) { }
    }
}