using System;
using System.Collections.Generic;
using System.Linq;

using ImageCare.Core.Domain.Configuration;

namespace ImageCare.UI.Avalonia.Services;

public sealed record AdaptiveHelpContext(
    bool IsFirstRun,
    bool HasSourceFolder,
    bool HasTargetFolder,
    int SelectedMediaCount,
    int RecentErrorCount,
    bool HasRestorableFolders,
    bool HasNewFeatures,
    bool IsReturningUser);

public sealed record AdaptiveHelpDecision(string HintId, int Score, string ReasonKey);

public sealed class AdaptiveHelpPolicy
{
    public const string WelcomeHintId = "welcome";
    public const string ReturningHintId = "returning";
    public const string RestoreFoldersHintId = "restore-folders";
    public const string DifficultyHintId = "difficulty";
    public const string SelectSourceHintId = "select-source";
    public const string SelectTargetHintId = "select-target";
    public const string SelectMediaHintId = "select-media";
    public const string TransferHintId = "transfer-media";
    public const string SearchHintId = "search-media";
    public const string SafeDeletionHintId = "safe-deletion";
    public const string NewFeaturesHintId = "new-features";
    public const string SettingsHintId = "help-settings";
    public const string ReadyHintId = "ready";

    public static readonly TimeSpan AutomaticCooldown = TimeSpan.FromDays(1);
    public static readonly TimeSpan DefaultReturningThreshold = TimeSpan.FromDays(30);
    public static readonly TimeSpan MinimumReturningThreshold = TimeSpan.FromDays(14);
    public static readonly TimeSpan MaximumReturningThreshold = TimeSpan.FromDays(60);

    public TimeSpan GetReturningThreshold(IReadOnlyList<DateTimeOffset>? sessionHistory)
    {
        if (sessionHistory == null || sessionHistory.Count < 3)
        {
            return DefaultReturningThreshold;
        }

        var ordered = sessionHistory.OrderBy(timestamp => timestamp).ToArray();
        var intervals = new List<TimeSpan>();
        for (var index = 1; index < ordered.Length; index++)
        {
            var interval = ordered[index] - ordered[index - 1];
            if (interval > TimeSpan.Zero && interval <= TimeSpan.FromDays(180))
            {
                intervals.Add(interval);
            }
        }

        if (intervals.Count < 2)
        {
            return DefaultReturningThreshold;
        }

        intervals.Sort();
        var median = intervals.Count % 2 == 1
            ? intervals[intervals.Count / 2]
            : TimeSpan.FromTicks(
                (intervals[(intervals.Count / 2) - 1].Ticks + intervals[intervals.Count / 2].Ticks) / 2);
        var adaptive = TimeSpan.FromTicks(median.Ticks * 3);
        return adaptive < MinimumReturningThreshold
            ? MinimumReturningThreshold
            : adaptive > MaximumReturningThreshold
                ? MaximumReturningThreshold
                : adaptive;
    }

    public bool IsReturningUser(
        DateTimeOffset? previousSessionUtc,
        IReadOnlyList<DateTimeOffset>? sessionHistory,
        DateTimeOffset now)
    {
        if (previousSessionUtc == null || previousSessionUtc > now)
        {
            return false;
        }

        return now - previousSessionUtc >= GetReturningThreshold(sessionHistory);
    }

    public bool CanShowAutomatically(Configuration configuration, DateTimeOffset now)
    {
        if (!configuration.AdaptiveHelpEnabled)
        {
            return false;
        }

        var lastShown = configuration.AdaptiveHelpLastShownUtc;
        return lastShown == null || lastShown > now || now - lastShown >= AutomaticCooldown;
    }

    public IReadOnlyList<AdaptiveHelpDecision> Rank(
        AdaptiveHelpContext context,
        Configuration configuration,
        bool automatic)
    {
        var candidates = new List<AdaptiveHelpDecision>();

        if (context.IsFirstRun && configuration.AdaptiveHelpUserGoal == AdaptiveHelpUserGoal.Unknown)
        {
            Add(candidates, WelcomeHintId, 1200, "Help_ReasonFirstRun", configuration);
        }

        if (context.IsReturningUser && configuration.AdaptiveHelpReturningUserEnabled)
        {
            Add(
                candidates,
                context.HasRestorableFolders ? RestoreFoldersHintId : ReturningHintId,
                1100,
                "Help_ReasonReturning",
                configuration);
        }

        if (context.RecentErrorCount >= 2 && configuration.AdaptiveHelpDifficultyAssistanceEnabled)
        {
            Add(candidates, DifficultyHintId, 1050 + context.RecentErrorCount, "Help_ReasonRepeatedErrors", configuration);
        }

        if (context.HasNewFeatures && configuration.AdaptiveHelpNewFeaturesEnabled)
        {
            Add(candidates, NewFeaturesHintId, 950, "Help_ReasonNewVersion", configuration);
        }

        if (!context.HasSourceFolder)
        {
            Add(candidates, SelectSourceHintId, 900, "Help_ReasonNoSource", configuration);
        }

        if (!context.HasTargetFolder)
        {
            Add(candidates, SelectTargetHintId, 850, "Help_ReasonNoTarget", configuration);
        }

        if (context.HasSourceFolder && context.SelectedMediaCount == 0)
        {
            Add(
                candidates,
                SelectMediaHintId,
                context.HasTargetFolder ? 930 : 800,
                "Help_ReasonNoSelection",
                configuration);
        }
        else if (context.SelectedMediaCount > 0 && context.HasTargetFolder)
        {
            Add(candidates, TransferHintId, 1000, "Help_ReasonSelectionReady", configuration);
        }

        switch (configuration.AdaptiveHelpUserGoal)
        {
            case AdaptiveHelpUserGoal.SearchMedia:
                Add(candidates, SearchHintId, 1050, "Help_ReasonSelectedGoal", configuration);
                break;
            case AdaptiveHelpUserGoal.CleanUpMedia:
                Add(candidates, SafeDeletionHintId, 780, "Help_ReasonSelectedGoal", configuration);
                break;
            case AdaptiveHelpUserGoal.CopyMedia:
            case AdaptiveHelpUserGoal.OrganizeMedia:
                if (context.SelectedMediaCount > 0)
                {
                    Add(candidates, TransferHintId, 1075, "Help_ReasonSelectedGoal", configuration);
                }
                break;
        }

        Add(candidates, SearchHintId, 450, "Help_ReasonOptional", configuration);
        Add(candidates, SafeDeletionHintId, 400, "Help_ReasonOptional", configuration);
        Add(candidates, SettingsHintId, 200, "Help_ReasonManual", configuration);
        Add(candidates, ReadyHintId, 100, "Help_ReasonManual", configuration);

        var limit = automatic
            ? 1
            : configuration.AdaptiveHelpDetailLevel switch
            {
                AdaptiveHelpDetailLevel.Minimal => 3,
                AdaptiveHelpDetailLevel.Detailed => 5,
                _ => 4
            };

        return candidates
            .GroupBy(candidate => candidate.HintId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.HintId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    private static void Add(
        ICollection<AdaptiveHelpDecision> candidates,
        string hintId,
        int score,
        string reasonKey,
        Configuration configuration)
    {
        if ((configuration.AdaptiveHelpDismissedHintIds ?? []).Contains(hintId, StringComparer.Ordinal))
        {
            return;
        }

        var progress = (configuration.AdaptiveHelpProgress ?? [])
            .FirstOrDefault(item => string.Equals(item.HintId, hintId, StringComparison.Ordinal));
        if (progress?.MasteryLevel == AdaptiveHelpMasteryLevel.Mastered && IsBasicWorkflowHint(hintId))
        {
            return;
        }

        var masteryPenalty = progress?.MasteryLevel switch
        {
            AdaptiveHelpMasteryLevel.Completed => 250,
            AdaptiveHelpMasteryLevel.Tried => 75,
            _ => 0
        };
        var fatiguePenalty = Math.Min(progress?.ExposureCount ?? 0, 5) * 20;
        candidates.Add(new AdaptiveHelpDecision(hintId, score - masteryPenalty - fatiguePenalty, reasonKey));
    }

    private static bool IsBasicWorkflowHint(string hintId) =>
        hintId is SelectSourceHintId or SelectTargetHintId or SelectMediaHintId or TransferHintId;
}
