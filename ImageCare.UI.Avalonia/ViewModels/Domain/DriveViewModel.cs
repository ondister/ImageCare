using System.Collections.Generic;

using AutoMapper;

using ImageCare.Core.Services;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal class DriveViewModel : DirectoryViewModel
{
    /// <inheritdoc />
    public DriveViewModel(string? name,
                          string path,
                          IEnumerable<DirectoryViewModel> children,
                          IFolderService folderService,
                          IMapper mapper,
                          ILogger logger)
        : base(name, path, children, folderService, mapper, logger) { }
}