using System.Collections.Generic;

using AutoMapper;

using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FolderService;
using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal sealed class SpecialDirectoryViewModel : DriveViewModel
{
    /// <inheritdoc />
    public SpecialDirectoryViewModel(string? name,
                                     string path,
                                     IEnumerable<DirectoryViewModel> children,
                                     IFolderService folderService,
                                     IFileSystemService fileSystemService,
                                     IMapper mapper,
                                     ILogger logger)
        : base(name, path, children, folderService, fileSystemService, mapper, logger) { }
}