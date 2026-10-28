using System;
using System.IO;
using System.Runtime.InteropServices;

using ImageCare.Core.Domain.Media;

using Serilog;

namespace ImageCare.UI.Avalonia.Platforms.MacOS;

internal sealed class MacOsVideoFrameExtractor : IVideoFrameExtractor
{
    private readonly ILogger _logger;
    private readonly object _extractionGate = new();

    public MacOsVideoFrameExtractor(ILogger logger)
    {
        _logger = logger;
    }

    public byte[]? ExtractJpegFrame(string videoPath, TimeSpan position, int maxPixelSize)
    {
        if (!OperatingSystem.IsMacOS())
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

                return MacNative.ExtractJpegFrame(
                    fullPath,
                    position < TimeSpan.Zero ? TimeSpan.Zero : position,
                    Math.Clamp(maxPixelSize, 64, 4096));
            }
            catch (Exception exception)
            {
                _logger.Warning(exception, "Failed to extract a macOS video frame from {VideoPath}", videoPath);
                return null;
            }
        }
    }

    private static class MacNative
    {
        private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";
        private const string CoreFoundationFramework = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string CoreGraphicsFramework = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreMediaFramework = "/System/Library/Frameworks/CoreMedia.framework/CoreMedia";
        private const string ImageIoFramework = "/System/Library/Frameworks/ImageIO.framework/ImageIO";

        private static readonly object LoadGate = new();
        private static bool _loaded;

        private static IntPtr _nsUrlClass;
        private static IntPtr _nsStringClass;
        private static IntPtr _avUrlAssetClass;
        private static IntPtr _avAssetImageGeneratorClass;

        private static IntPtr _allocSelector;
        private static IntPtr _releaseSelector;
        private static IntPtr _fileUrlWithPathSelector;
        private static IntPtr _stringWithUtf8StringSelector;
        private static IntPtr _initWithUrlOptionsSelector;
        private static IntPtr _initWithAssetSelector;
        private static IntPtr _setAppliesPreferredTrackTransformSelector;
        private static IntPtr _setMaximumSizeSelector;
        private static IntPtr _copyCgImageAtTimeSelector;
        private static IntPtr _localizedDescriptionSelector;
        private static IntPtr _utf8StringSelector;

        internal static byte[] ExtractJpegFrame(string fullPath, TimeSpan position, int maxPixelSize)
        {
            EnsureLoaded();

            var pool = objc_autoreleasePoolPush();
            IntPtr asset = IntPtr.Zero;
            IntPtr generator = IntPtr.Zero;
            IntPtr image = IntPtr.Zero;
            IntPtr data = IntPtr.Zero;
            IntPtr destination = IntPtr.Zero;

            try
            {
                var pathString = CreateString(fullPath);
                var url = SendIntPtr(_nsUrlClass, _fileUrlWithPathSelector, pathString);
                if (url == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Could not create the video file URL.");
                }

                var allocatedAsset = SendIntPtr(_avUrlAssetClass, _allocSelector);
                asset = SendIntPtr(allocatedAsset, _initWithUrlOptionsSelector, url, IntPtr.Zero);
                if (asset == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Could not create AVURLAsset.");
                }

                var allocatedGenerator = SendIntPtr(_avAssetImageGeneratorClass, _allocSelector);
                generator = SendIntPtr(allocatedGenerator, _initWithAssetSelector, asset);
                if (generator == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Could not create AVAssetImageGenerator.");
                }

                SendBool(generator, _setAppliesPreferredTrackTransformSelector, true);
                SendSize(
                    generator,
                    _setMaximumSizeSelector,
                    new CGSize(maxPixelSize, maxPixelSize));

                var requestedTime = CMTimeMakeWithSeconds(position.TotalSeconds, 600);
                image = SendCopyCgImage(
                    generator,
                    _copyCgImageAtTimeSelector,
                    requestedTime,
                    out _,
                    out var error);
                if (image == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        GetLocalizedDescription(error, "AVAssetImageGenerator could not extract the frame."));
                }

                data = CFDataCreateMutable(IntPtr.Zero, 0);
                if (data == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Could not allocate JPEG output data.");
                }

                var jpegType = CreateString("public.jpeg");
                destination = CGImageDestinationCreateWithData(data, jpegType, 1, IntPtr.Zero);
                if (destination == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Could not create the JPEG encoder.");
                }

                CGImageDestinationAddImage(destination, image, IntPtr.Zero);
                if (!CGImageDestinationFinalize(destination))
                {
                    throw new InvalidOperationException("ImageIO could not encode the video frame as JPEG.");
                }

                var length = CFDataGetLength(data);
                var source = CFDataGetBytePtr(data);
                if (length <= 0 || length > int.MaxValue || source == IntPtr.Zero)
                {
                    throw new InvalidOperationException("ImageIO returned invalid JPEG data.");
                }

                var result = new byte[checked((int)length)];
                Marshal.Copy(source, result, 0, result.Length);
                return result;
            }
            finally
            {
                if (destination != IntPtr.Zero)
                {
                    CFRelease(destination);
                }

                if (data != IntPtr.Zero)
                {
                    CFRelease(data);
                }

                if (image != IntPtr.Zero)
                {
                    CGImageRelease(image);
                }

                if (generator != IntPtr.Zero)
                {
                    SendVoid(generator, _releaseSelector);
                }

                if (asset != IntPtr.Zero)
                {
                    SendVoid(asset, _releaseSelector);
                }

                objc_autoreleasePoolPop(pool);
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            lock (LoadGate)
            {
                if (_loaded)
                {
                    return;
                }

                NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation");
                NativeLibrary.Load("/System/Library/Frameworks/AVFoundation.framework/AVFoundation");
                NativeLibrary.Load(CoreFoundationFramework);
                NativeLibrary.Load(CoreGraphicsFramework);
                NativeLibrary.Load(CoreMediaFramework);
                NativeLibrary.Load(ImageIoFramework);

                _nsUrlClass = objc_getClass("NSURL");
                _nsStringClass = objc_getClass("NSString");
                _avUrlAssetClass = objc_getClass("AVURLAsset");
                _avAssetImageGeneratorClass = objc_getClass("AVAssetImageGenerator");

                _allocSelector = sel_registerName("alloc");
                _releaseSelector = sel_registerName("release");
                _fileUrlWithPathSelector = sel_registerName("fileURLWithPath:");
                _stringWithUtf8StringSelector = sel_registerName("stringWithUTF8String:");
                _initWithUrlOptionsSelector = sel_registerName("initWithURL:options:");
                _initWithAssetSelector = sel_registerName("initWithAsset:");
                _setAppliesPreferredTrackTransformSelector = sel_registerName("setAppliesPreferredTrackTransform:");
                _setMaximumSizeSelector = sel_registerName("setMaximumSize:");
                _copyCgImageAtTimeSelector = sel_registerName("copyCGImageAtTime:actualTime:error:");
                _localizedDescriptionSelector = sel_registerName("localizedDescription");
                _utf8StringSelector = sel_registerName("UTF8String");

                if (_nsUrlClass == IntPtr.Zero ||
                    _nsStringClass == IntPtr.Zero ||
                    _avUrlAssetClass == IntPtr.Zero ||
                    _avAssetImageGeneratorClass == IntPtr.Zero)
                {
                    throw new PlatformNotSupportedException("Required macOS video frame classes are unavailable.");
                }

                _loaded = true;
            }
        }

        private static IntPtr CreateString(string value)
        {
            var utf8 = Marshal.StringToCoTaskMemUTF8(value);
            try
            {
                return SendIntPtr(_nsStringClass, _stringWithUtf8StringSelector, utf8);
            }
            finally
            {
                Marshal.FreeCoTaskMem(utf8);
            }
        }

        private static string GetLocalizedDescription(IntPtr error, string fallback)
        {
            if (error == IntPtr.Zero)
            {
                return fallback;
            }

            var description = SendIntPtr(error, _localizedDescriptionSelector);
            var utf8 = description == IntPtr.Zero
                ? IntPtr.Zero
                : SendIntPtr(description, _utf8StringSelector);
            return utf8 == IntPtr.Zero
                ? fallback
                : Marshal.PtrToStringUTF8(utf8) ?? fallback;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct CMTime
        {
            private readonly long _value;
            private readonly int _timescale;
            private readonly uint _flags;
            private readonly long _epoch;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct CGSize
        {
            private readonly double _width;
            private readonly double _height;

            public CGSize(double width, double height)
            {
                _width = width;
                _height = height;
            }
        }

        [DllImport(ObjCLibrary)]
        private static extern IntPtr objc_getClass(string name);

        [DllImport(ObjCLibrary)]
        private static extern IntPtr sel_registerName(string name);

        [DllImport(ObjCLibrary)]
        private static extern IntPtr objc_autoreleasePoolPush();

        [DllImport(ObjCLibrary)]
        private static extern void objc_autoreleasePoolPop(IntPtr pool);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector, IntPtr argument);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern IntPtr SendIntPtr(
            IntPtr receiver,
            IntPtr selector,
            IntPtr firstArgument,
            IntPtr secondArgument);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern void SendVoid(IntPtr receiver, IntPtr selector);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern void SendBool(
            IntPtr receiver,
            IntPtr selector,
            [MarshalAs(UnmanagedType.I1)] bool argument);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern void SendSize(IntPtr receiver, IntPtr selector, CGSize size);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern IntPtr SendCopyCgImage(
            IntPtr receiver,
            IntPtr selector,
            CMTime requestedTime,
            out CMTime actualTime,
            out IntPtr error);

        [DllImport(CoreMediaFramework)]
        private static extern CMTime CMTimeMakeWithSeconds(double seconds, int preferredTimescale);

        [DllImport(CoreFoundationFramework)]
        private static extern IntPtr CFDataCreateMutable(IntPtr allocator, nint capacity);

        [DllImport(CoreFoundationFramework)]
        private static extern nint CFDataGetLength(IntPtr data);

        [DllImport(CoreFoundationFramework)]
        private static extern IntPtr CFDataGetBytePtr(IntPtr data);

        [DllImport(CoreFoundationFramework)]
        private static extern void CFRelease(IntPtr value);

        [DllImport(CoreGraphicsFramework)]
        private static extern void CGImageRelease(IntPtr image);

        [DllImport(ImageIoFramework)]
        private static extern IntPtr CGImageDestinationCreateWithData(
            IntPtr data,
            IntPtr type,
            nuint imageCount,
            IntPtr options);

        [DllImport(ImageIoFramework)]
        private static extern void CGImageDestinationAddImage(
            IntPtr destination,
            IntPtr image,
            IntPtr properties);

        [DllImport(ImageIoFramework)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CGImageDestinationFinalize(IntPtr destination);
    }
}
