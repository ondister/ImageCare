using System;
using System.Collections.Generic;
using System.IO;

using ImageCare.Core.Domain.Configuration;
using ImageCare.UI.Avalonia.Localization;

namespace ImageCare.UI.Avalonia.Services;

internal sealed class AdaptiveHelpCatalog
{
    private readonly IReadOnlyDictionary<string, AdaptiveHelpHint> _hints =
        new Dictionary<string, AdaptiveHelpHint>(StringComparer.Ordinal)
        {
            [AdaptiveHelpPolicy.WelcomeHintId] = new(
                AdaptiveHelpPolicy.WelcomeHintId,
                "Help_WelcomeTitle",
                "Help_WelcomeMessage",
                AdaptiveHelpActionKind.FocusSource,
                "Help_ActionChooseSource"),
            [AdaptiveHelpPolicy.ReturningHintId] = new(
                AdaptiveHelpPolicy.ReturningHintId,
                "Help_ReturningTitle",
                "Help_ReturningMessage",
                AdaptiveHelpActionKind.FocusSource,
                "Help_ActionContinue"),
            [AdaptiveHelpPolicy.RestoreFoldersHintId] = new(
                AdaptiveHelpPolicy.RestoreFoldersHintId,
                "Help_RestoreFoldersTitle",
                "Help_RestoreFoldersMessage",
                AdaptiveHelpActionKind.RestoreFolders,
                "Help_ActionRestoreFolders"),
            [AdaptiveHelpPolicy.DifficultyHintId] = new(
                AdaptiveHelpPolicy.DifficultyHintId,
                "Help_DifficultyTitle",
                "Help_DifficultyMessage",
                AdaptiveHelpActionKind.OpenSettings,
                "Help_ActionReviewSettings"),
            [AdaptiveHelpPolicy.SelectSourceHintId] = new(
                AdaptiveHelpPolicy.SelectSourceHintId,
                "Help_SelectSourceTitle",
                "Help_SelectSourceMessage",
                AdaptiveHelpActionKind.FocusSource,
                "Help_ActionChooseSource"),
            [AdaptiveHelpPolicy.SelectTargetHintId] = new(
                AdaptiveHelpPolicy.SelectTargetHintId,
                "Help_SelectTargetTitle",
                "Help_SelectTargetMessage",
                AdaptiveHelpActionKind.FocusTarget,
                "Help_ActionChooseTarget"),
            [AdaptiveHelpPolicy.SelectMediaHintId] = new(
                AdaptiveHelpPolicy.SelectMediaHintId,
                "Help_SelectMediaTitle",
                "Help_SelectMediaMessage",
                AdaptiveHelpActionKind.FocusPreviews,
                "Help_ActionGoToMedia"),
            [AdaptiveHelpPolicy.TransferHintId] = new(
                AdaptiveHelpPolicy.TransferHintId,
                "Help_TransferTitle",
                "Help_TransferMessage",
                AdaptiveHelpActionKind.FocusPreviews,
                "Help_ActionGoToSelection"),
            [AdaptiveHelpPolicy.SearchHintId] = new(
                AdaptiveHelpPolicy.SearchHintId,
                "Help_SearchTitle",
                "Help_SearchMessage",
                AdaptiveHelpActionKind.OpenSearch,
                "Help_ActionOpenSearch"),
            [AdaptiveHelpPolicy.SafeDeletionHintId] = new(
                AdaptiveHelpPolicy.SafeDeletionHintId,
                "Help_SafeDeletionTitle",
                "Help_SafeDeletionMessage",
                AdaptiveHelpActionKind.OpenSettings,
                "Help_ActionOpenSettings"),
            [AdaptiveHelpPolicy.NewFeaturesHintId] = new(
                AdaptiveHelpPolicy.NewFeaturesHintId,
                "Help_NewFeaturesTitle",
                "Help_NewFeaturesMessage",
                AdaptiveHelpActionKind.OpenSearch,
                "Help_ActionTrySearch"),
            [AdaptiveHelpPolicy.SettingsHintId] = new(
                AdaptiveHelpPolicy.SettingsHintId,
                "Help_SettingsTitle",
                "Help_SettingsMessage",
                AdaptiveHelpActionKind.OpenSettings,
                "Help_ActionOpenSettings"),
            [AdaptiveHelpPolicy.OverviewSelectHintId] = new(
                AdaptiveHelpPolicy.OverviewSelectHintId,
                "Help_OverviewSelectTitle",
                "Help_OverviewSelectMessage",
                AdaptiveHelpActionKind.None,
                string.Empty),
            [AdaptiveHelpPolicy.OverviewAnnotationsHintId] = new(
                AdaptiveHelpPolicy.OverviewAnnotationsHintId,
                "Help_OverviewAnnotationsTitle",
                "Help_OverviewAnnotationsMessage",
                AdaptiveHelpActionKind.None,
                string.Empty),
            [AdaptiveHelpPolicy.OverviewBatchHintId] = new(
                AdaptiveHelpPolicy.OverviewBatchHintId,
                "Help_OverviewBatchTitle",
                "Help_OverviewBatchMessage",
                AdaptiveHelpActionKind.None,
                string.Empty),
            [AdaptiveHelpPolicy.OverviewBatchActiveHintId] = new(
                AdaptiveHelpPolicy.OverviewBatchActiveHintId,
                "Help_OverviewBatchActiveTitle",
                "Help_OverviewBatchActiveMessage",
                AdaptiveHelpActionKind.None,
                string.Empty),
            [AdaptiveHelpPolicy.ReadyHintId] = new(
                AdaptiveHelpPolicy.ReadyHintId,
                "Help_ReadyTitle",
                "Help_ReadyMessage",
                AdaptiveHelpActionKind.None,
                string.Empty)
        };

    private readonly LocalizationManager _localization;

    public AdaptiveHelpCatalog(LocalizationManager localization)
    {
        _localization = localization;
    }

    public IEnumerable<AdaptiveHelpHint> Hints => _hints.Values;

    public bool TryGetHint(string hintId, out AdaptiveHelpHint hint) =>
        _hints.TryGetValue(hintId, out hint!);

    public string GetTitle(AdaptiveHelpHint hint) => _localization[hint.TitleKey];

    public string GetActionText(AdaptiveHelpHint hint) =>
        string.IsNullOrEmpty(hint.ActionKey) ? string.Empty : _localization[hint.ActionKey];

    public string GetMessage(AdaptiveHelpHint hint, Configuration configuration)
    {
        if (hint.Id is AdaptiveHelpPolicy.ReturningHintId or AdaptiveHelpPolicy.RestoreFoldersHintId)
        {
            return _localization.Get(hint.MessageKey, GetLastWorkspaceName(configuration));
        }

        if (hint.Id == AdaptiveHelpPolicy.TransferHintId)
        {
            var defaultOperation = configuration.CopyOnDragDropByDefault
                ? _localization["Action_Copy"]
                : _localization["Action_Move"];
            var modifier = configuration.DragDropModifier switch
            {
                DragDropModifierKey.Alt => _localization["Settings_ModifierAlt"],
                DragDropModifierKey.Shift => _localization["Settings_ModifierShift"],
                DragDropModifierKey.Meta => _localization["Settings_ModifierMeta"],
                _ => _localization["Settings_ModifierControl"]
            };
            return _localization.Get(hint.MessageKey, defaultOperation, modifier);
        }

        return _localization[hint.MessageKey];
    }

    public string GetReason(
        AdaptiveHelpDecision decision,
        AdaptiveHelpContext context,
        TimeSpan returningThreshold)
    {
        return decision.ReasonKey switch
        {
            "Help_ReasonReturning" => _localization.Get(
                decision.ReasonKey,
                Math.Round(returningThreshold.TotalDays)),
            "Help_ReasonRepeatedErrors" => _localization.Get(decision.ReasonKey, context.RecentErrorCount),
            "Help_ReasonSelectionReady" => _localization.Get(decision.ReasonKey, context.SelectedMediaCount),
            "Help_ReasonOverviewSelection" => _localization.Get(
                decision.ReasonKey,
                context.FolderOverviewSelectionCount),
            "Help_ReasonOverviewMultipleSelection" => _localization.Get(
                decision.ReasonKey,
                context.FolderOverviewSelectionCount),
            _ => _localization[decision.ReasonKey]
        };
    }

    private string GetLastWorkspaceName(Configuration configuration)
    {
        var path = !string.IsNullOrWhiteSpace(configuration.LastSourceDirectoryPath)
            ? configuration.LastSourceDirectoryPath
            : configuration.LastTargetDirectoryPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return _localization["Help_LastWorkspaceUnknown"];
        }

        var normalizedPath = Path.TrimEndingDirectorySeparator(path);
        var name = Path.GetFileName(normalizedPath);
        return string.IsNullOrWhiteSpace(name) ? normalizedPath : name;
    }
}

internal sealed record AdaptiveHelpHint(
    string Id,
    string TitleKey,
    string MessageKey,
    AdaptiveHelpActionKind Action,
    string ActionKey);

internal enum AdaptiveHelpActionKind
{
    None,
    FocusSource,
    FocusTarget,
    FocusPreviews,
    RestoreFolders,
    OpenSearch,
    OpenSettings
}
