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

using Prism.Commands.Ex;
using Prism.Services.Dialogs;

using DelegateCommand = Prism.Commands.DelegateCommand;

namespace ImageCare.UI.Avalonia.ViewModels;

public sealed class SettingsViewModel : ViewModelBase, IDialogAware
{
	private readonly IConfigurationService _configService;
	private readonly IFileDialogService _fileDialogService;
	private readonly IMapper _mapper;

	private string _newExtension = string.Empty;
	private string _newName = string.Empty;
	private FileApplicationAssociationViewModel? _selectedAssociation;

	public SettingsViewModel(IConfigurationService configurationService,
	                         IFileDialogService fileDialogService,
	                         IMapper mapper)
	{
		_configService = configurationService;
		_fileDialogService = fileDialogService;
		_mapper = mapper;

		AddAssociationCommand = new DelegateCommand(AddAssociation);
		RemoveAssociationCommand = new DelegateCommand(RemoveAssociation, CanRemoveAssociation).ObservesProperty(() => SelectedAssociation);
		EditApplicationPathCommand = new AsyncDelegateCommand(EditApplicationPathAsync, CanEditApplicationPath).ObservesProperty(() => SelectedAssociation);
	}

	/// <inheritdoc />
	public string Title { get; } = "Settings";

	public ObservableCollection<FileApplicationAssociationViewModel> Associations { get; } = new();

	public string NewExtension
	{
		get => _newExtension;
		set => SetProperty(ref _newExtension, value);
	}

	public string NewName
	{
		get => _newName;
		set => SetProperty(ref _newName, value);
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
	public bool CanCloseDialog()
	{
		return true;
	}

	/// <inheritdoc />
	public void OnDialogClosed() { }

	/// <inheritdoc />
	public void OnDialogOpened(IDialogParameters parameters)
	{
		LoadAssociations();
	}

	/// <inheritdoc />
	public event Action<IDialogResult>? RequestClose;

	private void AddAssociation()
	{
		if (string.IsNullOrWhiteSpace(NewExtension) || string.IsNullOrWhiteSpace(NewName))
		{
			return;
		}

		var association = new FileApplicationAssociation
		{
			FileExtension = NewExtension.Trim(),
			Name = NewName.Trim(),
			ApplicationPath = string.Empty
		};

		_configService.Configuration.Value.ApplicationAssociationPairs.Add(association);
		SaveConfiguration();

		NewExtension = string.Empty;
		NewName = string.Empty;

		// Reload all
		LoadAssociations();
	}

	private void RemoveAssociation()
	{
		if (SelectedAssociation != null)
		{
			var associationToRemove = _configService.Configuration.Value.ApplicationAssociationPairs.FirstOrDefault(a => a.FileExtension == SelectedAssociation.FileExtension && a.Name == SelectedAssociation.Name);
			if (associationToRemove == null)
			{
				return;
			}

			_configService.Configuration.Value.ApplicationAssociationPairs.Remove(associationToRemove);
			SaveConfiguration();

			// Reload all
			LoadAssociations();
		}

		SelectedAssociation = null;
	}

	private bool CanRemoveAssociation()
	{
		return SelectedAssociation != null;
	}

	private async Task EditApplicationPathAsync(IProgress<int> progress, CancellationToken cancellationToken)
	{
		if (SelectedAssociation == null)
		{
			return;
		}

		var filePath = await _fileDialogService.ShowOpenFileDialogAsync(
			               "Select application",
			               ("Executable", new[] { "*.exe", "*.bat" }),
			               ("All files", new[] { "*.*" }));

		if (!string.IsNullOrEmpty(filePath))
		{
			SelectedAssociation.ApplicationPath = filePath;
			var associationToEdit = _configService.Configuration.Value.ApplicationAssociationPairs.FirstOrDefault(a => a.FileExtension == SelectedAssociation.FileExtension && a.Name == SelectedAssociation.Name);
			if (associationToEdit == null)
			{
				return;
			}

			associationToEdit.ApplicationPath = filePath;

			SaveConfiguration();
		}
	}

	private bool CanEditApplicationPath()
	{
		return SelectedAssociation != null;
	}

	private void LoadAssociations()
	{
		Associations.Clear();
		var orderedList = _configService.Configuration.Value.ApplicationAssociationPairs.OrderBy(p => p.FileExtension);

		foreach (var association in orderedList)
		{
			Associations.Add(_mapper.Map<FileApplicationAssociationViewModel>(association));
		}
	}

	private void SaveConfiguration()
	{
		_configService.SaveConfiguration();
	}
}