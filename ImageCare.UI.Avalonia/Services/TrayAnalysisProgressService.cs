using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using ImageCare.Core.Services.FolderStatisticsService;
using ImageCare.UI.Avalonia.Localization;

namespace ImageCare.UI.Avalonia.Services;

internal sealed class TrayAnalysisProgressService
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, ScanProgress> _activeScans = new();
    private readonly LocalizationManager _localization;

    public TrayAnalysisProgressService(LocalizationManager localization)
    {
        _localization = localization;
        _localization.CultureChanged += (_, _) => UpdateTrayTooltip();
    }

    public void Update(Guid scanId, ScanProgress progress)
    {
        lock (_sync)
        {
            if (progress.Status == ScanStatus.InitialScanStarted)
            {
                _activeScans[scanId] = progress;
            }
            else if (progress.Status is ScanStatus.InitialScanCompleted or ScanStatus.ErrorOccurred or ScanStatus.Idle)
            {
                _activeScans.Remove(scanId);
            }
        }

        UpdateTrayTooltip();
    }

    private void UpdateTrayTooltip()
    {
        ScanProgress[] activeScans;
        lock (_sync)
        {
            activeScans = _activeScans.Values.ToArray();
        }

        Dispatcher.UIThread.Post(() =>
        {
            var trayIcon = Application.Current is { } application
                ? TrayIcon.GetIcons(application)?.FirstOrDefault()
                : null;
            if (trayIcon == null)
            {
                return;
            }

            if (activeScans.Length == 0)
            {
                trayIcon.ToolTipText = "ImageCare";
                return;
            }

            var processed = activeScans.Sum(scan => scan.ProcessedFiles);
            var candidates = activeScans.Sum(scan => scan.CandidateFiles);
            var resourceKey = activeScans.Length == 1
                ? "Tray_AnalyzingFolder"
                : "Tray_AnalyzingFolders";
            var progressText = candidates > 0
                ? $"{processed}/{candidates} ({Math.Clamp((int)((long)processed * 100 / candidates), 0, 100)}%)"
                : _localization["Tray_PreparingAnalysis"];
            trayIcon.ToolTipText = $"ImageCare — {_localization[resourceKey]}: {progressText}";
        });
    }
}
