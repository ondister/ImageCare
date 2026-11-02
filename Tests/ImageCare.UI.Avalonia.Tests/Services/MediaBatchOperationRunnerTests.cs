using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Services.MediaBatchOperationsService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;

using Serilog;

using Notification = ImageCare.Core.Domain.Notification.Notification;

namespace ImageCare.UI.Avalonia.Tests.Services;

[TestFixture]
public sealed class MediaBatchOperationRunnerTests
{
    private FakeNotificationService _notificationService = null!;
    private LocalizationManager _localization = null!;
    private MediaBatchOperationRunner _runner = null!;

    [SetUp]
    public void SetUp()
    {
        _notificationService = new FakeNotificationService();
        _localization = LocalizationManager.Instance;
        _runner = new MediaBatchOperationRunner(
            _notificationService,
            _localization,
            new LoggerConfiguration().CreateLogger());
    }

    [Test]
    public async Task RunAsync_OnSuccess_ForwardsProgressAndSendsLocalizedNotifications()
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
        try
        {
            var progressReports = new List<MediaBatchOperationProgress>();
            var expectedProgress = new MediaBatchOperationProgress(
                MediaBatchOperationKind.Copy,
                1,
                2,
                "/source/one.jpg");
            using var cancellation = new CancellationTokenSource();

            await _runner.RunAsync(
                MediaBatchOperationKind.Copy,
                "Copy started",
                cancellation.Token,
                progressReports.Add,
                (progress, token) =>
                {
                    Assert.That(token, Is.EqualTo(cancellation.Token));
                    progress.Report(expectedProgress);
                    return Task.FromResult(CreateSummary(MediaBatchOperationKind.Copy, total: 2, succeeded: 2));
                });

            Assert.Multiple(() =>
            {
                Assert.That(progressReports, Is.EqualTo(new[] { expectedProgress }));
                Assert.That(_notificationService.Notifications, Has.Count.EqualTo(2));
                Assert.That(_notificationService.Notifications[0].GetType(), Is.EqualTo(typeof(Notification)));
                Assert.That(_notificationService.Notifications[0].Title, Is.EqualTo("Copy started"));
                Assert.That(_notificationService.Notifications[1], Is.TypeOf<SuccessNotification>());
                Assert.That(
                    _notificationService.Notifications[1].Title,
                    Is.EqualTo(_localization.Get("Batch_Completed_Copy", 2, 2)));
            });
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [Test]
    public async Task RunAsync_OnCancelledSummary_SetsCompletedStatusAndSendsCancellationNotification()
    {
        var statuses = new List<string>();

        await _runner.RunAsync(
            MediaBatchOperationKind.Move,
            "Move started",
            CancellationToken.None,
            _ => { },
            (_, _) => Task.FromResult(
                CreateSummary(MediaBatchOperationKind.Move, total: 3, succeeded: 1, cancelled: true)),
            statuses.Add);

        var completedTitle = _localization.Get("Batch_Completed_Move", 1, 3);
        Assert.Multiple(() =>
        {
            Assert.That(statuses, Is.EqualTo(new[] { completedTitle }));
            Assert.That(_notificationService.Notifications, Has.Count.EqualTo(2));
            Assert.That(_notificationService.Notifications[1].GetType(), Is.EqualTo(typeof(Notification)));
            Assert.That(
                _notificationService.Notifications[1].Title,
                Is.EqualTo(_localization["Batch_CancelledTitle"]));
            Assert.That(_notificationService.Notifications[1].Description, Is.EqualTo(completedTitle));
        });
    }

    [Test]
    public async Task RunAsync_OnPartialFailure_UsesFirstFailureInErrorNotification()
    {
        var failure = new MediaBatchOperationFailure("/source/two.jpg", "Disk full");

        await _runner.RunAsync(
            MediaBatchOperationKind.ExportJpeg,
            "Export started",
            CancellationToken.None,
            _ => { },
            (_, _) => Task.FromResult(
                CreateSummary(MediaBatchOperationKind.ExportJpeg, total: 2, succeeded: 1, failures: [failure])));

        var notification = _notificationService.Notifications[1];
        Assert.Multiple(() =>
        {
            Assert.That(notification, Is.TypeOf<ErrorNotification>());
            Assert.That(notification.Title, Is.EqualTo(_localization.Get("Batch_Completed_Export", 1, 2)));
            Assert.That(
                notification.Description,
                Is.EqualTo(_localization.Get("Batch_FailedDetail", 1, failure.Error)));
        });
    }

    [Test]
    public async Task RunAsync_WhenOperationThrowsCancellation_SetsCancelledStatusAndDoesNotRethrow()
    {
        var statuses = new List<string>();

        await _runner.RunAsync(
            MediaBatchOperationKind.Delete,
            "Delete started",
            CancellationToken.None,
            _ => { },
            (_, _) => Task.FromException<MediaBatchOperationSummary>(new OperationCanceledException()),
            statuses.Add);

        var cancelledTitle = _localization["Batch_CancelledTitle"];
        Assert.Multiple(() =>
        {
            Assert.That(statuses, Is.EqualTo(new[] { cancelledTitle }));
            Assert.That(_notificationService.Notifications, Has.Count.EqualTo(2));
            Assert.That(_notificationService.Notifications[1].GetType(), Is.EqualTo(typeof(Notification)));
            Assert.That(_notificationService.Notifications[1].Title, Is.EqualTo(cancelledTitle));
            Assert.That(_notificationService.Notifications[1].Description, Is.Empty);
        });
    }

    [Test]
    public async Task RunAsync_WhenOperationThrowsException_SetsFailedStatusAndSendsErrorNotification()
    {
        var statuses = new List<string>();
        var exception = new InvalidOperationException("Operation failed");

        await _runner.RunAsync(
            MediaBatchOperationKind.Copy,
            "Copy started",
            CancellationToken.None,
            _ => { },
            (_, _) => Task.FromException<MediaBatchOperationSummary>(exception),
            statuses.Add);

        var failedTitle = _localization["Batch_FailedTitle"];
        Assert.Multiple(() =>
        {
            Assert.That(statuses, Is.EqualTo(new[] { failedTitle }));
            Assert.That(_notificationService.Notifications, Has.Count.EqualTo(2));
            Assert.That(_notificationService.Notifications[1], Is.TypeOf<ErrorNotification>());
            Assert.That(_notificationService.Notifications[1].Title, Is.EqualTo(failedTitle));
            Assert.That(_notificationService.Notifications[1].Description, Is.EqualTo(exception.Message));
        });
    }

    private static MediaBatchOperationSummary CreateSummary(
        MediaBatchOperationKind kind,
        int total,
        int succeeded,
        IReadOnlyList<MediaBatchOperationFailure>? failures = null,
        bool cancelled = false)
    {
        return new MediaBatchOperationSummary(
            kind,
            total,
            succeeded,
            failures ?? [],
            cancelled,
            []);
    }

    private sealed class FakeNotificationService : INotificationService
    {
        public List<Notification> Notifications { get; } = [];

        public IObservable<Notification> NotificationReceived { get; } = new EmptyObservable();

        public void SendNotification(Notification notification) => Notifications.Add(notification);
    }

    private sealed class EmptyObservable : IObservable<Notification>
    {
        public IDisposable Subscribe(IObserver<Notification> observer) => EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static EmptyDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }
}
