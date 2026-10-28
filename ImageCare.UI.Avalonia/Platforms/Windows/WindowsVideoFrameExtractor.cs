using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using HanumanInstitute.LibMpv;
using HanumanInstitute.LibMpv.Core;

using ImageCare.Core.Domain.Media;

using Serilog;

namespace ImageCare.UI.Avalonia.Platforms.Windows;

internal sealed class WindowsVideoFrameExtractor : IVideoFrameExtractor
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(12);
    private static readonly object MpvConfigurationGate = new();

    private readonly ILogger _logger;
    private readonly object _extractionGate = new();

    public WindowsVideoFrameExtractor(ILogger logger)
    {
        _logger = logger;
    }

    public byte[]? ExtractJpegFrame(string videoPath, TimeSpan position, int maxPixelSize)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        lock (_extractionGate)
        {
            try
            {
                var fullPath = Path.GetFullPath(videoPath);
                if (!File.Exists(fullPath))
                {
                    return null;
                }

                ConfigureMpv();
                return ExtractFrame(
                    fullPath,
                    position < TimeSpan.Zero ? TimeSpan.Zero : position,
                    Math.Clamp(maxPixelSize, 64, 4096));
            }
            catch (Exception exception)
            {
                _logger.Warning(exception, "Failed to extract a Windows video frame from {VideoPath}", videoPath);
                return null;
            }
        }
    }

    private static void ConfigureMpv()
    {
        lock (MpvConfigurationGate)
        {
            var nativeDirectory = Path.Combine(AppContext.BaseDirectory, "player-native");
            var nativeLibraryPath = Path.Combine(nativeDirectory, "libmpv-2.dll");
            if (!File.Exists(nativeLibraryPath))
            {
                throw new FileNotFoundException("The native Windows video library was not deployed.", nativeLibraryPath);
            }

            MpvApi.RootPath = nativeDirectory;
        }
    }

    private static byte[] ExtractFrame(string videoPath, TimeSpan position, int maxPixelSize)
    {
        using var fileLoaded = new ManualResetEventSlim();
        using var playbackRestarted = new ManualResetEventSlim();
        using var context = new HeadlessMpvContext();

        void OnFileLoaded(object? sender, EventArgs args) => fileLoaded.Set();
        void OnPlaybackRestarted(object? sender, EventArgs args) => playbackRestarted.Set();

        context.FileLoaded += OnFileLoaded;
        context.PlaybackRestart += OnPlaybackRestarted;
        var renderingStarted = false;
        try
        {
            context.StartSoftwareRendering(static () => { });
            renderingStarted = true;
            playbackRestarted.Reset();
            context.LoadFile(videoPath).Invoke(CreateCommandOptions());
            WaitOrThrow(fileLoaded, "Timed out while loading the video.");
            WaitOrThrow(playbackRestarted, "Timed out while decoding the first video frame.");

            if (position > TimeSpan.Zero)
            {
                playbackRestarted.Reset();
                context.Seek(position.TotalSeconds, SeekOption.Absolute | SeekOption.Exact)
                       .Invoke(CreateCommandOptions());
                WaitOrThrow(playbackRestarted, "Timed out while seeking to the preview frame.");
            }

            var sourceWidth = context.DisplayWidth.Get() ?? context.Width.Get() ?? 0;
            var sourceHeight = context.DisplayHeight.Get() ?? context.Height.Get() ?? 0;
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                throw new InvalidOperationException("libmpv returned invalid video dimensions.");
            }

            var scale = Math.Min(1, maxPixelSize / (double)Math.Max(sourceWidth, sourceHeight));
            var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            var stride = checked(width * 4);
            var pixels = new byte[checked(stride * height)];
            var pinnedPixels = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                context.SoftwareRender(width, height, pinnedPixels.AddrOfPinnedObject(), "bgra");
                using var bitmap = new WriteableBitmap(
                    PixelFormat.Bgra8888,
                    AlphaFormat.Opaque,
                    pinnedPixels.AddrOfPinnedObject(),
                    new PixelSize(width, height),
                    new Vector(96, 96),
                    stride);
                using var output = new MemoryStream();
                bitmap.Save(output, new JpegBitmapEncoderOptions { Quality = 85 });
                return output.ToArray();
            }
            finally
            {
                pinnedPixels.Free();
            }
        }
        finally
        {
            context.FileLoaded -= OnFileLoaded;
            context.PlaybackRestart -= OnPlaybackRestarted;
            if (renderingStarted)
            {
                context.StopRendering();
            }
        }
    }

    private static MpvCommandOptions CreateCommandOptions() => new()
    {
        WaitForResponse = true,
        ThrowOnError = true,
        ResponseTimeout = checked((int)OperationTimeout.TotalMilliseconds)
    };

    private static void WaitOrThrow(ManualResetEventSlim signal, string message)
    {
        if (!signal.Wait(OperationTimeout))
        {
            throw new TimeoutException(message);
        }
    }

    private sealed class HeadlessMpvContext : MpvContext
    {
        protected override void OnPreInitialize()
        {
            SetOptionString("vo", "libmpv");
            SetOptionString("audio", "no");
            SetOptionString("pause", "yes");
            SetOptionString("keep-open", "yes");
            SetOptionString("hwdec", "no");
        }
    }
}
