using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Services.MediaPreviewOperationsService;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal sealed class DeletedMediaFileViewModel : ViewModelBase
{
    private readonly Func<DeletedMediaFile, Task> _restore;
    private bool _isRestoring;

    public DeletedMediaFileViewModel(DeletedMediaFile model, Func<DeletedMediaFile, Task> restore)
    {
        Model = model;
        _restore = restore;
        RestoreCommand = CreateAsyncCommand(RestoreAsync, () => !IsRestoring)
            .ObservesProperty(() => IsRestoring);
    }

    public DeletedMediaFile Model { get; }

    public string FileName => Path.GetFileName(Model.OriginalPath);

    public string OriginalFolder => Path.GetDirectoryName(Model.OriginalPath) ?? string.Empty;

    public string DeletedAt => Model.DeletedAtUtc.ToLocalTime().ToString("g");

    public ICommand RestoreCommand { get; }

    public bool IsRestoring
    {
        get => _isRestoring;
        private set => SetProperty(ref _isRestoring, value);
    }

    private async Task RestoreAsync()
    {
        try
        {
            IsRestoring = true;
            await _restore(Model);
        }
        finally
        {
            IsRestoring = false;
        }
    }
}
