using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using AutoMapper;

using ImageCare.Core.Services;
using ImageCare.Mvvm;
using ImageCare.Mvvm.Collections;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal class DirectoryViewModel : ViewModelBase, IComparable<DirectoryViewModel>
{
    protected readonly IFolderService FolderService;
    private readonly ILogger _logger;
    private readonly IMapper _mapper;
    private bool _isExpanded;
    private bool _isloaded;

    public DirectoryViewModel(string? name,
                              string path,
                              IEnumerable<DirectoryViewModel> children,
                              IFolderService folderService,
                              IMapper mapper,
                              ILogger logger)
    {
        FolderService = folderService;
        _logger = logger;
        _mapper = mapper;
        Name = name;
        Path = path;

        ChildFileSystemItems = new SortedObservableCollection<DirectoryViewModel>(children);
    }

    public string? Name { get; }

    public string Path { get; }

    public SortedObservableCollection<DirectoryViewModel> ChildFileSystemItems { get; }

    public bool IsLoaded
    {
        get => _isloaded;
        set => SetProperty(ref _isloaded, value);
    }

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
                _ = SeedFileSystemItemsAsync();
            }
        }
    }

    /// <inheritdoc />
    public int CompareTo(DirectoryViewModel? other)
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
        IsLoaded = true;
        try
        {
            var currentDirectoryModel = await FolderService.GetDirectoryModelAsync(Path);

            foreach (var directoryModel in currentDirectoryModel.DirectoryModels)
            {
                var fileSystemItemViewModel = _mapper.Map<DirectoryViewModel>(directoryModel);

                ChildFileSystemItems.InsertItem(fileSystemItemViewModel);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Unexpected exception during getting files from folder: {Path}");
        }
        finally
        {
            IsLoaded = false;
        }
    }
}