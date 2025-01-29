using System.Runtime.InteropServices;

using LibRawDotNet.Native;

using TurboJpegWrapper;

using static LibRawDotNet.Native.LibRawNativeWrapper;

namespace LibRawDotNet;

public class LibRawData : IDisposable
{
    private const int _imageDataOffset = 16;

    private readonly IntPtr _libRawDataPointer;
    private bool _disposedValue;
    private IProgress<LibRawProgress>? _progress;

    private LibRawData(IntPtr libRawDataPointer)
    {
        _libRawDataPointer = libRawDataPointer;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public static LibRawData OpenFile(string fileName)
    {
        if (!File.Exists(fileName))
        {
            throw new FileNotFoundException("File not found.", fileName);
        }

        if (!IsFileAccessible(fileName))
        {
            throw new IOException($"File {fileName} hase been already opened");
        }

        var handler = libraw_init(LibRawInitFlags.LibRawOptionsNone);

        if (handler == IntPtr.Zero)
        {
            throw new InvalidOperationException("LibRaw native library was not initialized.");
        }

        var result = libraw_open_wfile(handler, fileName);

        if (result == LibRawErrors.LibRawFileUnsupported)
        {
            throw new NotSupportedException($"Format of the file {fileName} is unsupported. Probably is not raw.");
        }

        if (result != LibRawErrors.LibRawSuccess)
        {
            throw new InvalidOperationException($"File {fileName} was not opened with internal error: {result}.");
        }

        return new LibRawData(handler);
    }

    public LibRawData WithProgress(IProgress<LibRawProgress> progress)
    {
        if (_progress != null)
        {
            _progress = progress;

            // Set callback function for processing.
            libraw_set_progress_handler(
                _libRawDataPointer,
                (_, state, iteration, _) =>
                {
                    if (iteration == 0 && _progress != null)
                    {
                        _progress.Report(state);
                    }

                    return 0;
                },
                IntPtr.Zero);
        }

        return this;
    }

    public Stream GetPreviewJpegStream(int previewIndex)
    {
        var unpackError = libraw_unpack_thumb_ex(_libRawDataPointer, previewIndex);
        if (unpackError != LibRawErrors.LibRawSuccess)
        {
            throw new LibRawException($"Error of unpacking preview from raw with index {previewIndex}: {unpackError}");
        }

        var dcDrawError = LibRawErrors.LibRawSuccess;
        var thumbPointer = libraw_dcraw_make_mem_thumb(_libRawDataPointer, ref dcDrawError);
        if (dcDrawError != LibRawErrors.LibRawSuccess)
        {
            throw new LibRawException($"Error of making preview from raw with index {previewIndex}: {unpackError}");
        }

        var thumbInfo = Marshal.PtrToStructure<libraw_processed_image_t>(thumbPointer);

        if (thumbInfo.type != LibRawImageFormats.LibRawImageJpeg)
        {
            libraw_dcraw_clear_mem(thumbPointer);

            throw new LibRawException($"Requested preview with index {previewIndex} is not jpeg.");
        }

        if (thumbInfo.data_size <= 0)
        {
            libraw_dcraw_clear_mem(thumbPointer);

            throw new LibRawException($"There is no preview with index {previewIndex} in raw.");
        }

        var buffer = new byte[thumbInfo.data_size];
        Marshal.Copy(thumbPointer + _imageDataOffset, buffer, 0, buffer.Length);

        libraw_dcraw_clear_mem(thumbPointer);

        return new MemoryStream(buffer);
    }

    public static bool IsFileAccessible(string filePath)
    {
        try
        {
            using (var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                return true;
            }
        }
        catch (IOException)
        {
            return false;
        }
    }

    public Stream GetRawJpegStream(int quality)
    {
        var unpackError = libraw_unpack(_libRawDataPointer);
        if (unpackError != LibRawErrors.LibRawSuccess)
        {
            throw new LibRawException($"Error of unpacking raw image from raw: {unpackError}");
        }

        var dcDrawError = LibRawErrors.LibRawSuccess;
        var processError = libraw_dcraw_process(_libRawDataPointer);
        if (processError != LibRawErrors.LibRawSuccess)
        {
            throw new LibRawException($"Error of processing image from raw: {unpackError}");
        }

        var imagePointer = libraw_dcraw_make_mem_image(_libRawDataPointer, ref dcDrawError);
        if (dcDrawError != LibRawErrors.LibRawSuccess)
        {
            throw new LibRawException($"Error of making image from raw: {unpackError}");
        }

        var imageInfo = Marshal.PtrToStructure<libraw_processed_image_t>(imagePointer);

        if (imageInfo.type != LibRawImageFormats.LibRawImageBitmap)
        {
            libraw_dcraw_clear_mem(imagePointer);

            throw new LibRawException("Requested image is not bitmap.");
        }

        if (imageInfo.data_size <= 0)
        {
            libraw_dcraw_clear_mem(imagePointer);

            throw new LibRawException("There is no image in raw.");
        }

        var imageDataPointer = imagePointer + Marshal.OffsetOf(typeof(libraw_processed_image_t), "data").ToInt32();
        var stride = imageInfo.width * imageInfo.colors * (imageInfo.bits / 8);
        var compressor = new TJCompressor();
        var compressedBytes = compressor.Compress(imageDataPointer, stride, imageInfo.width, imageInfo.height, TJPixelFormat.RGB, quality: quality);

        libraw_dcraw_clear_mem(imagePointer);

        return new MemoryStream(compressedBytes);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            if (_libRawDataPointer != IntPtr.Zero)
            {
                libraw_close(_libRawDataPointer);
            }

            _disposedValue = true;
        }
    }

    ~LibRawData()
    {
        Dispose(disposing: false);
    }
}