using System.Runtime.InteropServices;

using LibRawDotNet.Native;

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

    public static LibRawData OpenFile(string fileName)
    {
        if (!File.Exists(fileName))
        {
            throw new FileNotFoundException("File not found.", fileName);
        }

        var handler = libraw_init(LibRawInitFlags.LibRawOptionsNone);

        if (handler == IntPtr.Zero)
        {
            throw new InvalidOperationException("LibRaw native library was not initialized.");
        }

        var result = libraw_open_file(handler, fileName);

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

    public async Task<Stream> GetPreviewJpegStream(int previewIndex,
                                                   CancellationToken cancellationToken = default)
    {
        return await Task.Run(
                   () =>
                   {
                       var unpackError = libraw_unpack_thumb_ex(_libRawDataPointer, previewIndex);
                       if (unpackError != LibRawErrors.LibRawSuccess)
                       {
                           throw new LibRawException($"Error of unpacking preview from raw with index {previewIndex}: {unpackError}");
                       }

                       cancellationToken.ThrowIfCancellationRequested();

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

                       if (cancellationToken.IsCancellationRequested)
                       {
                           libraw_dcraw_clear_mem(thumbPointer);

                           throw new TaskCanceledException();
                       }

                       var buffer = new byte[thumbInfo.data_size];
                       Marshal.Copy(thumbPointer + _imageDataOffset, buffer, 0, buffer.Length);

                       libraw_dcraw_clear_mem(thumbPointer);

                       return new MemoryStream(buffer);
                   },
                   cancellationToken);
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