using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Services.FileSystemService;

namespace ImageCare.Core.Services.MediaAnnotationsService;

public sealed class XmpMediaAnnotationsStorageService : IMediaAnnotationsStorageService
{
    private const string StandardXmpIdentifier = "http://ns.adobe.com/xap/1.0/\0";
    private static readonly byte[] StandardXmpIdentifierBytes = Encoding.ASCII.GetBytes(StandardXmpIdentifier);
    private static readonly byte[] ExifIdentifierBytes = "Exif\0\0"u8.ToArray();
    private static readonly XNamespace XmpMetaNamespace = "adobe:ns:meta/";
    private static readonly XNamespace RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace XmpNamespace = "http://ns.adobe.com/xap/1.0/";
    private static readonly XNamespace DcNamespace = "http://purl.org/dc/elements/1.1/";
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private readonly IFileSystemService _fileSystemService;

    public XmpMediaAnnotationsStorageService(IFileSystemService fileSystemService)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
    }

    public async Task<MediaAnnotationsSnapshot> ReadAsync(
        string mediaPath,
        CancellationToken cancellationToken = default)
    {
        var fullMediaPath = NormalizeExistingMediaPath(mediaPath);
        XmpSidecarPath.EnsureUnambiguousRawOwner(fullMediaPath);
        var storagePath = XmpSidecarPath.GetExternalSidecarPath(fullMediaPath) ?? fullMediaPath;
        var document = XmpSidecarPath.UsesEmbeddedXmp(fullMediaPath)
            ? await ReadEmbeddedXmpAsync(fullMediaPath, cancellationToken).ConfigureAwait(false)
            : await ReadSidecarXmpAsync(storagePath, cancellationToken).ConfigureAwait(false);

        return new MediaAnnotationsSnapshot(
            fullMediaPath,
            document is null ? MediaAnnotations.Empty : ReadAnnotations(document),
            CaptureState(fullMediaPath, storagePath));
    }

    public async Task<MediaAnnotationsSnapshot> WriteAsync(
        string mediaPath,
        MediaAnnotations annotations,
        AnnotationFileState? expectedState = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        var fullMediaPath = NormalizeExistingMediaPath(mediaPath);
        XmpSidecarPath.EnsureUnambiguousRawOwner(fullMediaPath);
        var storagePath = XmpSidecarPath.GetExternalSidecarPath(fullMediaPath) ?? fullMediaPath;
        var initialState = CaptureState(fullMediaPath, storagePath);
        if (expectedState is not null)
        {
            EnsureUnchanged(expectedState, initialState, fullMediaPath);
        }

        if (XmpSidecarPath.UsesEmbeddedXmp(fullMediaPath))
        {
            await WriteEmbeddedXmpAsync(fullMediaPath, annotations, initialState, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await WriteSidecarXmpAsync(fullMediaPath, storagePath, annotations, initialState, cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadAsync(fullMediaPath, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task WriteSidecarXmpAsync(
        string mediaPath,
        string storagePath,
        MediaAnnotations annotations,
        AnnotationFileState initialState,
        CancellationToken cancellationToken)
    {
        var document = await ReadSidecarXmpAsync(storagePath, cancellationToken).ConfigureAwait(false)
            ?? CreateXmpDocument();
        WriteAnnotations(document, annotations);
        var content = SerializeXmp(document);

        var directory = Path.GetDirectoryName(storagePath)
            ?? throw new InvalidOperationException($"Cannot determine a directory for {storagePath}.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(storagePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await output.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            EnsureUnchanged(initialState, CaptureState(mediaPath, storagePath), mediaPath);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, storagePath, true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private async Task WriteEmbeddedXmpAsync(
        string mediaPath,
        MediaAnnotations annotations,
        AnnotationFileState initialState,
        CancellationToken cancellationToken)
    {
        var document = await ReadEmbeddedXmpAsync(mediaPath, cancellationToken).ConfigureAwait(false)
            ?? CreateXmpDocument();
        WriteAnnotations(document, annotations);
        var xmpBytes = SerializeXmp(document);
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

            EnsureUnchanged(initialState, CaptureState(mediaPath, mediaPath), mediaPath);
            _fileSystemService.CopyFileTimestampsAndAttributes(mediaPath, temporaryPath);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, mediaPath, true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static async Task<XDocument?> ReadSidecarXmpAsync(
        string storagePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(storagePath))
        {
            return null;
        }

        await using var input = new FileStream(
            storagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await LoadXmpAsync(input, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<XDocument?> ReadEmbeddedXmpAsync(
        string mediaPath,
        CancellationToken cancellationToken)
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
                return await LoadXmpAsync(xmpStream, cancellationToken).ConfigureAwait(false);
            }
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

    private static async Task<XDocument> LoadXmpAsync(Stream input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            CloseInput = false
        };
        using var reader = XmlReader.Create(input, settings);
        return await XDocument.LoadAsync(reader, LoadOptions.PreserveWhitespace, cancellationToken)
            .ConfigureAwait(false);
    }

    private static MediaAnnotations ReadAnnotations(XDocument document)
    {
        var descriptions = document.Descendants(RdfNamespace + "Description").ToArray();
        var ratingValue = descriptions
            .Select(description => (string?)description.Attribute(XmpNamespace + "Rating"))
            .Concat(descriptions.SelectMany(description =>
                description.Elements(XmpNamespace + "Rating")
                    .Select(element => element.Element(RdfNamespace + "value")?.Value ?? element.Value)))
            .FirstOrDefault(value => int.TryParse(value, out var parsed) && parsed is >= 0 and <= 5);
        var rating = int.TryParse(ratingValue, out var parsedRating) ? parsedRating : 0;
        var tags = descriptions
            .SelectMany(description => description.Elements(DcNamespace + "subject"))
            .SelectMany(subject => subject.Descendants(RdfNamespace + "li"))
            .Select(ReadListItemValue);
        return new MediaAnnotations(rating, tags);
    }

    private static void WriteAnnotations(XDocument document, MediaAnnotations annotations)
    {
        var rdf = document.Descendants(RdfNamespace + "RDF").FirstOrDefault();
        if (rdf is null)
        {
            var root = document.Root ?? new XElement(XmpMetaNamespace + "xmpmeta");
            if (document.Root is null)
            {
                document.Add(root);
            }

            rdf = new XElement(RdfNamespace + "RDF");
            root.Add(rdf);
        }

        var description = rdf.Elements(RdfNamespace + "Description").FirstOrDefault();
        if (description is null)
        {
            description = new XElement(
                RdfNamespace + "Description",
                new XAttribute(RdfNamespace + "about", string.Empty));
            rdf.Add(description);
        }

        // XMP permits parallel standard representations in separate descriptions. ImageCare
        // updates only the first representation selected by its reader and leaves alternates,
        // qualifiers, and application-owned duplicates represented in the XML document.
        var ratingAttribute = document.Descendants(RdfNamespace + "Description")
            .Attributes(XmpNamespace + "Rating")
            .FirstOrDefault();
        var ratingElement = document.Descendants(RdfNamespace + "Description")
            .Elements(XmpNamespace + "Rating")
            .FirstOrDefault();
        if (ratingAttribute is not null)
        {
            ratingAttribute.Value = annotations.Rating.ToString(CultureInfo.InvariantCulture);
        }
        else if (ratingElement is not null)
        {
            var qualifiedValue = ratingElement.Element(RdfNamespace + "value");
            if (qualifiedValue is not null)
            {
                qualifiedValue.Value = annotations.Rating.ToString(CultureInfo.InvariantCulture);
            }
            else if (!ratingElement.HasElements)
            {
                ratingElement.Value = annotations.Rating.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                ratingElement.AddFirst(new XElement(
                    RdfNamespace + "value",
                    annotations.Rating.ToString(CultureInfo.InvariantCulture)));
            }
        }
        else
        {
            description.SetAttributeValue(XmpNamespace + "Rating", annotations.Rating);
        }

        var subject = document.Descendants(RdfNamespace + "Description")
            .Elements(DcNamespace + "subject")
            .FirstOrDefault();
        if (subject is null)
        {
            subject = new XElement(DcNamespace + "subject");
            description.Add(subject);
        }

        var container = subject.Elements().FirstOrDefault(element =>
            element.Name == RdfNamespace + "Bag"
            || element.Name == RdfNamespace + "Seq"
            || element.Name == RdfNamespace + "Alt");
        if (container is null)
        {
            container = new XElement(RdfNamespace + "Bag");
            subject.Add(container);
        }

        var existingItems = container.Elements(RdfNamespace + "li").ToArray();
        var retainedItems = new HashSet<XElement>();
        var replacementItems = annotations.Tags.Select(tag =>
        {
            var retained = existingItems.FirstOrDefault(item =>
                !retainedItems.Contains(item)
                && string.Equals(ReadListItemValue(item), tag, StringComparison.OrdinalIgnoreCase));
            if (retained is not null)
            {
                retainedItems.Add(retained);
                return retained;
            }

            return new XElement(RdfNamespace + "li", tag);
        }).ToArray();

        // Replace only direct list members of the selected subject. Unchanged values retain the
        // original li node, including xml:lang, rdf:* attributes, and nested qualifiers. Parallel
        // dc:subject elements remain untouched and the reader intentionally unions their values.
        existingItems.Remove();
        container.Add(replacementItems);
    }

    private static string ReadListItemValue(XElement item) =>
        item.Element(RdfNamespace + "value")?.Value ?? item.Nodes().OfType<XText>().FirstOrDefault()?.Value ?? item.Value;

    private static XDocument CreateXmpDocument() =>
        new(
            new XElement(
                XmpMetaNamespace + "xmpmeta",
                new XAttribute(XNamespace.Xmlns + "x", XmpMetaNamespace),
                new XElement(
                    RdfNamespace + "RDF",
                    new XAttribute(XNamespace.Xmlns + "rdf", RdfNamespace),
                    new XElement(
                        RdfNamespace + "Description",
                        new XAttribute(RdfNamespace + "about", string.Empty),
                        new XAttribute(XNamespace.Xmlns + "xmp", XmpNamespace),
                        new XAttribute(XNamespace.Xmlns + "dc", DcNamespace)))));

    private static byte[] SerializeXmp(XDocument document)
    {
        using var output = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = false,
            CloseOutput = false
        };
        using (var writer = XmlWriter.Create(output, settings))
        {
            document.WriteTo(writer);
        }

        return output.ToArray();
    }

    private static AnnotationFileState CaptureState(string mediaPath, string storagePath)
    {
        var media = new FileInfo(mediaPath);
        media.Refresh();
        if (!media.Exists)
        {
            throw new FileNotFoundException("Media file was not found.", mediaPath);
        }

        var storage = new FileInfo(storagePath);
        storage.Refresh();
        return new AnnotationFileState(
            storagePath,
            storage.Exists,
            storage.Exists ? storage.Length : 0,
            storage.Exists ? storage.LastWriteTimeUtc : DateTime.MinValue,
            media.Length,
            media.LastWriteTimeUtc);
    }

    private static void EnsureUnchanged(
        AnnotationFileState expected,
        AnnotationFileState actual,
        string mediaPath)
    {
        var unchanged = PathComparer.Equals(expected.StoragePath, actual.StoragePath)
            && expected.StorageExists == actual.StorageExists
            && expected.StorageLength == actual.StorageLength
            && expected.StorageModifiedUtc == actual.StorageModifiedUtc
            && expected.MediaLength == actual.MediaLength
            && expected.MediaModifiedUtc == actual.MediaModifiedUtc;
        if (!unchanged)
        {
            throw new MediaAnnotationsConcurrentModificationException(mediaPath);
        }
    }

    private static string NormalizeExistingMediaPath(string mediaPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        var fullPath = Path.GetFullPath(mediaPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Media file was not found.", fullPath);
        }

        return fullPath;
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