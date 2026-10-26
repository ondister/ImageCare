using ImageCare.Core.Domain.Configuration;
using ImageCare.UI.Avalonia.Services;

namespace ImageCare.UI.Avalonia.Tests;

[TestFixture]
public sealed class AdaptiveHelpPolicyTests
{
    private readonly AdaptiveHelpPolicy _policy = new();
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void GetReturningThreshold_UsesFallback_WhenHistoryIsShort()
    {
        var threshold = _policy.GetReturningThreshold([Now.AddDays(-2), Now]);

        Assert.That(threshold, Is.EqualTo(TimeSpan.FromDays(30)));
    }

    [Test]
    public void GetReturningThreshold_UsesThreeMedianIntervals_WithBounds()
    {
        var regularHistory = new[]
        {
            Now.AddDays(-15),
            Now.AddDays(-10),
            Now.AddDays(-5),
            Now
        };
        var frequentHistory = new[]
        {
            Now.AddDays(-3),
            Now.AddDays(-2),
            Now.AddDays(-1),
            Now
        };
        var rareHistory = new[]
        {
            Now.AddDays(-120),
            Now.AddDays(-80),
            Now.AddDays(-40),
            Now
        };

        Assert.Multiple(() =>
        {
            Assert.That(_policy.GetReturningThreshold(regularHistory), Is.EqualTo(TimeSpan.FromDays(15)));
            Assert.That(_policy.GetReturningThreshold(frequentHistory), Is.EqualTo(TimeSpan.FromDays(14)));
            Assert.That(_policy.GetReturningThreshold(rareHistory), Is.EqualTo(TimeSpan.FromDays(60)));
        });
    }

    [Test]
    public void IsReturningUser_UsesInclusiveAdaptiveBoundary()
    {
        var history = new[]
        {
            Now.AddDays(-15),
            Now.AddDays(-10),
            Now.AddDays(-5),
            Now
        };

        Assert.Multiple(() =>
        {
            Assert.That(_policy.IsReturningUser(Now.AddDays(-15).AddTicks(1), history, Now), Is.False);
            Assert.That(_policy.IsReturningUser(Now.AddDays(-15), history, Now), Is.True);
            Assert.That(_policy.IsReturningUser(Now.AddDays(-15).AddTicks(-1), history, Now), Is.True);
        });
    }

    [Test]
    public void CanShowAutomatically_RespectsEnabledStateAndCooldown()
    {
        var configuration = new Configuration { AdaptiveHelpEnabled = true };

        Assert.That(_policy.CanShowAutomatically(configuration, Now), Is.True);

        configuration.AdaptiveHelpLastShownUtc = Now.AddHours(-23);
        Assert.That(_policy.CanShowAutomatically(configuration, Now), Is.False);

        configuration.AdaptiveHelpLastShownUtc = Now.AddDays(-1);
        Assert.That(_policy.CanShowAutomatically(configuration, Now), Is.True);

        configuration.AdaptiveHelpEnabled = false;
        Assert.That(_policy.CanShowAutomatically(configuration, Now), Is.False);
    }

    [TestCase(AdaptiveHelpDetailLevel.Minimal, 3)]
    [TestCase(AdaptiveHelpDetailLevel.Standard, 4)]
    [TestCase(AdaptiveHelpDetailLevel.Detailed, 5)]
    public void Rank_LimitsManualRecommendations_ByDetailLevel(
        AdaptiveHelpDetailLevel detailLevel,
        int expectedCount)
    {
        var configuration = new Configuration { AdaptiveHelpDetailLevel = detailLevel };
        var context = new AdaptiveHelpContext(false, false, false, 0, 0, false, false, false);

        var decisions = _policy.Rank(context, configuration, automatic: false);

        Assert.That(decisions, Has.Count.EqualTo(expectedCount));
    }

    [Test]
    public void Rank_ReturnsOnlyTopRecommendation_InAutomaticMode()
    {
        var configuration = new Configuration();
        var context = new AdaptiveHelpContext(false, false, false, 0, 0, false, false, false);

        var decisions = _policy.Rank(context, configuration, automatic: true);

        Assert.That(decisions, Has.Count.EqualTo(1));
        Assert.That(decisions[0].HintId, Is.EqualTo(AdaptiveHelpPolicy.SelectSourceHintId));
    }

    [Test]
    public void Rank_PrioritizesRepeatedErrors()
    {
        var configuration = new Configuration();
        var context = new AdaptiveHelpContext(false, true, true, 2, 3, false, false, false);

        var decisions = _policy.Rank(context, configuration, automatic: false);

        Assert.That(decisions[0].HintId, Is.EqualTo(AdaptiveHelpPolicy.DifficultyHintId));
        Assert.That(decisions[0].ReasonKey, Is.EqualTo("Help_ReasonRepeatedErrors"));
    }

    [Test]
    public void Rank_PrioritizesSelectedGoal()
    {
        var configuration = new Configuration
        {
            AdaptiveHelpUserGoal = AdaptiveHelpUserGoal.SearchMedia
        };
        var context = new AdaptiveHelpContext(false, true, true, 0, 0, false, false, false);

        var decisions = _policy.Rank(context, configuration, automatic: false);

        Assert.That(decisions[0].HintId, Is.EqualTo(AdaptiveHelpPolicy.SearchHintId));
        Assert.That(decisions[0].ReasonKey, Is.EqualTo("Help_ReasonSelectedGoal"));
    }

    [Test]
    public void Rank_ExcludesDismissedAndMasteredBasicHints()
    {
        var configuration = new Configuration
        {
            AdaptiveHelpDismissedHintIds = [AdaptiveHelpPolicy.SelectSourceHintId],
            AdaptiveHelpProgress =
            [
                new AdaptiveHelpProgressEntry
                {
                    HintId = AdaptiveHelpPolicy.SelectTargetHintId,
                    MasteryLevel = AdaptiveHelpMasteryLevel.Mastered
                }
            ]
        };
        var context = new AdaptiveHelpContext(false, false, false, 0, 0, false, false, false);

        var decisions = _policy.Rank(context, configuration, automatic: false);
        var ids = decisions.Select(decision => decision.HintId);

        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Not.Contain(AdaptiveHelpPolicy.SelectSourceHintId));
            Assert.That(ids, Does.Not.Contain(AdaptiveHelpPolicy.SelectTargetHintId));
        });
    }

    [Test]
    public void Rank_PrioritizesDragAndDrop_WhenSelectionIsReady()
    {
        var configuration = new Configuration();
        var context = new AdaptiveHelpContext(false, true, true, 3, 0, false, false, false);

        var decisions = _policy.Rank(context, configuration, automatic: false);

        Assert.That(decisions[0].HintId, Is.EqualTo(AdaptiveHelpPolicy.TransferHintId));
        Assert.That(decisions[0].ReasonKey, Is.EqualTo("Help_ReasonSelectionReady"));
    }

    [Test]
    public void Rank_ReturningWorkspace_OutranksContextualHints()
    {
        var configuration = new Configuration();
        var context = new AdaptiveHelpContext(false, false, false, 0, 0, true, false, true);

        var decisions = _policy.Rank(context, configuration, automatic: false);

        Assert.That(decisions[0].HintId, Is.EqualTo(AdaptiveHelpPolicy.RestoreFoldersHintId));
    }
}
