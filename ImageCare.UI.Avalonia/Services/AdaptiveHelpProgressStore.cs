using System;
using System.Collections.Generic;
using System.Linq;

using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Services.ConfigurationService;

using Serilog;

namespace ImageCare.UI.Avalonia.Services;

internal sealed class AdaptiveHelpProgressStore
{
    private const int MaximumSessionHistoryCount = 10;

    private readonly IConfigurationService _configurationService;
    private readonly ILogger _logger;

    public AdaptiveHelpProgressStore(IConfigurationService configurationService, ILogger logger)
    {
        _configurationService = configurationService;
        _logger = logger;
    }

    public void NormalizeState(Configuration configuration)
    {
        configuration.AdaptiveHelpCompletedHintIds ??= new List<string>();
        configuration.AdaptiveHelpDismissedHintIds ??= new List<string>();
        configuration.AdaptiveHelpSessionHistoryUtc ??= new List<DateTimeOffset>();
        configuration.AdaptiveHelpProgress ??= new List<AdaptiveHelpProgressEntry>();

        foreach (var completedHintId in configuration.AdaptiveHelpCompletedHintIds)
        {
            var progress = GetOrCreateProgress(configuration, completedHintId);
            if (progress.MasteryLevel < AdaptiveHelpMasteryLevel.Completed)
            {
                progress.MasteryLevel = AdaptiveHelpMasteryLevel.Completed;
            }
        }
    }

    public AdaptiveHelpSessionSnapshot RecordSession(
        Configuration configuration,
        DateTimeOffset now,
        int experienceVersion,
        string applicationVersion)
    {
        NormalizeState(configuration);
        var snapshot = new AdaptiveHelpSessionSnapshot(
            configuration.AdaptiveHelpLastSessionUtc,
            configuration.AdaptiveHelpSessionHistoryUtc.ToArray(),
            configuration.AdaptiveHelpLastSeenVersion);

        configuration.AdaptiveHelpLastSessionUtc = now;
        configuration.AdaptiveHelpExperienceVersion = experienceVersion;
        configuration.AdaptiveHelpLastSeenVersion = applicationVersion;
        configuration.AdaptiveHelpSessionHistoryUtc.Add(now);
        while (configuration.AdaptiveHelpSessionHistoryUtc.Count > MaximumSessionHistoryCount)
        {
            configuration.AdaptiveHelpSessionHistoryUtc.RemoveAt(0);
        }

        return snapshot;
    }

    public AdaptiveHelpProgressEntry GetOrCreateProgress(Configuration configuration, string hintId)
    {
        var progressEntries = configuration.AdaptiveHelpProgress ??= new List<AdaptiveHelpProgressEntry>();
        var progress = progressEntries.FirstOrDefault(item =>
            string.Equals(item.HintId, hintId, StringComparison.Ordinal));
        if (progress != null)
        {
            return progress;
        }

        progress = new AdaptiveHelpProgressEntry { HintId = hintId };
        progressEntries.Add(progress);
        return progress;
    }

    public void RecordExposure(Configuration configuration, string hintId, DateTimeOffset now)
    {
        var progress = GetOrCreateProgress(configuration, hintId);
        progress.ExposureCount++;
        progress.LastShownUtc = now;
        if (progress.MasteryLevel == AdaptiveHelpMasteryLevel.NotUsed)
        {
            progress.MasteryLevel = AdaptiveHelpMasteryLevel.Tried;
        }
    }

    public void RecordSuccessfulAction(Configuration configuration, string hintId, DateTimeOffset now)
    {
        var progress = GetOrCreateProgress(configuration, hintId);
        progress.SuccessfulActionCount++;
        progress.LastCompletedUtc = now;
        progress.MasteryLevel = progress.SuccessfulActionCount >= 3
            ? AdaptiveHelpMasteryLevel.Mastered
            : AdaptiveHelpMasteryLevel.Completed;

        var completed = configuration.AdaptiveHelpCompletedHintIds ??= new List<string>();
        if (!completed.Contains(hintId, StringComparer.Ordinal))
        {
            completed.Add(hintId);
        }
    }

    public void RecordDismissal(Configuration configuration, string hintId, bool dismissImmediately)
    {
        var progress = GetOrCreateProgress(configuration, hintId);
        progress.DismissCount++;
        if (!dismissImmediately && progress.DismissCount < 2)
        {
            return;
        }

        var dismissed = configuration.AdaptiveHelpDismissedHintIds ??= new List<string>();
        if (!dismissed.Contains(hintId, StringComparer.Ordinal))
        {
            dismissed.Add(hintId);
        }
    }

    public void Save()
    {
        try
        {
            _configurationService.SaveConfiguration();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save adaptive help state");
        }
    }
}

internal sealed record AdaptiveHelpSessionSnapshot(
    DateTimeOffset? PreviousSessionUtc,
    IReadOnlyList<DateTimeOffset> SessionHistoryUtc,
    string LastSeenVersion);
