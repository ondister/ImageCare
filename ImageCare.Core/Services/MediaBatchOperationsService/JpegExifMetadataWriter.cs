using System.Globalization;
using System.Text;

using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Media.Metadata;

namespace ImageCare.Core.Services.MediaBatchOperationsService;

internal static class JpegExifMetadataWriter
{
    private const ushort TypeByte = 1;
    private const ushort TypeAscii = 2;
    private const ushort TypeShort = 3;
    private const ushort TypeLong = 4;
    private const ushort TypeRational = 5;
    private const ushort TagOrientation = 0x0112;
    private const ushort TagDateTime = 0x0132;
    private const ushort TagExifIfd = 0x8769;
    private const ushort TagGpsIfd = 0x8825;
    private const ushort TagExposureTime = 0x829A;
    private const ushort TagFNumber = 0x829D;
    private const ushort TagIso = 0x8827;
    private const ushort TagDateTimeOriginal = 0x9003;
    private const ushort TagDateTimeDigitized = 0x9004;
    private const ushort TagExifImageWidth = 0xA002;
    private const ushort TagExifImageHeight = 0xA003;
    private static readonly byte[] ExifIdentifier = "Exif\0\0"u8.ToArray();

    public static async Task WriteAsync(
        string jpegPath,
        IMediaMetadata metadata,
        CancellationToken cancellationToken)
    {
        var exifPayload = BuildExifPayload(metadata);
        if (exifPayload.Length > ushort.MaxValue - 2)
        {
            throw new InvalidDataException("The exported EXIF metadata exceeds the JPEG APP1 size limit.");
        }

        var directory = Path.GetDirectoryName(jpegPath)
            ?? throw new InvalidOperationException($"Cannot determine a directory for {jpegPath}.");
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(jpegPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var input = new FileStream(
                jpegPath,
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
                await RewriteJpegAsync(input, output, exifPayload, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, jpegPath, true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static byte[] BuildExifPayload(IMediaMetadata metadata)
    {
        var dateTime = Encoding.ASCII.GetBytes(
            metadata.CreationDateTime.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture) + '\0');
        var orientation = metadata.Orientation is >= ExifOrientation.TopLeft and <= ExifOrientation.LeftBottom
            ? (ushort)metadata.Orientation
            : (ushort)ExifOrientation.TopLeft;
        var imageMetadata = metadata as IImageMetadata;
        var hasExposure = TryParsePositiveRational(imageMetadata?.ShutterSpeed, out var exposure);
        var hasAperture = TryParsePositiveRational(imageMetadata?.Aperture, out var aperture);
        var hasIso = imageMetadata?.Iso is > 0 and <= ushort.MaxValue;
        var hasWidth = metadata.Width > 0;
        var hasHeight = metadata.Height > 0;
        var hasGps = HasLocation(metadata.Location);

        var ifd0EntryCount = hasGps ? 4 : 3;
        const uint ifd0Offset = 8;
        var dateTimeOffset = ifd0Offset + GetIfdSize(ifd0EntryCount);
        var exifIfdOffset = dateTimeOffset + (uint)dateTime.Length;
        var exifEntryCount = 2 +
            (hasExposure ? 1 : 0) +
            (hasAperture ? 1 : 0) +
            (hasIso ? 1 : 0) +
            (hasWidth ? 1 : 0) +
            (hasHeight ? 1 : 0);
        var dataOffset = exifIfdOffset + GetIfdSize(exifEntryCount);

        var exposureOffset = hasExposure ? Allocate(ref dataOffset, 8) : 0;
        var apertureOffset = hasAperture ? Allocate(ref dataOffset, 8) : 0;
        var originalDateOffset = Allocate(ref dataOffset, dateTime.Length);
        var digitizedDateOffset = Allocate(ref dataOffset, dateTime.Length);
        var gpsIfdOffset = hasGps ? Allocate(ref dataOffset, checked((int)GetIfdSize(7))) : 0;
        var latitudeOffset = hasGps ? Allocate(ref dataOffset, 24) : 0;
        var longitudeOffset = hasGps ? Allocate(ref dataOffset, 24) : 0;
        var altitudeOffset = hasGps ? Allocate(ref dataOffset, 8) : 0;

        using var tiff = new MemoryStream(checked((int)dataOffset));
        using var writer = new BinaryWriter(tiff, Encoding.ASCII, leaveOpen: true);

        writer.Write((byte)'I');
        writer.Write((byte)'I');
        writer.Write((ushort)42);
        writer.Write(ifd0Offset);

        writer.Write((ushort)ifd0EntryCount);
        WriteShortEntry(writer, TagOrientation, orientation);
        WriteOffsetEntry(writer, TagDateTime, TypeAscii, (uint)dateTime.Length, dateTimeOffset);
        WriteOffsetEntry(writer, TagExifIfd, TypeLong, 1, exifIfdOffset);
        if (hasGps)
        {
            WriteOffsetEntry(writer, TagGpsIfd, TypeLong, 1, gpsIfdOffset);
        }

        writer.Write(0u);
        writer.Write(dateTime);

        writer.Write((ushort)exifEntryCount);
        if (hasExposure)
        {
            WriteOffsetEntry(writer, TagExposureTime, TypeRational, 1, exposureOffset);
        }

        if (hasAperture)
        {
            WriteOffsetEntry(writer, TagFNumber, TypeRational, 1, apertureOffset);
        }

        if (hasIso)
        {
            WriteShortEntry(writer, TagIso, (ushort)imageMetadata!.Iso!.Value);
        }

        WriteOffsetEntry(writer, TagDateTimeOriginal, TypeAscii, (uint)dateTime.Length, originalDateOffset);
        WriteOffsetEntry(writer, TagDateTimeDigitized, TypeAscii, (uint)dateTime.Length, digitizedDateOffset);
        if (hasWidth)
        {
            WriteLongEntry(writer, TagExifImageWidth, checked((uint)metadata.Width));
        }

        if (hasHeight)
        {
            WriteLongEntry(writer, TagExifImageHeight, checked((uint)metadata.Height));
        }

        writer.Write(0u);

        if (hasExposure)
        {
            WriteRational(writer, exposure);
        }

        if (hasAperture)
        {
            WriteRational(writer, aperture);
        }

        writer.Write(dateTime);
        writer.Write(dateTime);

        if (hasGps)
        {
            WriteGpsIfd(writer, metadata.Location, latitudeOffset, longitudeOffset, altitudeOffset);
        }

        writer.Flush();
        using var payload = new MemoryStream(ExifIdentifier.Length + checked((int)tiff.Length));
        payload.Write(ExifIdentifier);
        tiff.Position = 0;
        tiff.CopyTo(payload);
        return payload.ToArray();
    }

    private static void WriteGpsIfd(
        BinaryWriter writer,
        Location location,
        uint latitudeOffset,
        uint longitudeOffset,
        uint altitudeOffset)
    {
        var latitude = ToDegreesMinutesSeconds(location.Latitude);
        var longitude = ToDegreesMinutesSeconds(location.Longitude);

        writer.Write((ushort)7);
        WriteByteArrayEntry(writer, 0, [2, 3, 0, 0]);
        WriteAsciiEntry(writer, 1, location.Latitude < 0 ? 'S' : 'N');
        WriteOffsetEntry(writer, 2, TypeRational, 3, latitudeOffset);
        WriteAsciiEntry(writer, 3, location.Longitude < 0 ? 'W' : 'E');
        WriteOffsetEntry(writer, 4, TypeRational, 3, longitudeOffset);
        WriteByteEntry(writer, 5, location.Altitude < 0 ? (byte)1 : (byte)0);
        WriteOffsetEntry(writer, 6, TypeRational, 1, altitudeOffset);
        writer.Write(0u);

        foreach (var value in latitude)
        {
            WriteRational(writer, value);
        }

        foreach (var value in longitude)
        {
            WriteRational(writer, value);
        }

        WriteRational(writer, ToRational(Math.Abs(location.Altitude), 1_000));
    }

    private static async Task RewriteJpegAsync(
        Stream input,
        Stream output,
        byte[] exifPayload,
        CancellationToken cancellationToken)
    {
        var soi = new byte[2];
        await ReadExactlyAsync(input, soi, cancellationToken).ConfigureAwait(false);
        if (soi[0] != 0xFF || soi[1] != 0xD8)
        {
            throw new InvalidDataException("The exported preview is not a valid JPEG stream.");
        }

        await output.WriteAsync(soi, cancellationToken).ConfigureAwait(false);

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

            var isExif = segment.Marker == 0xE1 && segment.Payload.AsSpan().StartsWith(ExifIdentifier);
            if (!isExif)
            {
                segments.Add(segment);
            }
        }

        var insertionIndex = 0;
        while (insertionIndex < segments.Count && segments[insertionIndex].Marker == 0xE0)
        {
            insertionIndex++;
        }

        for (var index = 0; index <= segments.Count; index++)
        {
            if (index == insertionIndex)
            {
                await WriteExifSegmentAsync(output, exifPayload, cancellationToken).ConfigureAwait(false);
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

    private static async Task WriteExifSegmentAsync(
        Stream output,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var jpegLength = payload.Length + 2;
        var header = new byte[]
        {
            0xFF,
            0xE1,
            (byte)(jpegLength >> 8),
            (byte)jpegLength
        };
        await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
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

    private static uint GetIfdSize(int entryCount) => checked((uint)(2 + entryCount * 12 + 4));

    private static uint Allocate(ref uint offset, int length)
    {
        var result = offset;
        offset = checked(offset + (uint)length);
        return result;
    }

    private static void WriteShortEntry(BinaryWriter writer, ushort tag, ushort value)
    {
        writer.Write(tag);
        writer.Write(TypeShort);
        writer.Write(1u);
        writer.Write(value);
        writer.Write((ushort)0);
    }

    private static void WriteLongEntry(BinaryWriter writer, ushort tag, uint value)
    {
        writer.Write(tag);
        writer.Write(TypeLong);
        writer.Write(1u);
        writer.Write(value);
    }

    private static void WriteOffsetEntry(
        BinaryWriter writer,
        ushort tag,
        ushort type,
        uint count,
        uint offset)
    {
        writer.Write(tag);
        writer.Write(type);
        writer.Write(count);
        writer.Write(offset);
    }

    private static void WriteByteArrayEntry(BinaryWriter writer, ushort tag, byte[] value)
    {
        writer.Write(tag);
        writer.Write(TypeByte);
        writer.Write((uint)value.Length);
        writer.Write(value);
    }

    private static void WriteByteEntry(BinaryWriter writer, ushort tag, byte value)
    {
        writer.Write(tag);
        writer.Write(TypeByte);
        writer.Write(1u);
        writer.Write(value);
        writer.Write(new byte[3]);
    }

    private static void WriteAsciiEntry(BinaryWriter writer, ushort tag, char value)
    {
        writer.Write(tag);
        writer.Write(TypeAscii);
        writer.Write(2u);
        writer.Write((byte)value);
        writer.Write((byte)0);
        writer.Write((ushort)0);
    }

    private static void WriteRational(BinaryWriter writer, RationalValue value)
    {
        writer.Write(value.Numerator);
        writer.Write(value.Denominator);
    }

    private static bool TryParsePositiveRational(string? description, out RationalValue value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        var token = description.Trim();
        if (token.StartsWith("f/", StringComparison.OrdinalIgnoreCase))
        {
            token = token[2..];
        }

        token = token.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        var parts = token.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) &&
            numerator > 0 && denominator > 0)
        {
            value = ToRational(numerator / denominator, 1_000_000);
            return true;
        }

        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue) ||
            decimalValue <= 0 ||
            !double.IsFinite(decimalValue))
        {
            return false;
        }

        value = ToRational(decimalValue, 1_000_000);
        return true;
    }

    private static RationalValue[] ToDegreesMinutesSeconds(double coordinate)
    {
        const long precision = 1_000_000;
        var totalSeconds = checked((long)Math.Round(Math.Abs(coordinate) * 3_600 * precision));
        var degrees = totalSeconds / (3_600 * precision);
        totalSeconds -= degrees * 3_600 * precision;
        var minutes = totalSeconds / (60 * precision);
        var seconds = totalSeconds - minutes * 60 * precision;

        return
        [
            new RationalValue(checked((uint)degrees), 1),
            new RationalValue(checked((uint)minutes), 1),
            new RationalValue(checked((uint)seconds), checked((uint)precision))
        ];
    }

    private static RationalValue ToRational(double value, uint denominator)
    {
        var numerator = Math.Round(value * denominator);
        if (!double.IsFinite(numerator) || numerator < 0 || numerator > uint.MaxValue)
        {
            return new RationalValue(0, 1);
        }

        return new RationalValue((uint)numerator, denominator);
    }

    private static bool HasLocation(Location location) =>
        location != Location.Empty &&
        double.IsFinite(location.Latitude) &&
        double.IsFinite(location.Longitude) &&
        double.IsFinite(location.Altitude) &&
        location.Latitude is >= -90 and <= 90 &&
        location.Longitude is >= -180 and <= 180;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Preserve the original metadata-writing failure.
        }
    }

    private readonly record struct RationalValue(uint Numerator, uint Denominator);

    private sealed record JpegSegment(byte Marker, byte[] Payload, byte[] Encoded);
}
