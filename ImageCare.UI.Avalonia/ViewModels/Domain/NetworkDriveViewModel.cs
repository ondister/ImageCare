using System.Collections.Generic;

using ImageCare.Core.Services.FolderService;
using ImageCare.UI.Avalonia.ViewModels.Folders;

using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal sealed class NetworkDriveViewModel : DriveViewModel
{
    /// <inheritdoc />
    public NetworkDriveViewModel(string? name,
                                 string path,
                                 IEnumerable<DirectoryViewModel> children,
                                 IFolderService folderService,
                                 FolderMutationCoordinator mutationCoordinator,
                                 IMapper mapper,
                                 ILogger logger)
        : base(name, path, children, folderService, mutationCoordinator, mapper, logger) { }
}