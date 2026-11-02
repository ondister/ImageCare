using System.Text;
using System.Xml.Linq;
using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Services.FileSystemService;

namespace ImageCare.Core.Services.MediaAnnotationsService;

internal sealed class EmbeddedJpegXmpStorage(
    XmpDocumentCodec codec,
    IFileSystemService fileSystemService)
{
    private const string StandardXmpIdentifier = "http://ns.adobe.com/xap/1.0/\0";
    private static readonly byte[] StandardXmpIdentifierBytes = Encoding.ASCII.GetBytes(StandardXmpIdentifier);
    private static readonly byte[] ExifIdentifierBytes = "Exif\0\0"u8.ToArray();

    public async Task<XDocument?> ReadAsync(string mediaPath, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            mediaPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await ValidateAndCopySoiAsync(input, null, cancellationToken).ConfigureAwait(false);
        while (true)
        {
            var segment = await ReadJpegSegmentAsync(input, cancellationToken).ConfigureAwait(false);
            if (segment is null || segment.Marker == 0xDA || segment.Marker == 0xD9)
            {
                return null;
            }

            if (segment.Marker == 0xE1 && StartsWith(segment.Payload, StandardXmpIdentifierBytes))
            {
                var xml = segment.Payload.AsMemory(StandardXmpIdentifierBytes.Length);
                while (xml.Length > 0 && xml.Span[^1] == 0)
                {
                    xml = xml[..^1];
                }

                await using var xmpStream = new MemoryStream(xml.ToArray(), writable: false);
                return await codec.LoadAsync(xmpStream, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task WriteAsync(
        string mediaPath,
        MediaAnnotations annotations,
        Action ensureReadyToCommit,
        CancellationToken cancellationToken)
    {
        var document = await ReadAsync(mediaPath, cancellationToken).ConfigureAwait(false)
            ?? codec.CreateDocument();
        codec.WriteAnnotations(document, annotations);
        var xmpBytes = codec.Serialize(document);
        var app1PayloadLength = StandardXmpIdentifierBytes.Length + xmpBytes.Length;
        if (app1PayloadLength > ushort.MaxValue - 2)
        {
            throw new InvalidDataException("The standard embedded XMP packet exceeds the JPEG APP1 size limit.");
        }

        var directory = Path.GetDirectoryName(mediaPath)
            ?? throw new InvalidOperationException($"Cannot determine a directory for {mediaPath}.");
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(mediaPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var input = new FileStream(
                mediaPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await RewriteJpegWithXmpAsync(input, output, xmpBytes, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            ensureReadyToCommit();
            fileSystemService.CopyFileTimestampsAndAttributes(mediaPath, temporaryPath);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, mediaPath, true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static async Task RewriteJpegWithXmpAsync(
        Stream input,
        Stream output,
        byte[] xmpBytes,
        CancellationToken cancellationToken)
    {
        await ValidateAndCopySoiAsync(input, output, cancellationToken).ConfigureAwait(false);

        var segments = new List<JpegSegment>();
        JpegSegment terminalSegment;
        while (true)
        {
            var segment = await ReadJpegSegmentAsync(input, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("Unexpected end of JPEG before the scan data.");
            if (segment.Marker is 0xDA or 0xD9)
            {
                terminalSegment = segment;
                break;
            }

            var isStandardXmp = segment.Marker == 0xE1
                && StartsWith(segment.Payload, StandardXmpIdentifierBytes);
            if (!isStandardXmp)
            {
                segments.Add(segment);
            }
        }

        var insertionIndex = 0;
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            var isApp0 = segment.Marker == 0xE0;
            var isExifApp1 = segment.Marker == 0xE1 && StartsWith(segment.Payload, ExifIdentifierBytes);
            if (isApp0 || isExifApp1)
            {
                insertionIndex = index + 1;
            }
        }

        for (var index = 0; index <= segments.Count; index++)
        {
            if (index == insertionIndex)
            {
                await WriteXmpApp1SegmentAsync(output, xmpBytes, cancellationToken).ConfigureAwait(false);
            }

            if (index < segments.Count)
            {
                await output.WriteAsync(segments[index].Encoded, cancellationToken).ConfigureAwait(false);
            }
        }

        await output.WriteAsync(terminalSegment.Encoded, cancellationToken).ConfigureAwait(false);
        if (terminalSegment.Marker == 0xDA)
        {
            await input.CopyToAsync(output, 128 * 1024, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ValidateAndCopySoiAsync(
        Stream input,
        Stream? output,
        CancellationToken cancellationToken)
    {
        var soi = new byte[2];
        await ReadExactlyAsync(input, soi, cancellationToken).ConfigureAwait(false);
        if (soi[0] != 0xFF || soi[1] != 0xD8)
        {
            throw new InvalidDataException("The file is not a valid JPEG stream.");
        }

        if (output is not null)
        {
            await output.WriteAsync(soi, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<JpegSegment?> ReadJpegSegmentAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        var first = input.ReadByte();
        if (first < 0)
        {
            return null;
        }

        if (first != 0xFF)
        {
            throw new InvalidDataException("Invalid JPEG marker prefix.");
        }

        using var encoded = new MemoryStream();
        encoded.WriteByte((byte)first);
        int marker;
        do
        {
            marker = input.ReadByte();
            if (marker < 0)
            {
                throw new EndOfStreamException();
            }

            encoded.WriteByte((byte)marker);
        }
        while (marker == 0xFF);

        if (marker == 0x00)
        {
            throw new InvalidDataException("Unexpected stuffed byte outside JPEG scan data.");
        }

        if (marker is 0xD8 or 0xD9 or 0x01 || marker is >= 0xD0 and <= 0xD7)
        {
            return new JpegSegment((byte)marker, [], encoded.ToArray());
        }

        var lengthBytes = new byte[2];
        await ReadExactlyAsync(input, lengthBytes, cancellationToken).ConfigureAwait(false);
        encoded.Write(lengthBytes);
        var length = (lengthBytes[0] << 8) | lengthBytes[1];
        if (length < 2)
        {
            throw new InvalidDataException("Invalid JPEG segment length.");
        }

        var payload = new byte[length - 2];
        await ReadExactlyAsync(input, payload, cancellationToken).ConfigureAwait(false);
        encoded.Write(payload);
        return new JpegSegment((byte)marker, payload, encoded.ToArray());
    }

    private static async Task WriteXmpApp1SegmentAsync(
        Stream output,
        byte[] xmpBytes,
        CancellationToken cancellationToken)
    {
        var payloadLength = StandardXmpIdentifierBytes.Length + xmpBytes.Length;
        var jpegLength = payloadLength + 2;
        var header = new byte[]
        {
            0xFF,
            0xE1,
            (byte)(jpegLength >> 8),
            (byte)jpegLength
        };
        await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(StandardXmpIdentifierBytes, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(xmpBytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }
    }

    private static bool StartsWith(byte[] value, byte[] prefix) =>
        value.Length >= prefix.Length && value.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Cleanup must not hide the original write failure.
        }
    }

    private sealed record JpegSegment(byte Marker, byte[] Payload, byte[] Encoded);
}
