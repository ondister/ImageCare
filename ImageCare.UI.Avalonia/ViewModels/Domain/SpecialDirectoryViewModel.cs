using System.Collections.Generic;
using System.Collections.Specialized;

using ImageCare.Core.Services.FolderService;
using ImageCare.UI.Avalonia.ViewModels.Folders;

using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal sealed class SpecialDirectoryViewModel : DriveViewModel
{
    /// <inheritdoc />
    public SpecialDirectoryViewModel(string? name,
                                     string path,
                                     IEnumerable<DirectoryViewModel> children,
                                     IFolderService folderService,
                                     FolderMutationCoordinator mutationCoordinator,
                                     IMapper mapper,
                                     ILogger logger)
        : base(name, path, children, folderService, mutationCoordinator, mapper, logger)
    {
        ChildFileSystemItems.CollectionChanged += OnChildrenChanged;
    }

    public int ItemCount => ChildFileSystemItems.Count;

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(ItemCount));
    }
}