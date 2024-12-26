using System.Collections.Generic;

using AutoMapper;
using ImageCare.Core.Services.FolderService;
using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal sealed class NetworkDriveViewModel : DriveViewModel
{
    /// <inheritdoc />
    public NetworkDriveViewModel(string? name,
                                 string path,
                                 IEnumerable<DirectoryViewModel> children,
                                 IFolderService folderService,
                                 IMapper mapper,
                                 ILogger logger)
        : base(name, path, children, folderService, mapper, logger) { }
}