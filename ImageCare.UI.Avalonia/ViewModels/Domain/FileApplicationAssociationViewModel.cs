using ImageCare.Core.Domain.Configuration;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

public sealed class FileApplicationAssociationViewModel : ViewModelBase
{
    private string _name = string.Empty;
    private string _fileExtension = string.Empty;
    private string _applicationPath = string.Empty;
    private ApplicationLaunchMode _launchMode;

    public FileApplicationAssociationViewModel(
        string name,
        string fileExtension,
        string applicationPath,
        ApplicationLaunchMode launchMode = ApplicationLaunchMode.Executable)
    {
        Name = name;
        FileExtension = fileExtension;
        ApplicationPath = applicationPath;
        LaunchMode = launchMode;
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string FileExtension
    {
        get => _fileExtension;
        set => SetProperty(ref _fileExtension, value);
    }

    public string ApplicationPath
    {
        get => _applicationPath;
        set => SetProperty(ref _applicationPath, value);
    }

    public ApplicationLaunchMode LaunchMode
    {
        get => _launchMode;
        set => SetProperty(ref _launchMode, value);
    }
}
