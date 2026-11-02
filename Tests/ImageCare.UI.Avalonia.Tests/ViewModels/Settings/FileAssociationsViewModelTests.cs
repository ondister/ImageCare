using System.Reactive.Linq;

using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;
using ImageCare.UI.Avalonia.ViewModels.Settings;

using Mapster;
using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.Tests.ViewModels.Settings;

[TestFixture]
[NonParallelizable]
public sealed class FileAssociationsViewModelTests
{
    private FakeConfigurationService _configurationService = null!;
    private FakeFileDialogService _fileDialogService = null!;
    private Serilog.Core.Logger _logger = null!;
    private FileAssociationsViewModel _viewModel = null!;
    private string _originalCulture = null!;

    [SetUp]
    public void SetUp()
    {
        _originalCulture = LocalizationManager.Instance.Culture.Name;
        LocalizationManager.Instance.SetCulture("en");
        _configurationService = new FakeConfigurationService();
        _fileDialogService = new FakeFileDialogService();
        _logger = new LoggerConfiguration().CreateLogger();
        _viewModel = new FileAssociationsViewModel(
            _configurationService,
            _fileDialogService,
            CreateMapper(),
            _logger,
            LocalizationManager.Instance,
            _configurationService.SaveConfiguration);
    }

    [TearDown]
    public void TearDown()
    {
        LocalizationManager.Instance.SetCulture(_originalCulture);
        _logger.Dispose();
    }

    [Test]
    public async Task AddAssociation_UsesExtensionModeAndPathAsDuplicateCriteria()
    {
        var custom = _viewModel.ApplicationChoices.Single(choice => choice.IsCustom);
        _viewModel.NewExtension = ".jpg";
        _viewModel.SelectedApplicationChoice = custom;
        _fileDialogService.SelectedApplication = TestPath("Tools", "viewer.app");
        await _viewModel.ChooseApplicationCommand.ExecuteAsync();
        _configurationService.Configuration.Value.ApplicationAssociationPairs.Add(
            new FileApplicationAssociation
            {
                FileExtension = ".JPG",
                Name = "A different display name",
                ApplicationPath = _fileDialogService.SelectedApplication,
                LaunchMode = custom.LaunchMode
            });

        _viewModel.AddAssociationCommand.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(_configurationService.Configuration.Value.ApplicationAssociationPairs, Has.Count.EqualTo(1));
            Assert.That(_configurationService.SaveCallCount, Is.Zero);
        });
    }

    [Test]
    public async Task CustomPicker_EnablesAddAndMapsFileNameIntoAssociation()
    {
        var applicationPath = TestPath("Tools", "Photo Editor.app");
        _fileDialogService.SelectedApplication = applicationPath;
        _viewModel.NewExtension = ".dng";
        _viewModel.SelectedApplicationChoice = _viewModel.ApplicationChoices.Single(choice => choice.IsCustom);

        Assert.That(_viewModel.AddAssociationCommand.CanExecute(), Is.False);
        await _viewModel.ChooseApplicationCommand.ExecuteAsync();
        _viewModel.AddAssociationCommand.Execute();

        var association = _configurationService.Configuration.Value.ApplicationAssociationPairs.Single();
        Assert.Multiple(() =>
        {
            Assert.That(association.FileExtension, Is.EqualTo(".dng"));
            Assert.That(association.Name, Is.EqualTo("Photo Editor"));
            Assert.That(association.ApplicationPath, Is.EqualTo(applicationPath));
            Assert.That(_viewModel.NewApplicationPath, Is.Empty);
            Assert.That(_viewModel.Associations, Has.Count.EqualTo(1));
            Assert.That(_configurationService.SaveCallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void RemoveAssociation_RemovesOnlySelectedExactMapping()
    {
        var first = CreateAssociation(".jpg", "Editor", "/apps/editor", ApplicationLaunchMode.Executable);
        var second = CreateAssociation(".jpg", "Editor Beta", "/apps/editor", ApplicationLaunchMode.Executable);
        _configurationService.Configuration.Value.ApplicationAssociationPairs.AddRange([first, second]);
        _viewModel.Open();
        _viewModel.SelectedAssociation = _viewModel.Associations.Single(item => item.Name == first.Name);

        _viewModel.RemoveAssociationCommand.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(_configurationService.Configuration.Value.ApplicationAssociationPairs, Is.EqualTo(new[] { second }));
            Assert.That(_viewModel.SelectedAssociation, Is.Null);
            Assert.That(_viewModel.Associations.Select(item => item.Name), Is.EqualTo(new[] { second.Name }));
            Assert.That(_configurationService.SaveCallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void RestoreRecommendedApplications_AddsMissingDefaultsOnlyOnce()
    {
        _viewModel.RestoreRecommendedApplicationsCommand.Execute();
        var countAfterRestore = _configurationService.Configuration.Value.ApplicationAssociationPairs.Count;
        _viewModel.RestoreRecommendedApplicationsCommand.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(countAfterRestore, Is.GreaterThan(0));
            Assert.That(_configurationService.Configuration.Value.ApplicationAssociationPairs, Has.Count.EqualTo(countAfterRestore));
            Assert.That(_configurationService.SaveCallCount, Is.EqualTo(1));
            Assert.That(_viewModel.Associations, Has.Count.EqualTo(countAfterRestore));
        });
    }

    [Test]
    public void RefreshCulture_RebuildsChoicesAndPreservesSelectedMode()
    {
        var originalChoice = _viewModel.ApplicationChoices.Single(choice => choice.IsCustom);
        _viewModel.SelectedApplicationChoice = originalChoice;

        LocalizationManager.Instance.SetCulture("ru");
        _viewModel.RefreshCulture();

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.SelectedApplicationChoice, Is.Not.SameAs(originalChoice));
            Assert.That(_viewModel.SelectedApplicationChoice?.IsCustom, Is.True);
            Assert.That(_viewModel.SelectedApplicationChoice?.LaunchMode, Is.EqualTo(originalChoice.LaunchMode));
            Assert.That(_viewModel.SelectedApplicationChoice?.DisplayName, Is.EqualTo(LocalizationManager.Instance["Settings_CustomApplication"]));
        });
    }

    private static FileApplicationAssociation CreateAssociation(
        string extension,
        string name,
        string path,
        ApplicationLaunchMode launchMode) =>
        new()
        {
            FileExtension = extension,
            Name = name,
            ApplicationPath = path,
            LaunchMode = launchMode
        };

    private static IMapper CreateMapper()
    {
        var configuration = new TypeAdapterConfig();
        configuration.NewConfig<FileApplicationAssociation, FileApplicationAssociationViewModel>();
        return new Mapper(configuration);
    }

    private static string TestPath(params string[] parts) =>
        Path.Combine([Path.GetTempPath(), "ImageCareFileAssociationsTests", .. parts]);

    private sealed class FakeConfigurationService : IConfigurationService
    {
        public IObservable<Configuration> ConfigurationSaved => Observable.Never<Configuration>();
        public Lazy<Configuration> Configuration { get; } = new(() => new Configuration());
        public int SaveCallCount { get; private set; }
        public void SaveConfiguration() => SaveCallCount++;
    }

    private sealed class FakeFileDialogService : IFileDialogService
    {
        public string? SelectedApplication { get; set; }
        public Task<string?> ShowOpenApplicationDialogAsync(string title) => Task.FromResult(SelectedApplication);
        public Task<string?> ShowOpenFileDialogAsync(string title, params (string Name, string[] Patterns)[] filters) =>
            throw new NotSupportedException();
        public Task<string?> ShowOpenFolderDialogAsync(string title) => throw new NotSupportedException();
    }
}
