using System.Reactive.Linq;

using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Settings;

using Serilog;

namespace ImageCare.UI.Avalonia.Tests.ViewModels.Settings;

[TestFixture]
[NonParallelizable]
public sealed class SettingsGeneralViewModelTests
{
    private FakeConfigurationService _configurationService = null!;
    private Serilog.Core.Logger _logger = null!;
    private SettingsGeneralViewModel _viewModel = null!;
    private string _originalCulture = null!;
    private int _cultureChangedCount;

    [SetUp]
    public void SetUp()
    {
        _originalCulture = LocalizationManager.Instance.Culture.Name;
        LocalizationManager.Instance.SetCulture("en");
        _configurationService = new FakeConfigurationService();
        _cultureChangedCount = 0;
        _logger = new LoggerConfiguration().CreateLogger();
        _viewModel = new SettingsGeneralViewModel(
            _configurationService,
            LocalizationManager.Instance,
            _configurationService.SaveConfiguration,
            () => _cultureChangedCount++);
    }

    [TearDown]
    public void TearDown()
    {
        LocalizationManager.Instance.SetCulture(_originalCulture);
        _logger.Dispose();
    }

    [Test]
    public void Open_LoadsBackingFieldsWithoutSavingOrNotifyingCultureChange()
    {
        var configuration = _configurationService.Configuration.Value;
        configuration.RestoreLastFoldersOnStartup = false;
        configuration.AdaptiveHelpEnabled = false;
        configuration.AdaptiveHelpReturningUserEnabled = false;
        configuration.AdaptiveHelpDifficultyAssistanceEnabled = false;
        configuration.AdaptiveHelpNewFeaturesEnabled = false;
        configuration.AdaptiveHelpDetailLevel = AdaptiveHelpDetailLevel.Detailed;
        configuration.DragDropModifier = DragDropModifierKey.Meta;
        configuration.CopyOnDragDropByDefault = true;
        configuration.FolderTagsEnabled = false;
        configuration.FolderTagApplicationLogEnabled = true;

        _viewModel.Open();

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.SelectedLanguage?.Name, Is.EqualTo("en"));
            Assert.That(_viewModel.RestoreLastFoldersOnStartup, Is.False);
            Assert.That(_viewModel.AdaptiveHelpEnabled, Is.False);
            Assert.That(_viewModel.AdaptiveHelpReturningUserEnabled, Is.False);
            Assert.That(_viewModel.AdaptiveHelpDifficultyAssistanceEnabled, Is.False);
            Assert.That(_viewModel.AdaptiveHelpNewFeaturesEnabled, Is.False);
            Assert.That(_viewModel.SelectedAdaptiveHelpDetailLevel?.Level, Is.EqualTo(AdaptiveHelpDetailLevel.Detailed));
            Assert.That(_viewModel.SelectedDragDropModifier?.Modifier, Is.EqualTo(DragDropModifierKey.Meta));
            Assert.That(_viewModel.CopyOnDragDropByDefault, Is.True);
            Assert.That(_viewModel.FolderTagsEnabled, Is.False);
            Assert.That(_viewModel.FolderTagApplicationLogEnabled, Is.True);
            Assert.That(_configurationService.SaveCallCount, Is.Zero);
            Assert.That(_cultureChangedCount, Is.Zero);
        });
    }

    [Test]
    public void UserSetters_SaveExactlyOnceOnlyForChangedSemanticValues()
    {
        _viewModel.Open();

        _viewModel.RestoreLastFoldersOnStartup = false;
        _viewModel.RestoreLastFoldersOnStartup = false;
        _viewModel.AdaptiveHelpEnabled = false;
        _viewModel.AdaptiveHelpEnabled = false;
        _viewModel.CopyOnDragDropByDefault = true;
        _viewModel.CopyOnDragDropByDefault = true;
        _viewModel.FolderTagsEnabled = false;
        _viewModel.FolderTagsEnabled = false;
        _viewModel.FolderTagApplicationLogEnabled = true;
        _viewModel.FolderTagApplicationLogEnabled = true;
        _viewModel.SelectedDragDropModifier = _viewModel.DragDropModifierOptions.Single(
            option => option.Modifier == DragDropModifierKey.Alt);
        _viewModel.SelectedDragDropModifier = new("Other label", DragDropModifierKey.Alt);
        _viewModel.SelectedAdaptiveHelpDetailLevel = _viewModel.AdaptiveHelpDetailLevelOptions.Single(
            option => option.Level == AdaptiveHelpDetailLevel.Detailed);
        _viewModel.SelectedAdaptiveHelpDetailLevel = new("Other label", AdaptiveHelpDetailLevel.Detailed);

        Assert.Multiple(() =>
        {
            Assert.That(_configurationService.SaveCallCount, Is.EqualTo(7));
            Assert.That(_configurationService.Configuration.Value.DragDropModifier, Is.EqualTo(DragDropModifierKey.Alt));
            Assert.That(_configurationService.Configuration.Value.AdaptiveHelpDetailLevel, Is.EqualTo(AdaptiveHelpDetailLevel.Detailed));
        });
    }

    [Test]
    public void LanguageChange_SavesOnceRebuildsLocalizedOptionsAndInvokesNarrowCallback()
    {
        var configuration = _configurationService.Configuration.Value;
        configuration.DragDropModifier = DragDropModifierKey.Shift;
        configuration.AdaptiveHelpDetailLevel = AdaptiveHelpDetailLevel.Minimal;
        _viewModel.Open();
        var previousModifier = _viewModel.SelectedDragDropModifier;
        var previousDetailLevel = _viewModel.SelectedAdaptiveHelpDetailLevel;
        var russian = _viewModel.AvailableLanguages.Single(language => language.Name == "ru");

        _viewModel.SelectedLanguage = russian;
        _viewModel.SelectedLanguage = new LanguageOption(russian.Culture);

        Assert.Multiple(() =>
        {
            Assert.That(LocalizationManager.Instance.Culture.Name, Is.EqualTo("ru"));
            Assert.That(configuration.UiCultureName, Is.EqualTo("ru"));
            Assert.That(_configurationService.SaveCallCount, Is.EqualTo(1));
            Assert.That(_cultureChangedCount, Is.EqualTo(1));
            Assert.That(_viewModel.SelectedDragDropModifier, Is.Not.SameAs(previousModifier));
            Assert.That(_viewModel.SelectedDragDropModifier?.Modifier, Is.EqualTo(DragDropModifierKey.Shift));
            Assert.That(_viewModel.SelectedAdaptiveHelpDetailLevel, Is.Not.SameAs(previousDetailLevel));
            Assert.That(_viewModel.SelectedAdaptiveHelpDetailLevel?.Level, Is.EqualTo(AdaptiveHelpDetailLevel.Minimal));
        });
    }

    [Test]
    public void AdaptiveHelpCommands_ResetOnlyTheirOwnedStateAndSaveOnceEach()
    {
        var configuration = _configurationService.Configuration.Value;
        var session = DateTimeOffset.UtcNow.AddDays(-2);
        configuration.AdaptiveHelpCompletedHintIds = ["completed"];
        configuration.AdaptiveHelpDismissedHintIds = ["dismissed"];
        configuration.AdaptiveHelpProgress = [new AdaptiveHelpProgressEntry { HintId = "progress" }];
        configuration.AdaptiveHelpUserGoal = AdaptiveHelpUserGoal.CopyMedia;
        configuration.AdaptiveHelpSessionHistoryUtc = [session];
        configuration.AdaptiveHelpLastSessionUtc = session;
        configuration.AdaptiveHelpLastShownUtc = session;

        _viewModel.ResetAdaptiveHelpProgressCommand.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(configuration.AdaptiveHelpCompletedHintIds, Is.Empty);
            Assert.That(configuration.AdaptiveHelpDismissedHintIds, Is.Empty);
            Assert.That(configuration.AdaptiveHelpProgress, Is.Empty);
            Assert.That(configuration.AdaptiveHelpUserGoal, Is.EqualTo(AdaptiveHelpUserGoal.Unknown));
            Assert.That(configuration.AdaptiveHelpSessionHistoryUtc, Is.EqualTo(new[] { session }));
            Assert.That(configuration.AdaptiveHelpLastSessionUtc, Is.EqualTo(session));
            Assert.That(configuration.AdaptiveHelpLastShownUtc, Is.EqualTo(session));
            Assert.That(_configurationService.SaveCallCount, Is.EqualTo(1));
        });

        configuration.AdaptiveHelpCompletedHintIds.Add("keep");
        _viewModel.ClearAdaptiveHelpHistoryCommand.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(configuration.AdaptiveHelpSessionHistoryUtc, Is.Empty);
            Assert.That(configuration.AdaptiveHelpLastSessionUtc, Is.Null);
            Assert.That(configuration.AdaptiveHelpLastShownUtc, Is.Null);
            Assert.That(configuration.AdaptiveHelpCompletedHintIds, Is.EqualTo(new[] { "keep" }));
            Assert.That(_configurationService.SaveCallCount, Is.EqualTo(2));
        });
    }
}

[TestFixture]
public sealed class SafeDeletionSettingsViewModelTests
{
    private FakeConfigurationService _configurationService = null!;
    private FakeFileDialogService _fileDialogService = null!;
    private Serilog.Core.Logger _logger = null!;
    private SafeDeletionSettingsViewModel _viewModel = null!;

    [SetUp]
    public void SetUp()
    {
        _configurationService = new FakeConfigurationService();
        _fileDialogService = new FakeFileDialogService();
        _logger = new LoggerConfiguration().CreateLogger();
        _viewModel = new SafeDeletionSettingsViewModel(
            _configurationService,
            _fileDialogService,
            _logger,
            LocalizationManager.Instance,
            _configurationService.SaveConfiguration);
    }

    [TearDown]
    public void TearDown() => _logger.Dispose();

    [TestCase(0, 1)]
    [TestCase(10_001, 10_000)]
    public void Open_ClampsDisplayedMaximumWithoutSavingOrMutatingConfiguration(int configured, int expected)
    {
        var configuration = _configurationService.Configuration.Value;
        configuration.SafeDeletionEnabled = false;
        configuration.SafeDeletionDirectoryPath = "/buffer";
        configuration.SafeDeletionMaxFileCount = configured;

        _viewModel.Open();

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.Enabled, Is.False);
            Assert.That(_viewModel.DirectoryPath, Is.EqualTo("/buffer"));
            Assert.That(_viewModel.MaxFileCount, Is.EqualTo(expected));
            Assert.That(configuration.SafeDeletionMaxFileCount, Is.EqualTo(configured));
            Assert.That(_configurationService.SaveCallCount, Is.Zero);
        });
    }

    [Test]
    public void UserSetters_ClampAndSaveExactlyOnceOnlyWhenChanged()
    {
        _viewModel.Open();

        _viewModel.Enabled = false;
        _viewModel.Enabled = false;
        _viewModel.DirectoryPath = "/new-buffer";
        _viewModel.DirectoryPath = "/new-buffer";
        _viewModel.MaxFileCount = 50_000;
        _viewModel.MaxFileCount = 20_000;

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.MaxFileCount, Is.EqualTo(10_000));
            Assert.That(_configurationService.Configuration.Value.SafeDeletionMaxFileCount, Is.EqualTo(10_000));
            Assert.That(_configurationService.SaveCallCount, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task Picker_CancellationDoesNothingAndSuccessUsesPathSetterOnce()
    {
        _viewModel.Open();
        var initialPath = _viewModel.DirectoryPath;
        _fileDialogService.SelectedFolder = null;

        await _viewModel.ChooseDirectoryCommand.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.DirectoryPath, Is.EqualTo(initialPath));
            Assert.That(_configurationService.SaveCallCount, Is.Zero);
        });

        _fileDialogService.SelectedFolder = "/chosen-buffer";
        await _viewModel.ChooseDirectoryCommand.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.DirectoryPath, Is.EqualTo("/chosen-buffer"));
            Assert.That(_configurationService.Configuration.Value.SafeDeletionDirectoryPath, Is.EqualTo("/chosen-buffer"));
            Assert.That(_configurationService.SaveCallCount, Is.EqualTo(1));
        });
    }
}

[TestFixture]
[NonParallelizable]
public sealed class McpServerSettingsViewModelTests
{
    private FakeConfigurationService _configurationService = null!;
    private FakeMcpServerController _controller = null!;
    private Serilog.Core.Logger _logger = null!;
    private McpServerSettingsViewModel _viewModel = null!;

    [SetUp]
    public void SetUp()
    {
        _configurationService = new FakeConfigurationService();
        _controller = new FakeMcpServerController();
        _logger = new LoggerConfiguration().CreateLogger();
        _viewModel = new McpServerSettingsViewModel(
            _configurationService,
            _controller,
            new ImmediateSynchronizationContext(),
            _logger,
            LocalizationManager.Instance,
            _configurationService.SaveConfiguration);
    }

    [TearDown]
    public void TearDown() => _logger.Dispose();

    [Test]
    public void Open_ClampsPortAndRefreshesStatusWithoutSavingOrControllerCalls()
    {
        var configuration = _configurationService.Configuration.Value;
        configuration.McpServerEnabled = true;
        configuration.McpServerPort = 80;
        _controller.IsRunning = true;
        _controller.EndpointUrl = "http://127.0.0.1:5277/mcp";

        _viewModel.Open();

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.Enabled, Is.True);
            Assert.That(_viewModel.Port, Is.EqualTo(1024));
            Assert.That(configuration.McpServerPort, Is.EqualTo(80));
            Assert.That(_viewModel.Status, Does.Contain(_controller.EndpointUrl));
            Assert.That(_configurationService.SaveCallCount, Is.Zero);
            Assert.That(_controller.Calls, Is.Empty);
        });
    }

    [Test]
    public async Task EnableAndDisable_SaveOncePerChangeAndApplyLifecycle()
    {
        _viewModel.Open();

        _viewModel.Enabled = true;
        await _viewModel.WaitForPendingOperationsAsync();
        _viewModel.Enabled = true;
        _viewModel.Enabled = false;
        await _viewModel.WaitForPendingOperationsAsync();
        _viewModel.Enabled = false;

        Assert.Multiple(() =>
        {
            Assert.That(_controller.Calls, Is.EqualTo(new[] { "start:5277", "stop" }));
            Assert.That(_controller.IsRunning, Is.False);
            Assert.That(_viewModel.Status, Is.EqualTo(LocalizationManager.Instance["Settings_McpServerStatusStopped"]));
            Assert.That(_configurationService.SaveCallCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task PortChangeWhileEnabled_RestartsServerAndClampsEquivalentValues()
    {
        var configuration = _configurationService.Configuration.Value;
        configuration.McpServerEnabled = true;
        _controller.IsRunning = true;
        _controller.EndpointUrl = "http://127.0.0.1:5277/mcp";
        _viewModel.Open();

        _viewModel.Port = 70_000;
        await _viewModel.WaitForPendingOperationsAsync();
        _viewModel.Port = 80_000;

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.Port, Is.EqualTo(65535));
            Assert.That(configuration.McpServerPort, Is.EqualTo(65535));
            Assert.That(_controller.Calls, Is.EqualTo(new[] { "stop", "start:65535" }));
            Assert.That(_configurationService.SaveCallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StartFailure_ProducesLocalizedFailureStatus()
    {
        _controller.StartError = new InvalidOperationException("port unavailable");
        _viewModel.Open();

        _viewModel.Enabled = true;
        await _viewModel.WaitForPendingOperationsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.Status, Does.Contain("port unavailable"));
            Assert.That(_controller.LastError, Is.EqualTo("port unavailable"));
            Assert.That(_controller.IsRunning, Is.False);
        });
    }

    [Test]
    public async Task RapidEnableDisable_IsSerializedAndFinalStateWins()
    {
        _controller.BlockNextStart();
        _viewModel.Open();

        _viewModel.Enabled = true;
        await _controller.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        _viewModel.Enabled = false;
        _controller.ReleaseStart();
        await _viewModel.WaitForPendingOperationsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_controller.Calls, Is.EqualTo(new[] { "start:5277", "stop" }));
            Assert.That(_controller.MaximumConcurrentOperations, Is.EqualTo(1));
            Assert.That(_controller.IsRunning, Is.False);
            Assert.That(_viewModel.Enabled, Is.False);
            Assert.That(_viewModel.Status, Is.EqualTo(LocalizationManager.Instance["Settings_McpServerStatusStopped"]));
            Assert.That(_configurationService.SaveCallCount, Is.EqualTo(2));
        });
    }
}

internal sealed class FakeConfigurationService : IConfigurationService
{
    public IObservable<Configuration> ConfigurationSaved => Observable.Never<Configuration>();
    public Lazy<Configuration> Configuration { get; } = new(() => new Configuration());
    public int SaveCallCount { get; private set; }
    public void SaveConfiguration() => SaveCallCount++;
}

internal sealed class FakeFileDialogService : IFileDialogService
{
    public string? SelectedFolder { get; set; }
    public Task<string?> ShowOpenFolderDialogAsync(string title) => Task.FromResult(SelectedFolder);
    public Task<string?> ShowOpenFileDialogAsync(string title, params (string Name, string[] Patterns)[] filters) =>
        throw new NotSupportedException();
    public Task<string?> ShowOpenApplicationDialogAsync(string title) => throw new NotSupportedException();
}

internal sealed class ImmediateSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    public override void Send(SendOrPostCallback callback, object? state) => callback(state);
}

internal sealed class FakeMcpServerController : IImageCareMcpServerController
{
    private TaskCompletionSource? _startRelease;
    private int _concurrentOperations;

    public bool IsRunning { get; set; }
    public string? EndpointUrl { get; set; }
    public string? LastError { get; private set; }
    public Exception? StartError { get; set; }
    public List<string> Calls { get; } = [];
    public int MaximumConcurrentOperations { get; private set; }
    public TaskCompletionSource StartEntered { get; private set; } = CompletedSource();

    public void BlockNextStart()
    {
        StartEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _startRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void ReleaseStart() => _startRelease?.TrySetResult();

    public async Task StartAsync(
        int? port = null,
        string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        EnterOperation();
        try
        {
            Calls.Add($"start:{port}");
            StartEntered.TrySetResult();
            if (_startRelease != null)
            {
                await _startRelease.Task.WaitAsync(cancellationToken);
                _startRelease = null;
            }

            if (StartError != null)
            {
                LastError = StartError.Message;
                throw StartError;
            }

            IsRunning = true;
            EndpointUrl = $"http://127.0.0.1:{port}/mcp";
            LastError = null;
        }
        finally
        {
            ExitOperation();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        EnterOperation();
        try
        {
            Calls.Add("stop");
            IsRunning = false;
            EndpointUrl = null;
            return Task.CompletedTask;
        }
        finally
        {
            ExitOperation();
        }
    }

    private void EnterOperation()
    {
        _concurrentOperations++;
        MaximumConcurrentOperations = Math.Max(MaximumConcurrentOperations, _concurrentOperations);
    }

    private void ExitOperation() => _concurrentOperations--;

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
