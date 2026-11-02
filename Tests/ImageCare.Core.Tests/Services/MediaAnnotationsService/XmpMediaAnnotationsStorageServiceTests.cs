using System.Text;
using System.Xml;
using System.Xml.Linq;
using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.MediaAnnotationsService;
using Moq;

namespace ImageCare.Core.Tests.Services.MediaAnnotationsService;

[TestFixture]
public sealed class XmpMediaAnnotationsStorageServiceTests
{
    private const string StandardXmpIdentifier = "http://ns.adobe.com/xap/1.0/\0";
    private const string XmpMetaNamespace = "adobe:ns:meta/";
    private const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string XmpNamespace = "http://ns.adobe.com/xap/1.0/";
    private const string DcNamespace = "http://purl.org/dc/elements/1.1/";

    private string _directory = null!;
    private Mock<IFileSystemService> _fileSystem = null!;
    private XmpMediaAnnotationsStorageService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"imagecare-xmp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _fileSystem = new Mock<IFileSystemService>(MockBehavior.Strict);
        _service = new XmpMediaAnnotationsStorageService(_fileSystem.Object);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public async Task ReadAsync_WhenSidecarDoesNotExist_ReturnsEmptyAnnotationsAndMissingStorageState()
    {
        var mediaPath = CreateMediaFile("photo.cr3", "raw");

        var snapshot = await _service.ReadAsync(mediaPath);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.MediaPath, Is.EqualTo(Path.GetFullPath(mediaPath)));
            Assert.That(snapshot.Annotations, Is.EqualTo(MediaAnnotations.Empty));
            Assert.That(snapshot.FileState.StoragePath, Is.EqualTo(Path.ChangeExtension(mediaPath, ".xmp")));
            Assert.That(snapshot.FileState.StorageExists, Is.False);
            Assert.That(snapshot.FileState.MediaLength, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task WriteAsync_WithExistingSidecar_UpdatesOwnedValuesAndPreservesUnrelatedXmlAndQualifiers()
    {
        var mediaPath = CreateMediaFile("photo.cr3", "raw");
        var sidecarPath = Path.ChangeExtension(mediaPath, ".xmp");
        await File.WriteAllTextAsync(sidecarPath, ExistingXmp);

        var snapshot = await _service.WriteAsync(
            mediaPath,
            new MediaAnnotations(5, ["bird", "sea"]));

        var document = XDocument.Load(sidecarPath, LoadOptions.PreserveWhitespace);
        XNamespace rdf = RdfNamespace;
        XNamespace xmp = XmpNamespace;
        XNamespace foo = "urn:imagecare:test";
        var retainedBird = document.Descendants(rdf + "li")
            .Single(item => item.Value == "Bird");

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Annotations.Rating, Is.EqualTo(5));
            Assert.That(snapshot.Annotations.Tags, Is.EqualTo(new[] { "Bird", "sea" }));
            Assert.That(document.Descendants(rdf + "Description").Single().Attribute(xmp + "Rating")?.Value,
                Is.EqualTo("5"));
            Assert.That(document.Descendants(foo + "payload").Single().Value, Is.EqualTo("keep"));
            Assert.That(document.Descendants(foo + "extra").Single().Value, Is.EqualTo("keep"));
            Assert.That(retainedBird.Attribute(XNamespace.Xml + "lang")?.Value, Is.EqualTo("en"));
            Assert.That(retainedBird.Attribute(foo + "qualifier")?.Value, Is.EqualTo("q"));
            Assert.That(TemporaryFiles(), Is.Empty);
        });
    }

    [Test]
    public async Task WriteAsync_WhenExpectedStateIsStale_ThrowsWithoutReplacingSidecar()
    {
        var mediaPath = CreateMediaFile("photo.nef", "raw");
        var initial = await _service.WriteAsync(mediaPath, new MediaAnnotations(1, ["first"]));
        var sidecarPath = Path.ChangeExtension(mediaPath, ".xmp");
        await File.AppendAllTextAsync(sidecarPath, " ");
        var externallyModified = await File.ReadAllTextAsync(sidecarPath);

        Assert.That(
            async () => await _service.WriteAsync(
                mediaPath,
                new MediaAnnotations(2, ["second"]),
                initial.FileState),
            Throws.TypeOf<MediaAnnotationsConcurrentModificationException>());
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(sidecarPath), Is.EqualTo(externallyModified));
            Assert.That(TemporaryFiles(), Is.Empty);
        });
    }

    [Test]
    public void ReadAsync_WhenSidecarContainsDtd_RejectsTheDocument()
    {
        var mediaPath = CreateMediaFile("photo.arw", "raw");
        File.WriteAllText(
            Path.ChangeExtension(mediaPath, ".xmp"),
            "<!DOCTYPE x [<!ENTITY value 'unsafe'>]><x>&value;</x>");

        Assert.That(
            async () => await _service.ReadAsync(mediaPath),
            Throws.TypeOf<XmlException>());
    }

    [Test]
    public async Task WriteAsync_WhenCancelledDuringSidecarWrite_LeavesOriginalAndRemovesTemporaryFile()
    {
        var mediaPath = CreateMediaFile("photo.dng", "raw");
        var sidecarPath = Path.ChangeExtension(mediaPath, ".xmp");
        await File.WriteAllTextAsync(sidecarPath, ExistingXmp);
        var original = await File.ReadAllBytesAsync(sidecarPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await _service.WriteAsync(
                mediaPath,
                new MediaAnnotations(4, ["changed"]),
                cancellationToken: cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(sidecarPath), Is.EqualTo(original));
            Assert.That(TemporaryFiles(), Is.Empty);
        });
    }

    [Test]
    public async Task ReadAsync_WithEmbeddedStandardXmp_ReadsRatingAndTagsAndIgnoresTrailingNulls()
    {
        var xmp = Encoding.UTF8.GetBytes(CreateXmp(3, "one", "two"));
        var packet = Encoding.ASCII.GetBytes(StandardXmpIdentifier)
            .Concat(xmp)
            .Concat(new byte[] { 0, 0 })
            .ToArray();
        var mediaPath = CreateJpeg("photo.jpg", Segment(0xE1, packet));

        var snapshot = await _service.ReadAsync(mediaPath);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Annotations.Rating, Is.EqualTo(3));
            Assert.That(snapshot.Annotations.Tags, Is.EqualTo(new[] { "one", "two" }));
            Assert.That(snapshot.FileState.StoragePath, Is.EqualTo(mediaPath));
            Assert.That(snapshot.FileState.StorageExists, Is.True);
        });
    }

    [Test]
    public async Task WriteAsync_WithJpeg_ReplacesStandardXmpAfterExifAndPreservesOtherSegmentsAndScanData()
    {
        var oldPacket = Encoding.ASCII.GetBytes(StandardXmpIdentifier)
            .Concat(Encoding.UTF8.GetBytes(CreateXmp(1, "old")))
            .ToArray();
        var app0 = Segment(0xE0, "JFIF\0"u8.ToArray());
        var exif = Segment(0xE1, "Exif\0\0metadata"u8.ToArray());
        var unrelated = Segment(0xE2, "preserve-me"u8.ToArray());
        var mediaPath = CreateJpeg("photo.jpeg", app0, Segment(0xE1, oldPacket), exif, unrelated);
        var originalWriteTime = DateTime.UtcNow.AddHours(-3);
        File.SetLastWriteTimeUtc(mediaPath, originalWriteTime);
        _fileSystem
            .Setup(fileSystem => fileSystem.CopyFileTimestampsAndAttributes(mediaPath, It.IsAny<string>()))
            .Callback((string source, string destination) =>
                File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source)));

        var snapshot = await _service.WriteAsync(mediaPath, new MediaAnnotations(4, ["new"]));

        var rewritten = await File.ReadAllBytesAsync(mediaPath);
        var identifier = Encoding.ASCII.GetBytes(StandardXmpIdentifier);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Annotations.Rating, Is.EqualTo(4));
            Assert.That(snapshot.Annotations.Tags, Is.EqualTo(new[] { "new" }));
            Assert.That(CountOccurrences(rewritten, identifier), Is.EqualTo(1));
            Assert.That(IndexOf(rewritten, "Exif\0\0metadata"u8.ToArray()), Is.LessThan(IndexOf(rewritten, identifier)));
            Assert.That(IndexOf(rewritten, identifier), Is.LessThan(IndexOf(rewritten, "preserve-me"u8.ToArray())));
            Assert.That(rewritten.AsSpan(rewritten.Length - ScanTail.Length).ToArray(), Is.EqualTo(ScanTail));
            Assert.That(File.GetLastWriteTimeUtc(mediaPath), Is.EqualTo(originalWriteTime).Within(TimeSpan.FromSeconds(1)));
            Assert.That(TemporaryFiles(), Is.Empty);
        });
        _fileSystem.Verify(
            fileSystem => fileSystem.CopyFileTimestampsAndAttributes(mediaPath, It.IsAny<string>()),
            Times.Once);
    }

    [Test]
    public void WriteAsync_WhenJpegIsMalformed_LeavesOriginalAndRemovesTemporaryFile()
    {
        var mediaPath = CreateMediaFile("broken.jpg", "not-a-jpeg");
        var original = File.ReadAllBytes(mediaPath);

        Assert.That(
            async () => await _service.WriteAsync(mediaPath, new MediaAnnotations(2, ["tag"])),
            Throws.TypeOf<InvalidDataException>());
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(mediaPath), Is.EqualTo(original));
            Assert.That(TemporaryFiles(), Is.Empty);
        });
    }

    [Test]
    public void WriteAsync_WhenEmbeddedPacketExceedsApp1Limit_ThrowsBeforeCreatingTemporaryFile()
    {
        var mediaPath = CreateJpeg("large.jpg");
        var oversizedTag = new string('x', ushort.MaxValue);

        Assert.That(
            async () => await _service.WriteAsync(mediaPath, new MediaAnnotations(1, [oversizedTag])),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(TemporaryFiles(), Is.Empty);
    }

    private string CreateMediaFile(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string CreateJpeg(string name, params byte[][] segments)
    {
        var path = Path.Combine(_directory, name);
        using var output = new MemoryStream();
        output.Write([0xFF, 0xD8]);
        foreach (var segment in segments)
        {
            output.Write(segment);
        }

        output.Write(Segment(0xDA, []));
        output.Write(ScanTail);
        File.WriteAllBytes(path, output.ToArray());
        return path;
    }

    private string[] TemporaryFiles() =>
        Directory.GetFiles(_directory, ".*.tmp", SearchOption.TopDirectoryOnly);

    private static byte[] Segment(byte marker, byte[] payload)
    {
        var length = payload.Length + 2;
        return [0xFF, marker, (byte)(length >> 8), (byte)length, .. payload];
    }

    private static int IndexOf(byte[] source, byte[] value) =>
        source.AsSpan().IndexOf(value);

    private static int CountOccurrences(byte[] source, byte[] value)
    {
        var count = 0;
        var remaining = source.AsSpan();
        while (true)
        {
            var index = remaining.IndexOf(value);
            if (index < 0)
            {
                return count;
            }

            count++;
            remaining = remaining[(index + value.Length)..];
        }
    }

    private static string CreateXmp(int rating, params string[] tags) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <x:xmpmeta xmlns:x="{XmpMetaNamespace}">
          <rdf:RDF xmlns:rdf="{RdfNamespace}">
            <rdf:Description rdf:about="" xmlns:xmp="{XmpNamespace}" xmlns:dc="{DcNamespace}" xmp:Rating="{rating}">
              <dc:subject><rdf:Bag>{string.Concat(tags.Select(tag => $"<rdf:li>{tag}</rdf:li>"))}</rdf:Bag></dc:subject>
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;

    private const string ExistingXmp = $"""
        <?xml version="1.0" encoding="utf-8"?>
        <x:xmpmeta xmlns:x="{XmpMetaNamespace}" xmlns:foo="urn:imagecare:test">
          <rdf:RDF xmlns:rdf="{RdfNamespace}">
            <rdf:Description rdf:about="" xmlns:xmp="{XmpNamespace}" xmlns:dc="{DcNamespace}" xmp:Rating="2" foo:keep="yes">
              <dc:subject><rdf:Bag><rdf:li xml:lang="en" foo:qualifier="q">Bird</rdf:li><foo:extra>keep</foo:extra></rdf:Bag></dc:subject>
              <foo:payload>keep</foo:payload>
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;

    private static readonly byte[] ScanTail = [1, 2, 0xFF, 0, 3, 0xFF, 0xD9];
}
