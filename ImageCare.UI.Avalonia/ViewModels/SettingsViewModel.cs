using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using AutoMapper;

using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Mvvm;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Prism.Dialogs;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

public sealed class SettingsViewModel : ViewModelBase, IDialogAware
{
    private readonly IConfigurationService _configService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly DialogCloseListener _requestClose;

    private string _newExtension = string.Empty;
    private string _newName = string.Empty;
    private FileApplicationAssociationViewModel? _selectedAssociation;

    public SettingsViewModel(IConfigurationService configurationService,
                             IFileDialogService fileDialogService,
                             IMapper mapper,
                             ILogger logger,
                             DialogCloseListener requestClose)
    {
        _configService = configurationService;
        _fileDialogService = fileDialogService;
        _mapper = mapper;
        _logger = logger;
        _requestClose = requestClose;

        AddAssociationCommand = CreateCommand(AddAssociation, CanAddAssociation, OnCommandException)
                                .ObservesProperty(() => NewExtension)
                                .ObservesProperty(() => NewName);
        RemoveAssociationCommand = CreateCommand(RemoveAssociation, CanRemoveAssociation, OnCommandException)
            .ObservesProperty(() => SelectedAssociation);
        EditApplicationPathCommand = CreateAsyncCommand(EditApplicationPathAsync, CanEditApplicationPath, OnCommandException)
            .ObservesProperty(() => SelectedAssociation);
    }

    /// <inheritdoc />
    public string Title { get; } = "Settings";

    public ObservableCollection<FileApplicationAssociationViewModel> Associations { get; } = new();

    public string NewExtension
    {
        get => _newExtension;
        set => SetProperty(ref _newExtension, value?.Trim() ?? string.Empty);
    }

    public string NewName
    {
        get => _newName;
        set => SetProperty(ref _newName, value?.Trim() ?? string.Empty);
    }

    public FileApplicationAssociationViewModel? SelectedAssociation
    {
        get => _selectedAssociation;
        set => SetProperty(ref _selectedAssociation, value);
    }

    public ICommand AddAssociationCommand { get; }

    public ICommand RemoveAssociationCommand { get; }

    public ICommand EditApplicationPathCommand { get; }

    /// <inheritdoc />
    DialogCloseListener IDialogAware.RequestClose => _requestClose;

    /// <inheritdoc />
    public bool CanCloseDialog()
    {
        return true;
    }

    /// <inheritdoc />
    public void OnDialogClosed()
    {
        try
        {
            Associations.Clear();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during SettingsViewModel disposal");
        }
    }

    /// <inheritdoc />
    public void OnDialogOpened(IDialogParameters parameters)
    {
        try
        {
            LoadAssociations();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during dialog open");
        }
    }

    /// <inheritdoc />
    public event Action<IDialogResult>? RequestClose;

    private void AddAssociation()
    {
        if (string.IsNullOrWhiteSpace(NewExtension) || string.IsNullOrWhiteSpace(NewName))
        {
            return;
        }

        var trimmedExtension = NewExtension.Trim();
        var trimmedName = NewName.Trim();

        if (_configService.Configuration.Value.ApplicationAssociationPairs
                          .Any(a => a.FileExtension.Equals(trimmedExtension, StringComparison.OrdinalIgnoreCase)
                                 && a.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var association = new FileApplicationAssociation
        {
            FileExtension = trimmedExtension,
            Name = trimmedName,
            ApplicationPath = string.Empty
        };

        _configService.Configuration.Value.ApplicationAssociationPairs.Add(association);
        SaveConfiguration();

        NewExtension = string.Empty;
        NewName = string.Empty;

        LoadAssociations();
    }

    private bool CanAddAssociation()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(NewExtension) && !string.IsNullOrWhiteSpace(NewName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in CanAddAssociation");

            return false;
        }
    }

    private void RemoveAssociation()
    {
        try
        {
            if (SelectedAssociation == null)
            {
                return;
            }

            var associationToRemove = _configService.Configuration.Value.ApplicationAssociationPairs
                                                    .FirstOrDefault(a => a.FileExtension == SelectedAssociation.FileExtension
                                                                      && a.Name == SelectedAssociation.Name);

            if (associationToRemove == null)
            {
                return;
            }

            _configService.Configuration.Value.ApplicationAssociationPairs.Remove(associationToRemove);
            SaveConfiguration();

            LoadAssociations();
            SelectedAssociation = null;
        }
        catch (Exception ex)
        {
            _logger.Error(
                ex,
                "Failed to remove association: {Extension} - {Name}",
                SelectedAssociation?.FileExtension,
                SelectedAssociation?.Name);
        }
    }

    private bool CanRemoveAssociation()
    {
        try
        {
            return SelectedAssociation != null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in CanRemoveAssociation");

            return false;
        }
    }

    private async Task EditApplicationPathAsync(IProgress<int> progress, CancellationToken cancellationToken = default)
    {
        try
        {
            if (SelectedAssociation == null)
            {
                _logger.Warning("Attempted to edit application path for null association");
                return;
            }

            var filePath = await _fileDialogService.ShowOpenFileDialogAsync(
                               "Select application",
                               ("Executable", new[] { "*.exe", "*.bat" }),
                               ("All files", new[] { "*.*" }));

            if (string.IsNullOrEmpty(filePath))
            {
                _logger.Debug(
                    "User cancelled file selection for association: {Extension} - {Name}",
                    SelectedAssociation.FileExtension,
                    SelectedAssociation.Name);
                return;
            }

            var associationToEdit = _configService.Configuration.Value.ApplicationAssociationPairs
                                                  .FirstOrDefault(a => a.FileExtension == SelectedAssociation.FileExtension
                                                                    && a.Name == SelectedAssociation.Name);

            if (associationToEdit == null)
            {
                return;
            }

            SelectedAssociation.ApplicationPath = filePath;
            associationToEdit.ApplicationPath = filePath;

            SaveConfiguration();
        }
        catch (OperationCanceledException)
        {
            // Ignored
        }
        catch (Exception ex)
        {
            _logger.Error(
                ex,
                "Failed to edit application path for: {Extension} - {Name}",
                SelectedAssociation?.FileExtension,
                SelectedAssociation?.Name);
        }
    }

    private bool CanEditApplicationPath()
    {
        try
        {
            return SelectedAssociation != null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in CanEditApplicationPath");

            return false;
        }
    }

    private void LoadAssociations()
    {
        try
        {
            Associations.Clear();
            var orderedList = _configService.Configuration.Value.ApplicationAssociationPairs
                                            .OrderBy(p => p.FileExtension)
                                            .ThenBy(p => p.Name);

            foreach (var association in orderedList)
            {
                try
                {
                    Associations.Add(_mapper.Map<FileApplicationAssociationViewModel>(association));
                }
                catch (Exception ex)
                {
                    _logger.Error(
                        ex,
                        "Failed to map association: {Extension} - {Name}",
                        association.FileExtension,
                        association.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load associations");
        }
    }

    private void SaveConfiguration()
    {
        try
        {
            _configService.SaveConfiguration();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save configuration");
        }
    }

    private void OnCommandException(Exception exception)
    {
        _logger.Error(exception, "Command execution failed");
    }

    private void OnRequestClose(IDialogResult obj)
    {
        RequestClose?.Invoke(obj);
    }
}