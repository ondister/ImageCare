using System.Reactive.Linq;

using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.UI.Avalonia.Services;

using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ImageCare.UI.Avalonia.Tests.Services;

[TestFixture]
public sealed class AdaptiveHelpProgressStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void NormalizeState_InitializesNullCollections_FromOldConfiguration()
    {
        var configuration = new Configuration
        {
            AdaptiveHelpCompletedHintIds = null!,
            AdaptiveHelpDismissedHintIds = null!,
            AdaptiveHelpSessionHistoryUtc = null!,
            AdaptiveHelpProgress = null!
        };
        var store = CreateStore(configuration);

        store.NormalizeState(configuration);

        Assert.Multiple(() =>
        {
            Assert.That(configuration.AdaptiveHelpCompletedHintIds, Is.Empty);
            Assert.That(configuration.AdaptiveHelpDismissedHintIds, Is.Empty);
            Assert.That(configuration.AdaptiveHelpSessionHistoryUtc, Is.Empty);
            Assert.That(configuration.AdaptiveHelpProgress, Is.Empty);
        });
    }

    [Test]
    public void NormalizeState_MigratesCompletedHints_ToProgress()
    {
        var configuration = new Configuration
        {
            AdaptiveHelpCompletedHintIds =
            [
                AdaptiveHelpPolicy.SelectSourceHintId,
                AdaptiveHelpPolicy.SelectTargetHintId
            ],
            AdaptiveHelpProgress =
            [
                new AdaptiveHelpProgressEntry
                {
                    HintId = AdaptiveHelpPolicy.SelectSourceHintId,
                    MasteryLevel = AdaptiveHelpMasteryLevel.Tried
                }
            ]
        };
        var store = CreateStore(configuration);

        store.NormalizeState(configuration);

        Assert.Multiple(() =>
        {
            Assert.That(configuration.AdaptiveHelpProgress, Has.Count.EqualTo(2));
            Assert.That(
                configuration.AdaptiveHelpProgress.Single(entry =>
                    entry.HintId == AdaptiveHelpPolicy.SelectSourceHintId).MasteryLevel,
                Is.EqualTo(AdaptiveHelpMasteryLevel.Completed));
            Assert.That(
                configuration.AdaptiveHelpProgress.Single(entry =>
                    entry.HintId == AdaptiveHelpPolicy.SelectTargetHintId).MasteryLevel,
                Is.EqualTo(AdaptiveHelpMasteryLevel.Completed));
        });
    }

    [Test]
    public void RecordExposure_TracksTimeCountAndTriedMastery()
    {
        var configuration = new Configuration();
        var store = CreateStore(configuration);

        store.RecordExposure(configuration, AdaptiveHelpPolicy.TransferHintId, Now);
        store.RecordExposure(configuration, AdaptiveHelpPolicy.TransferHintId, Now.AddMinutes(1));

        var progress = configuration.AdaptiveHelpProgress.Single();
        Assert.Multiple(() =>
        {
            Assert.That(progress.ExposureCount, Is.EqualTo(2));
            Assert.That(progress.LastShownUtc, Is.EqualTo(Now.AddMinutes(1)));
            Assert.That(progress.MasteryLevel, Is.EqualTo(AdaptiveHelpMasteryLevel.Tried));
        });
    }

    [Test]
    public void RecordDismissal_DismissesAfterSecondClose()
    {
        var configuration = new Configuration();
        var store = CreateStore(configuration);

        store.RecordDismissal(configuration, AdaptiveHelpPolicy.SelectMediaHintId, dismissImmediately: false);
        Assert.That(configuration.AdaptiveHelpDismissedHintIds, Is.Empty);

        store.RecordDismissal(configuration, AdaptiveHelpPolicy.SelectMediaHintId, dismissImmediately: false);

        Assert.Multiple(() =>
        {
            Assert.That(configuration.AdaptiveHelpDismissedHintIds, Is.EqualTo([AdaptiveHelpPolicy.SelectMediaHintId]));
            Assert.That(configuration.AdaptiveHelpProgress.Single().DismissCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void RecordSuccessfulAction_MastersHintAfterThreeSuccesses()
    {
        var configuration = new Configuration();
        var store = CreateStore(configuration);

        store.RecordSuccessfulAction(configuration, AdaptiveHelpPolicy.TransferHintId, Now);
        store.RecordSuccessfulAction(configuration, AdaptiveHelpPolicy.TransferHintId, Now.AddMinutes(1));
        store.RecordSuccessfulAction(configuration, AdaptiveHelpPolicy.TransferHintId, Now.AddMinutes(2));

        var progress = configuration.AdaptiveHelpProgress.Single();
        Assert.Multiple(() =>
        {
            Assert.That(progress.SuccessfulActionCount, Is.EqualTo(3));
            Assert.That(progress.LastCompletedUtc, Is.EqualTo(Now.AddMinutes(2)));
            Assert.That(progress.MasteryLevel, Is.EqualTo(AdaptiveHelpMasteryLevel.Mastered));
            Assert.That(configuration.AdaptiveHelpCompletedHintIds, Is.EqualTo([AdaptiveHelpPolicy.TransferHintId]));
        });
    }

    [Test]
    public void RecordSession_PreservesSnapshotAndTrimsHistoryToTen()
    {
        var previousSession = Now.AddDays(-1);
        var history = Enumerable.Range(1, 10).Select(days => Now.AddDays(-days)).ToList();
        var originalHistory = history.ToArray();
        var configuration = new Configuration
        {
            AdaptiveHelpLastSessionUtc = previousSession,
            AdaptiveHelpSessionHistoryUtc = history,
            AdaptiveHelpLastSeenVersion = "0.1.1"
        };
        var store = CreateStore(configuration);

        var snapshot = store.RecordSession(configuration, Now, 2, "0.1.2");

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.PreviousSessionUtc, Is.EqualTo(previousSession));
            Assert.That(snapshot.SessionHistoryUtc, Is.EqualTo(originalHistory));
            Assert.That(snapshot.LastSeenVersion, Is.EqualTo("0.1.1"));
            Assert.That(configuration.AdaptiveHelpLastSessionUtc, Is.EqualTo(Now));
            Assert.That(configuration.AdaptiveHelpExperienceVersion, Is.EqualTo(2));
            Assert.That(configuration.AdaptiveHelpLastSeenVersion, Is.EqualTo("0.1.2"));
            Assert.That(configuration.AdaptiveHelpSessionHistoryUtc, Has.Count.EqualTo(10));
            Assert.That(configuration.AdaptiveHelpSessionHistoryUtc[^1], Is.EqualTo(Now));
            Assert.That(configuration.AdaptiveHelpSessionHistoryUtc[0], Is.EqualTo(originalHistory[1]));
        });
    }

    [Test]
    public void Save_LogsAndDoesNotThrow_WhenConfigurationSaveFails()
    {
        var configurationService = new TestConfigurationService(new Configuration())
        {
            SaveException = new IOException("save failed")
        };
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        var store = new AdaptiveHelpProgressStore(configurationService, logger);

        Assert.That(store.Save, Throws.Nothing);
        Assert.That(sink.Events, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(sink.Events[0].Level, Is.EqualTo(LogEventLevel.Error));
            Assert.That(sink.Events[0].Exception, Is.SameAs(configurationService.SaveException));
        });
    }

    private static AdaptiveHelpProgressStore CreateStore(Configuration configuration)
    {
        var logger = new LoggerConfiguration().CreateLogger();
        return new AdaptiveHelpProgressStore(new TestConfigurationService(configuration), logger);
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
        public TestConfigurationService(Configuration configuration)
        {
            Configuration = new Lazy<Configuration>(() => configuration);
        }

        public IObservable<Configuration> ConfigurationSaved => Observable.Never<Configuration>();

        public Lazy<Configuration> Configuration { get; }

        public Exception? SaveException { get; init; }

        public void SaveConfiguration()
        {
            if (SaveException != null)
            {
                throw SaveException;
            }
        }
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
