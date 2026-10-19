using System;
using System.IO;
using System.Runtime.InteropServices;

using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace ImageCare.UI.Avalonia.Platforms.MacOS;

/// <summary>
/// Hosts the native macOS AVPlayer inside the Avalonia visual tree.
/// </summary>
internal sealed class MacVideoPlayerView : NativeControlHost
{
    private static readonly TimeSpan _positionUpdateInterval = TimeSpan.FromMilliseconds(250);

    private readonly DispatcherTimer _positionTimer;

    private IntPtr _nativeView;
    private IntPtr _player;
    private IntPtr _playerLayer;
    private string _source = string.Empty;
    private bool _isPlaying;
    private bool _failureReported;
    private double _volume = 100;

    public MacVideoPlayerView()
    {
        _positionTimer = new DispatcherTimer
        {
            Interval = _positionUpdateInterval
        };
        _positionTimer.Tick += OnPositionTimerTick;
    }

    public event EventHandler? PlaybackStateChanged;

    public event EventHandler? PositionChanged;

    public event Action<Exception>? PlaybackFailed;

    public string Source
    {
        get => _source;
        set
        {
            var normalizedSource = value ?? string.Empty;
            if (string.Equals(_source, normalizedSource, StringComparison.Ordinal))
            {
                return;
            }

            _source = normalizedSource;
            OpenSource();
        }
    }

    public bool HasMediaLoaded => _player != IntPtr.Zero;

    public bool IsPlaying => _isPlaying;

    public TimeSpan Position => GetPlayerTime(MacNative.CurrentTimeSelector);

    public TimeSpan Duration
    {
        get
        {
            if (_player == IntPtr.Zero)
            {
                return TimeSpan.Zero;
            }

            var item = MacNative.SendIntPtr(_player, MacNative.CurrentItemSelector);
            return item == IntPtr.Zero
                ? TimeSpan.Zero
                : GetTime(MacNative.SendTime(item, MacNative.DurationSelector));
        }
    }

    public bool IsSeekable => Duration > TimeSpan.Zero;

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            if (_player != IntPtr.Zero)
            {
                MacNative.SendFloat(_player, MacNative.SetVolumeSelector, (float)(_volume / 100));
            }
        }
    }

    public void Play()
    {
        if (_player == IntPtr.Zero)
        {
            OpenSource();
        }

        if (_player == IntPtr.Zero)
        {
            return;
        }

        MacNative.SendVoid(_player, MacNative.PlaySelector);
        SetPlaying(true);
    }

    public void Pause()
    {
        if (_player == IntPtr.Zero)
        {
            return;
        }

        MacNative.SendVoid(_player, MacNative.PauseSelector);
        SetPlaying(false);
    }

    public void Stop()
    {
        if (_player == IntPtr.Zero)
        {
            return;
        }

        MacNative.SendVoid(_player, MacNative.PauseSelector);
        Seek(TimeSpan.Zero);
        SetPlaying(false);
    }

    public void Seek(TimeSpan position)
    {
        if (_player == IntPtr.Zero)
        {
            return;
        }

        var duration = Duration;
        var clampedPosition = duration > TimeSpan.Zero
            ? TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, duration.TotalSeconds))
            : TimeSpan.FromSeconds(Math.Max(0, position.TotalSeconds));

        var time = MacNative.CMTimeMakeWithSeconds(clampedPosition.TotalSeconds, 600);
        MacNative.SendTimeArgument(_player, MacNative.SeekToTimeSelector, time);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return base.CreateNativeControlCore(parent);
        }

        MacNative.EnsureLoaded();

        var allocatedView = MacNative.SendIntPtr(MacNative.NsViewClass, MacNative.AllocSelector);
        _nativeView = MacNative.SendRect(allocatedView, MacNative.InitWithFrameSelector, default);
        if (_nativeView == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not create the native macOS video view.");
        }

        MacNative.SendBool(_nativeView, MacNative.SetWantsLayerSelector, true);
        _positionTimer.Start();
        OpenSource();

        return new PlatformHandle(_nativeView, "NSView");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _positionTimer.Stop();
        ReleasePlayer();

        if (_nativeView != IntPtr.Zero)
        {
            MacNative.SendVoid(_nativeView, MacNative.ReleaseSelector);
            _nativeView = IntPtr.Zero;
        }
    }

    private void OpenSource()
    {
        if (!OperatingSystem.IsMacOS() || _nativeView == IntPtr.Zero)
        {
            return;
        }

        ReleasePlayer();
        _failureReported = false;

        if (string.IsNullOrWhiteSpace(_source))
        {
            NotifyStateChanged();
            return;
        }

        var fullPath = Path.GetFullPath(_source);
        if (!File.Exists(fullPath))
        {
            var exception = new FileNotFoundException("The selected video file does not exist.", fullPath);
            _failureReported = true;
            PlaybackFailed?.Invoke(exception);
            NotifyStateChanged();
            return;
        }

        var pathString = MacNative.CreateString(fullPath);
        var url = MacNative.SendIntPtr(MacNative.NsUrlClass, MacNative.FileUrlWithPathSelector, pathString);
        if (url == IntPtr.Zero)
        {
            PlaybackFailed?.Invoke(new InvalidOperationException($"Could not create a file URL for '{fullPath}'."));
            NotifyStateChanged();
            return;
        }

        var allocatedPlayer = MacNative.SendIntPtr(MacNative.AvPlayerClass, MacNative.AllocSelector);
        _player = MacNative.SendIntPtr(allocatedPlayer, MacNative.InitWithUrlSelector, url);
        if (_player == IntPtr.Zero)
        {
            PlaybackFailed?.Invoke(new InvalidOperationException($"AVPlayer could not open '{fullPath}'."));
            NotifyStateChanged();
            return;
        }

        _playerLayer = MacNative.SendIntPtr(MacNative.AvPlayerLayerClass, MacNative.PlayerLayerWithPlayerSelector, _player);
        if (_playerLayer == IntPtr.Zero)
        {
            ReleasePlayer();
            PlaybackFailed?.Invoke(new InvalidOperationException("Could not create AVPlayerLayer."));
            NotifyStateChanged();
            return;
        }

        MacNative.SendIntPtr(_playerLayer, MacNative.RetainSelector);
        var resizeAspect = MacNative.CreateString("AVLayerVideoGravityResizeAspect");
        MacNative.SendIntPtr(_playerLayer, MacNative.SetVideoGravitySelector, resizeAspect);
        MacNative.SendIntPtr(_nativeView, MacNative.SetLayerSelector, _playerLayer);
        MacNative.SendFloat(_player, MacNative.SetVolumeSelector, (float)(_volume / 100));

        NotifyStateChanged();
    }

    private void ReleasePlayer()
    {
        if (_player != IntPtr.Zero)
        {
            MacNative.SendVoid(_player, MacNative.PauseSelector);
        }

        if (_nativeView != IntPtr.Zero && _playerLayer != IntPtr.Zero)
        {
            MacNative.SendIntPtr(_nativeView, MacNative.SetLayerSelector, IntPtr.Zero);
        }

        if (_playerLayer != IntPtr.Zero)
        {
            MacNative.SendVoid(_playerLayer, MacNative.ReleaseSelector);
            _playerLayer = IntPtr.Zero;
        }

        if (_player != IntPtr.Zero)
        {
            MacNative.SendVoid(_player, MacNative.ReleaseSelector);
            _player = IntPtr.Zero;
        }

        _isPlaying = false;
    }

    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        if (_player == IntPtr.Zero)
        {
            return;
        }

        var item = MacNative.SendIntPtr(_player, MacNative.CurrentItemSelector);
        if (item != IntPtr.Zero)
        {
            var status = MacNative.SendLong(item, MacNative.StatusSelector);
            if (status == MacNative.FailedStatus && !_failureReported)
            {
                _failureReported = true;
                SetPlaying(false);
                PlaybackFailed?.Invoke(new InvalidOperationException(GetPlayerError(item)));
            }
        }

        if (_isPlaying)
        {
            var duration = Duration;
            var position = Position;
            if (duration > TimeSpan.Zero && position >= duration - _positionUpdateInterval)
            {
                SetPlaying(false);
            }
        }

        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private string GetPlayerError(IntPtr item)
    {
        var error = MacNative.SendIntPtr(item, MacNative.ErrorSelector);
        if (error == IntPtr.Zero)
        {
            return "AVPlayer failed to play the selected video.";
        }

        var description = MacNative.SendIntPtr(error, MacNative.LocalizedDescriptionSelector);
        var utf8 = description == IntPtr.Zero
            ? IntPtr.Zero
            : MacNative.SendIntPtr(description, MacNative.Utf8StringSelector);

        return utf8 == IntPtr.Zero
            ? "AVPlayer failed to play the selected video."
            : Marshal.PtrToStringUTF8(utf8) ?? "AVPlayer failed to play the selected video.";
    }

    private TimeSpan GetPlayerTime(IntPtr selector)
    {
        return _player == IntPtr.Zero
            ? TimeSpan.Zero
            : GetTime(MacNative.SendTime(_player, selector));
    }

    private static TimeSpan GetTime(MacNative.CMTime time)
    {
        var seconds = MacNative.CMTimeGetSeconds(time);
        return double.IsFinite(seconds) && seconds >= 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;
    }

    private void SetPlaying(bool value)
    {
        if (_isPlaying == value)
        {
            return;
        }

        _isPlaying = value;
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static class MacNative
    {
        private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";
        private const string CoreMediaFramework = "/System/Library/Frameworks/CoreMedia.framework/CoreMedia";

        internal const long FailedStatus = 2;

        private static bool _loaded;

        internal static IntPtr NsViewClass { get; private set; }
        internal static IntPtr NsUrlClass { get; private set; }
        internal static IntPtr NsStringClass { get; private set; }
        internal static IntPtr AvPlayerClass { get; private set; }
        internal static IntPtr AvPlayerLayerClass { get; private set; }

        internal static IntPtr AllocSelector { get; private set; }
        internal static IntPtr ReleaseSelector { get; private set; }
        internal static IntPtr RetainSelector { get; private set; }
        internal static IntPtr InitWithFrameSelector { get; private set; }
        internal static IntPtr SetWantsLayerSelector { get; private set; }
        internal static IntPtr SetLayerSelector { get; private set; }
        internal static IntPtr FileUrlWithPathSelector { get; private set; }
        internal static IntPtr InitWithUrlSelector { get; private set; }
        internal static IntPtr PlayerLayerWithPlayerSelector { get; private set; }
        internal static IntPtr SetVideoGravitySelector { get; private set; }
        internal static IntPtr PlaySelector { get; private set; }
        internal static IntPtr PauseSelector { get; private set; }
        internal static IntPtr CurrentTimeSelector { get; private set; }
        internal static IntPtr CurrentItemSelector { get; private set; }
        internal static IntPtr DurationSelector { get; private set; }
        internal static IntPtr SeekToTimeSelector { get; private set; }
        internal static IntPtr SetVolumeSelector { get; private set; }
        internal static IntPtr StatusSelector { get; private set; }
        internal static IntPtr ErrorSelector { get; private set; }
        internal static IntPtr LocalizedDescriptionSelector { get; private set; }
        internal static IntPtr Utf8StringSelector { get; private set; }
        internal static IntPtr StringWithUtf8StringSelector { get; private set; }

        internal static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation");
            NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
            NativeLibrary.Load("/System/Library/Frameworks/QuartzCore.framework/QuartzCore");
            NativeLibrary.Load("/System/Library/Frameworks/AVFoundation.framework/AVFoundation");
            NativeLibrary.Load(CoreMediaFramework);

            NsViewClass = objc_getClass("NSView");
            NsUrlClass = objc_getClass("NSURL");
            NsStringClass = objc_getClass("NSString");
            AvPlayerClass = objc_getClass("AVPlayer");
            AvPlayerLayerClass = objc_getClass("AVPlayerLayer");

            AllocSelector = sel_registerName("alloc");
            ReleaseSelector = sel_registerName("release");
            RetainSelector = sel_registerName("retain");
            InitWithFrameSelector = sel_registerName("initWithFrame:");
            SetWantsLayerSelector = sel_registerName("setWantsLayer:");
            SetLayerSelector = sel_registerName("setLayer:");
            FileUrlWithPathSelector = sel_registerName("fileURLWithPath:");
            InitWithUrlSelector = sel_registerName("initWithURL:");
            PlayerLayerWithPlayerSelector = sel_registerName("playerLayerWithPlayer:");
            SetVideoGravitySelector = sel_registerName("setVideoGravity:");
            PlaySelector = sel_registerName("play");
            PauseSelector = sel_registerName("pause");
            CurrentTimeSelector = sel_registerName("currentTime");
            CurrentItemSelector = sel_registerName("currentItem");
            DurationSelector = sel_registerName("duration");
            SeekToTimeSelector = sel_registerName("seekToTime:");
            SetVolumeSelector = sel_registerName("setVolume:");
            StatusSelector = sel_registerName("status");
            ErrorSelector = sel_registerName("error");
            LocalizedDescriptionSelector = sel_registerName("localizedDescription");
            Utf8StringSelector = sel_registerName("UTF8String");
            StringWithUtf8StringSelector = sel_registerName("stringWithUTF8String:");

            if (NsViewClass == IntPtr.Zero || NsUrlClass == IntPtr.Zero || NsStringClass == IntPtr.Zero ||
                AvPlayerClass == IntPtr.Zero || AvPlayerLayerClass == IntPtr.Zero)
            {
                throw new PlatformNotSupportedException("Required macOS media classes are unavailable.");
            }

            _loaded = true;
        }

        internal static IntPtr CreateString(string value)
        {
            EnsureLoaded();
            var utf8 = Marshal.StringToCoTaskMemUTF8(value);
            try
            {
                return SendIntPtr(NsStringClass, StringWithUtf8StringSelector, utf8);
            }
            finally
            {
                Marshal.FreeCoTaskMem(utf8);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal readonly struct CMTime
        {
            private readonly long _value;
            private readonly int _timescale;
            private readonly uint _flags;
            private readonly long _epoch;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal readonly struct NSRect
        {
            private readonly double _x;
            private readonly double _y;
            private readonly double _width;
            private readonly double _height;
        }

        [DllImport(ObjCLibrary)]
        private static extern IntPtr objc_getClass(string name);

        [DllImport(ObjCLibrary)]
        private static extern IntPtr sel_registerName(string name);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        internal static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        internal static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector, IntPtr argument);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        internal static extern IntPtr SendRect(IntPtr receiver, IntPtr selector, NSRect argument);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        internal static extern void SendBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool argument);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        internal static extern void SendFloat(IntPtr receiver, IntPtr selector, float argument);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        internal static extern void SendVoid(IntPtr receiver, IntPtr selector);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        internal static extern long SendLong(IntPtr receiver, IntPtr selector);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        internal static extern CMTime SendTime(IntPtr receiver, IntPtr selector);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        internal static extern void SendTimeArgument(IntPtr receiver, IntPtr selector, CMTime argument);

        [DllImport(CoreMediaFramework)]
        internal static extern CMTime CMTimeMakeWithSeconds(double seconds, int preferredTimescale);

        [DllImport(CoreMediaFramework)]
        internal static extern double CMTimeGetSeconds(CMTime time);
    }
}
