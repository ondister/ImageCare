using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Settings;

using Serilog;

namespace ImageCare.UI.Avalonia.Tests.ViewModels.Settings;

[TestFixture]
public sealed class AnnotationsSettingsViewModelTests
{
    private FakeConfigurationService _configurationService = null!;
    private FakeFileDialogService _fileDialogService = null!;
    private FakeAnnotationsService _annotationsService = null!;
    private Serilog.Core.Logger _logger = null!;
    private AnnotationsSettingsViewModel _viewModel = null!;

    [SetUp]
    public void SetUp()
    {
        _configurationService = new FakeConfigurationService();
        _fileDialogService = new FakeFileDialogService();
        _annotationsService = new FakeAnnotationsService();
        _logger = new LoggerConfiguration().CreateLogger();
        _viewModel = new AnnotationsSettingsViewModel(
            _configurationService,
            _fileDialogService,
            _annotationsService,
            new ImmediateSynchronizationContext(),
            _logger,
            LocalizationManager.Instance,
            _configurationService.SaveConfiguration);
    }

    [TearDown]
    public async Task TearDown()
    {
        _viewModel.Close();
        await _annotationsService.DisposeAsync();
        _logger.Dispose();
    }

    [Test]
    public void OpenClose_NormalizesConfigurationAndOwnsStatusSubscription()
    {
        var root = TestPath("library");
        var configuration = _configurationService.Configuration.Value;
        configuration.PinnedTags = [null!, "  Bird   tag ", "bird tag", ""];
        configuration.MediaSearchRootPaths = [root + Path.DirectorySeparatorChar, Path.Combine(root, "."), ""];

        _viewModel.Open();
        _annotationsService.Publish(
            new AnnotationsReconciliationStatus(AnnotationsReconciliationState.Running, root, 3));

        Assert.Multiple(() =>
        {
            Assert.That(configuration.PinnedTags, Is.EqualTo(new[] { "Bird tag" }));
            Assert.That(configuration.MediaSearchRootPaths, Is.EqualTo(new[] { Path.GetFullPath(root) }));
            Assert.That(_viewModel.MediaSearchRootPaths, Is.EqualTo(configuration.MediaSearchRootPaths));
            Assert.That(_viewModel.AvailableTags, Is.EqualTo(new[] { "Bird tag" }));
            Assert.That(_viewModel.IsAnnotationsBusy, Is.True);
            Assert.That(_annotationsService.ObserverCount, Is.EqualTo(1));
            Assert.That(_configurationService.SaveCallCount, Is.EqualTo(1));
        });

        var runningStatus = _viewModel.AnnotationsStatus;
        _viewModel.Close();
        _annotationsService.Publish(
            new AnnotationsReconciliationStatus(AnnotationsReconciliationState.Completed, root, 4));

        Assert.Multiple(() =>
        {
            Assert.That(_annotationsService.ObserverCount, Is.Zero);
            Assert.That(_viewModel.AnnotationsStatus, Is.EqualTo(runningStatus));
            Assert.That(_viewModel.MediaSearchRootPaths, Is.Empty);
            Assert.That(_viewModel.AvailableTags, Is.Empty);
        });
    }

    [Test]
    public void PinnedTags_AddDuplicateRemoveAndRefreshAvailableTags()
    {
        _configurationService.Configuration.Value.PinnedTags = ["Existing"];
        _viewModel.Open();
        var savesAfterOpen = _configurationService.SaveCallCount;

        _viewModel.NewPinnedTag = "  New   tag ";
        _viewModel.AddPinnedTagCommand.Execute();
        _viewModel.NewPinnedTag = "new tag";
        _viewModel.AddPinnedTagCommand.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(_configurationService.Configuration.Value.PinnedTags, Is.EquivalentTo(new[] { "Existing", "New tag" }));
            Assert.That(_viewModel.AvailableTags, Is.EquivalentTo(new[] { "Existing", "New tag" }));
            Assert.That(_configurationService.SaveCallCount, Is.EqualTo(savesAfterOpen + 1));
        });

        _viewModel.SelectedPinnedTag = "NEW TAG";
        _viewModel.RemovePinnedTagCommand.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(_configurationService.Configuration.Value.PinnedTags, Is.EqualTo(new[] { "Existing" }));
            Assert.That(_viewModel.AvailableTags, Is.EqualTo(new[] { "Existing" }));
            Assert.That(_viewModel.SelectedPinnedTag, Is.Null);
        });
    }

    [Test]
    public async Task Roots_NormalizeDeduplicateAddRegisterAndRemove()
    {
        var existingRoot = TestPath("existing");
        var addedRoot = TestPath("added");
        _configurationService.Configuration.Value.MediaSearchRootPaths =
            [existingRoot + Path.DirectorySeparatorChar, Path.Combine(existingRoot, ".")];
        _viewModel.Open();
        var savesAfterOpen = _configurationService.SaveCallCount;

        _fileDialogService.SelectedFolder = Path.Combine(existingRoot, ".");
        await _viewModel.AddMediaSearchRootCommand.ExecuteAsync();
        Assert.That(_configurationService.SaveCallCount, Is.EqualTo(savesAfterOpen));

        _fileDialogService.SelectedFolder = addedRoot + Path.DirectorySeparatorChar;
        await _viewModel.AddMediaSearchRootCommand.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.MediaSearchRootPaths, Is.EqualTo(new[] { Path.GetFullPath(existingRoot), Path.GetFullPath(addedRoot) }));
            Assert.That(_annotationsService.RegisteredDirectories, Is.EqualTo(new[] { Path.GetFullPath(addedRoot) }));
            Assert.That(_configurationService.SaveCallCount, Is.EqualTo(savesAfterOpen + 1));
        });

        _viewModel.SelectedMediaSearchRoot = Path.GetFullPath(existingRoot);
        _viewModel.RemoveMediaSearchRootCommand.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.MediaSearchRootPaths, Is.EqualTo(new[] { Path.GetFullPath(addedRoot) }));
            Assert.That(_configurationService.Configuration.Value.MediaSearchRootPaths, Is.EqualTo(_viewModel.MediaSearchRootPaths));
            Assert.That(_viewModel.SelectedMediaSearchRoot, Is.Null);
        });
    }

    [Test]
    public async Task Rebuild_KeepsBusyUntilServiceCompletes()
    {
        var root = TestPath("rebuild");
        _configurationService.Configuration.Value.MediaSearchRootPaths = [root];
        _annotationsService.RebuildCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _viewModel.Open();

        var rebuildTask = _viewModel.RebuildMediaIndexCommand.ExecuteAsync();
        await _annotationsService.RebuildStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.IsAnnotationsBusy, Is.True);
            Assert.That(_viewModel.RebuildMediaIndexCommand.CanExecute(), Is.False);
            Assert.That(_annotationsService.RebuiltRoots, Is.EqualTo(new[] { Path.GetFullPath(root) }));
        });

        _annotationsService.RebuildCompletion.SetResult();
        await rebuildTask;

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.IsAnnotationsBusy, Is.False);
            Assert.That(_viewModel.RebuildMediaIndexCommand.CanExecute(), Is.True);
        });
    }

    [Test]
    public async Task ImportTags_IgnoresCommentsDeduplicatesAndRefreshesAvailableTags()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"ImageCare-tags-{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(filePath, ["# comment", "   # indented comment", "Bird", " Night   sky ", "night sky"]);
        try
        {
            _configurationService.Configuration.Value.PinnedTags = ["Bird"];
            _fileDialogService.SelectedFile = filePath;
            _viewModel.Open();

            await _viewModel.ImportPinnedTagsCommand.ExecuteAsync();

            Assert.Multiple(() =>
            {
                Assert.That(_configurationService.Configuration.Value.PinnedTags, Is.EqualTo(new[] { "Bird", "Night sky" }));
                Assert.That(_viewModel.AvailableTags, Is.EqualTo(new[] { "Bird", "Night sky" }));
                Assert.That(
                    _viewModel.TagImportStatus,
                    Is.EqualTo(LocalizationManager.Instance.Get("Annotations_TagsImported", 1)));
            });
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static string TestPath(params string[] parts) =>
        Path.Combine([Path.GetTempPath(), "ImageCareAnnotationsSettingsTests", .. parts]);

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
        public override void Send(SendOrPostCallback callback, object? state) => callback(state);
    }

    private sealed class FakeConfigurationService : IConfigurationService
    {
        public IObservable<Configuration> ConfigurationSaved => Observable.Never<Configuration>();
        public Lazy<Configuration> Configuration { get; } = new(() => new Configuration());
        public int SaveCallCount { get; private set; }
        public void SaveConfiguration() => SaveCallCount++;
    }

    private sealed class FakeFileDialogService : IFileDialogService
    {
        public string? SelectedFile { get; set; }
        public string? SelectedFolder { get; set; }
        public Task<string?> ShowOpenFileDialogAsync(string title, params (string Name, string[] Patterns)[] filters) =>
            Task.FromResult(SelectedFile);
        public Task<string?> ShowOpenFolderDialogAsync(string title) => Task.FromResult(SelectedFolder);
        public Task<string?> ShowOpenApplicationDialogAsync(string title) => throw new NotSupportedException();
    }

    private sealed class FakeAnnotationsService : IMediaAnnotationsService
    {
        private readonly Subject<AnnotationsReconciliationStatus> _status = new();

        public IObservable<AnnotationsReconciliationStatus> ReconciliationStatus =>
            Observable.Create<AnnotationsReconciliationStatus>(observer =>
            {
                ObserverCount++;
                var subscription = _status.Subscribe(observer);
                return Disposable.Create(() =>
                {
                    subscription.Dispose();
                    ObserverCount--;
                });
            });

        public int ObserverCount { get; private set; }
        public List<string> RegisteredDirectories { get; } = [];
        public IReadOnlyList<string> RebuiltRoots { get; private set; } = [];
        public TaskCompletionSource RebuildStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RebuildCompletion { get; set; } = CompletedSource();

        public void Publish(AnnotationsReconciliationStatus status) => _status.OnNext(status);
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<MediaAnnotations> GetAsync(string mediaPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaAnnotationsSnapshot> GetSnapshotAsync(string mediaPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaAnnotationsSnapshot> SaveAsync(string mediaPath, MediaAnnotations annotations, AnnotationFileState? expectedState = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IndexedMediaAnnotations>> GetDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IndexedMediaAnnotations>> SearchAsync(MediaAnnotationsSearchQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task RegisterDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            RegisteredDirectories.Add(directoryPath);
            return Task.CompletedTask;
        }

        public Task RefreshDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RegisterRootsAsync(IEnumerable<string> roots, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task RebuildRootsAsync(IEnumerable<string> roots, CancellationToken cancellationToken = default)
        {
            RebuiltRoots = roots.ToArray();
            RebuildStarted.TrySetResult();
            await RebuildCompletion.Task.WaitAsync(cancellationToken);
        }

        public Task ReconcileRegisteredRootsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartBackgroundReconciliationAsync(TimeSpan interval, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopBackgroundReconciliationAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static TaskCompletionSource CompletedSource()
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetResult();
            return source;
        }
    }
}
