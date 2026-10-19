using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using Avalonia.Input;
using Avalonia.Threading;

using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Services.ConfigurationService;

namespace ImageCare.UI.Avalonia.Services;

public enum FileTransferOperation
{
    Copy,
    Move
}

public readonly record struct FileTransferOperationStatus(
    FileTransferOperation Operation,
    bool IsModifierActive);

public sealed class FileTransferOperationState : IDisposable
{
    private readonly IConfigurationService _configurationService;
    private readonly BehaviorSubject<FileTransferOperationStatus> _status;
    private readonly IDisposable _configurationSubscription;
    private readonly DispatcherTimer _modifierPollTimer;
    private KeyModifiers _windowModifiers;
    private KeyModifiers _dragModifiers;
    private bool _dragActive;

    public FileTransferOperationState(IConfigurationService configurationService)
    {
        _configurationService = configurationService;
        _status = new BehaviorSubject<FileTransferOperationStatus>(CalculateStatus());
        _configurationSubscription = configurationService.ConfigurationSaved
            .Subscribe(_ => PublishCurrentStatus());
        _modifierPollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _modifierPollTimer.Tick += OnModifierPollTimerTick;
    }

    public FileTransferOperationStatus CurrentStatus => _status.Value;

    public IObservable<FileTransferOperationStatus> StatusChanged =>
        _status.DistinctUntilChanged();

    public void HandleKeyDown(Key key, KeyModifiers eventModifiers)
    {
        _windowModifiers = ApplyChangedKey(eventModifiers, key, isDown: true);
        if (_dragActive)
        {
            _dragModifiers = _windowModifiers;
        }

        PublishCurrentStatus();
    }

    public void HandleKeyUp(Key key, KeyModifiers eventModifiers)
    {
        _windowModifiers = ApplyChangedKey(eventModifiers, key, isDown: false);
        if (_dragActive)
        {
            _dragModifiers = _windowModifiers;
        }

        PublishCurrentStatus();
    }

    public void BeginDrag(KeyModifiers eventModifiers)
    {
        _dragActive = true;
        var knownModifiers = eventModifiers | _windowModifiers;
        _dragModifiers = PlatformModifierKeys.GetCurrent(knownModifiers) | knownModifiers;
        if (OperatingSystem.IsMacOS())
        {
            _modifierPollTimer.Start();
        }

        PublishCurrentStatus();
    }

    public void UpdateFromDrag(KeyModifiers eventModifiers)
    {
        _dragActive = true;
        _dragModifiers = PlatformModifierKeys.GetCurrent(eventModifiers);
        PublishCurrentStatus();
    }

    public void RefreshForDrop(KeyModifiers eventModifiers)
    {
        _dragModifiers = PlatformModifierKeys.GetCurrent(eventModifiers);
        PublishCurrentStatus();
    }

    public void EndDrag()
    {
        _modifierPollTimer.Stop();
        _dragActive = false;
        _dragModifiers = KeyModifiers.None;
        PublishCurrentStatus();
    }

    public void ClearWindowModifiers()
    {
        _windowModifiers = KeyModifiers.None;
        if (!_dragActive)
        {
            PublishCurrentStatus();
        }
    }

    public void ResetModifiers()
    {
        _modifierPollTimer.Stop();
        _windowModifiers = KeyModifiers.None;
        _dragModifiers = KeyModifiers.None;
        _dragActive = false;
        PublishCurrentStatus();
    }

    public void Dispose()
    {
        _modifierPollTimer.Stop();
        _modifierPollTimer.Tick -= OnModifierPollTimerTick;
        _configurationSubscription.Dispose();
        _status.Dispose();
    }

    private FileTransferOperationStatus CalculateStatus()
    {
        var configuration = _configurationService.Configuration.Value;
        var configuredModifier = ToKeyModifier(configuration.DragDropModifier);
        var availableModifiers = _dragActive
            ? _dragModifiers | _windowModifiers
            : _windowModifiers;
        var modifierActive = availableModifiers.HasFlag(configuredModifier);
        var shouldCopy = configuration.CopyOnDragDropByDefault ^ modifierActive;
        return new FileTransferOperationStatus(
            shouldCopy ? FileTransferOperation.Copy : FileTransferOperation.Move,
            modifierActive);
    }

    private void PublishCurrentStatus()
    {
        var status = CalculateStatus();
        if (status != _status.Value)
        {
            _status.OnNext(status);
        }
    }

    private void OnModifierPollTimerTick(object? sender, EventArgs args)
    {
        if (!_dragActive || !PlatformModifierKeys.TryGetCurrent(out var modifiers))
        {
            return;
        }

        _dragModifiers = modifiers;
        PublishCurrentStatus();
    }

    private static KeyModifiers ApplyChangedKey(
        KeyModifiers eventModifiers,
        Key key,
        bool isDown)
    {
        var changedModifier = key switch
        {
            Key.LeftCtrl or Key.RightCtrl => KeyModifiers.Control,
            Key.LeftAlt or Key.RightAlt => KeyModifiers.Alt,
            Key.LeftShift or Key.RightShift => KeyModifiers.Shift,
            Key.LWin or Key.RWin => KeyModifiers.Meta,
            _ => KeyModifiers.None
        };

        return isDown
            ? eventModifiers | changedModifier
            : eventModifiers & ~changedModifier;
    }

    private static KeyModifiers ToKeyModifier(DragDropModifierKey modifier) =>
        modifier switch
        {
            DragDropModifierKey.Alt => KeyModifiers.Alt,
            DragDropModifierKey.Shift => KeyModifiers.Shift,
            DragDropModifierKey.Meta => KeyModifiers.Meta,
            _ => KeyModifiers.Control
        };
}
