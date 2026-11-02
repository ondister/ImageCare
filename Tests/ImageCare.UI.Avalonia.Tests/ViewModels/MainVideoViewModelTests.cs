using System.Reactive.Subjects;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Preview;
using ImageCare.UI.Avalonia.Services.MediaPlayer;
using ImageCare.UI.Avalonia.ViewModels;

using Prism.Navigation;
using Prism.Navigation.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.Tests.ViewModels;

[TestFixture]
public sealed class MainVideoViewModelTests
{
    private Subject<SelectedMediaPreview> _previewSelections = null!;
    private Subject<SelectedDirectory> _folderSelections = null!;
    private ImmediateSynchronizationContext _synchronizationContext = null!;
    private Serilog.Core.Logger _logger = null!;
    private MainVideoViewModel _viewModel = null!;
    private FakeMediaPlayerAdapter _player = null!;

    [SetUp]
    public void SetUp()
    {
        _previewSelections = new Subject<SelectedMediaPreview>();
        _folderSelections = new Subject<SelectedDirectory>();
        _synchronizationContext = new ImmediateSynchronizationContext();
        _logger = new LoggerConfiguration().CreateLogger();
        _viewModel = new MainVideoViewModel(
            _previewSelections,
            _folderSelections,
            _logger,
            _synchronizationContext);
        _player = new FakeMediaPlayerAdapter();
        _viewModel.Player = _player;
    }

    [TearDown]
    public void TearDown()
    {
        _viewModel.OnNavigatedFrom(CreateNavigationContext());
        _player.Dispose();
        _previewSelections.Dispose();
        _folderSelections.Dispose();
        _logger.Dispose();
    }

    [Test]
    public void ShowAndPlay_SynchronizeSourceStateAndCommands()
    {
        _viewModel.Show(CreatePreview("video-a.mp4"));

        Assert.Multiple(() =>
        {
            Assert.That(_player.Source, Is.EqualTo("video-a.mp4"));
            Assert.That(_viewModel.PlayCommand.CanExecute(null), Is.True);
            Assert.That(_viewModel.PauseCommand.CanExecute(null), Is.False);
            Assert.That(_viewModel.StopCommand.CanExecute(null), Is.False);
        });

        _viewModel.PlayCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(_player.PlayCallCount, Is.EqualTo(1));
            Assert.That(_viewModel.IsPlaying, Is.True);
            Assert.That(_viewModel.HasMediaLoaded, Is.True);
            Assert.That(_viewModel.PlayCommand.CanExecute(null), Is.False);
            Assert.That(_viewModel.PauseCommand.CanExecute(null), Is.True);
            Assert.That(_viewModel.StopCommand.CanExecute(null), Is.True);
        });
    }

    [Test]
    public void PauseAndStop_UseAdapterStateForCommandAvailability()
    {
        _viewModel.Show(CreatePreview("video.mp4"));
        _viewModel.PlayCommand.Execute(null);

        _viewModel.PauseCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(_player.TogglePauseCallCount, Is.EqualTo(1));
            Assert.That(_viewModel.IsPlaying, Is.False);
            Assert.That(_viewModel.HasMediaLoaded, Is.True);
            Assert.That(_viewModel.PlayCommand.CanExecute(null), Is.True);
        });

        _viewModel.StopCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(_player.StopCallCount, Is.EqualTo(2), "Show stops the previous media before setting its source.");
            Assert.That(_viewModel.IsPlaying, Is.False);
            Assert.That(_viewModel.HasMediaLoaded, Is.False);
            Assert.That(_viewModel.PauseCommand.CanExecute(null), Is.False);
            Assert.That(_viewModel.StopCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public void PlayerProperties_ProjectAndUpdateTheAdapterContract()
    {
        _player.Position = TimeSpan.FromSeconds(15);
        _player.DurationValue = TimeSpan.FromSeconds(60);
        _player.RemainingValue = TimeSpan.FromSeconds(45);
        _player.Percent = 25;
        _player.IsSeekableValue = true;
        _player.Volume = 70;
        _player.RaisePositionChanged();

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.TimePosition, Is.EqualTo(TimeSpan.FromSeconds(15)));
            Assert.That(_viewModel.Duration, Is.EqualTo(TimeSpan.FromSeconds(60)));
            Assert.That(_viewModel.TimeRemaining, Is.EqualTo(TimeSpan.FromSeconds(45)));
            Assert.That(_viewModel.PercentPos, Is.EqualTo(25));
            Assert.That(_viewModel.IsSeekable, Is.True);
            Assert.That(_viewModel.Volume, Is.EqualTo(70));
        });

        _viewModel.TimePosition = TimeSpan.FromSeconds(30);
        _viewModel.PercentPos = 50;
        _viewModel.Volume = 35;

        Assert.Multiple(() =>
        {
            Assert.That(_player.Position, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(_player.Percent, Is.EqualTo(50));
            Assert.That(_player.Volume, Is.EqualTo(35));
        });
    }

    [Test]
    public void ReplacingPlayer_DetachesOldEventsAndSynchronizesCurrentSource()
    {
        _viewModel.Show(CreatePreview("current.mp4"));
        var replacement = new FakeMediaPlayerAdapter();

        _viewModel.Player = replacement;
        _player.SetPlaybackState(hasMediaLoaded: true, isPlaying: true);

        Assert.Multiple(() =>
        {
            Assert.That(replacement.Source, Is.EqualTo("current.mp4"));
            Assert.That(_viewModel.HasMediaLoaded, Is.False);
            Assert.That(_viewModel.IsPlaying, Is.False);
        });

        replacement.SetPlaybackState(hasMediaLoaded: true, isPlaying: true);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.HasMediaLoaded, Is.True);
            Assert.That(_viewModel.IsPlaying, Is.True);
        });
    }

    [Test]
    public void FailureAndStateEvents_AreMarshaledThroughSynchronizationContext()
    {
        _viewModel.Show(CreatePreview("broken.mp4"));
        _player.SetPlaybackState(hasMediaLoaded: true, isPlaying: true);
        var postsBeforeFailure = _synchronizationContext.PostCallCount;

        _player.RaiseFailure(new InvalidOperationException("Cannot play media"));

        Assert.Multiple(() =>
        {
            Assert.That(_synchronizationContext.PostCallCount, Is.GreaterThan(postsBeforeFailure));
            Assert.That(_viewModel.HasMediaLoaded, Is.False);
            Assert.That(_viewModel.IsPlaying, Is.False);
        });
    }

    [Test]
    public async Task Navigation_SubscriptionsSelectClearAndDetachMedia()
    {
        var context = CreateNavigationContext();
        context.Parameters.Add("imagePreview", CreatePreview("parameter.mp4"));

        _viewModel.OnNavigatedTo(context);
        Assert.That(_viewModel.MediaUrl, Is.EqualTo("parameter.mp4"));

        _folderSelections.OnNext(new SelectedDirectory("folder", "/folder", FileManagerPanel.Left));
        Assert.That(_viewModel.MediaUrl, Is.Empty);

        _previewSelections.OnNext(CreatePreview("observable.mp4"));
        await WaitUntilAsync(() => _viewModel.MediaUrl == "observable.mp4");

        _viewModel.OnNavigatedFrom(context);
        _previewSelections.OnNext(CreatePreview("detached.mp4"));
        await Task.Delay(200);

        Assert.That(_viewModel.MediaUrl, Is.EqualTo("observable.mp4"));
    }

    private static SelectedMediaPreview CreatePreview(string path) =>
        new("video", path, MediaFormat.MediaFormatMp4, 100, FileManagerPanel.Left);

    private static NavigationContext CreateNavigationContext(NavigationParameters? parameters = null) =>
        new(null!, new Uri("video", UriKind.Relative), parameters ?? new NavigationParameters());

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.That(condition(), Is.True, "Timed out waiting for the throttled media selection.");
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public int PostCallCount { get; private set; }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            PostCallCount++;
            callback(state);
        }
    }

    private sealed class FakeMediaPlayerAdapter : IMediaPlayerAdapter
    {
        private string _source = string.Empty;

        public event EventHandler? StateChanged;
        public event EventHandler? PositionChanged;
        public event Action<Exception>? Failed;

        public int PlayCallCount { get; private set; }
        public int TogglePauseCallCount { get; private set; }
        public int StopCallCount { get; private set; }

        public string Source
        {
            get => _source;
            set
            {
                _source = value;
                HasMediaLoaded = false;
                IsPlaying = false;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public TimeSpan Position { get; set; }
        public TimeSpan Duration => DurationValue;
        public TimeSpan DurationValue { get; set; }
        public TimeSpan Remaining => RemainingValue;
        public TimeSpan RemainingValue { get; set; }
        public double Percent { get; set; }
        public bool IsSeekable => IsSeekableValue;
        public bool IsSeekableValue { get; set; }
        public double Volume { get; set; }
        public bool IsPlaying { get; private set; }
        public bool HasMediaLoaded { get; private set; }

        public void Attach()
        {
        }

        public void Detach()
        {
        }

        public Task LoadAsync()
        {
            HasMediaLoaded = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task PlayAsync()
        {
            PlayCallCount++;
            HasMediaLoaded = true;
            IsPlaying = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public void TogglePause()
        {
            TogglePauseCallCount++;
            IsPlaying = !IsPlaying;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Stop()
        {
            StopCallCount++;
            HasMediaLoaded = false;
            IsPlaying = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetPlaybackState(bool hasMediaLoaded, bool isPlaying)
        {
            HasMediaLoaded = hasMediaLoaded;
            IsPlaying = isPlaying;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RaisePositionChanged() => PositionChanged?.Invoke(this, EventArgs.Empty);

        public void RaiseFailure(Exception exception)
        {
            HasMediaLoaded = false;
            IsPlaying = false;
            Failed?.Invoke(exception);
        }

        public void Dispose()
        {
        }
    }
}
