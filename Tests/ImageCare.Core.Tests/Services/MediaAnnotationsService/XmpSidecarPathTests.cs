using ImageCare.Core.Services.MediaAnnotationsService;

namespace ImageCare.Core.Tests.Services.MediaAnnotationsService;

[TestFixture]
public class XmpSidecarPathTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"imagecare-sidecar-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
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
    public void GetExternalSidecarPath_WithDngFile_ReturnsBaseNameSidecar()
    {
        var mediaPath = Path.Combine(_directory, "photo.dng");

        var result = XmpSidecarPath.GetExternalSidecarPath(mediaPath);

        Assert.That(result, Is.EqualTo(Path.Combine(_directory, "photo.xmp")));
        Assert.That(XmpSidecarPath.UsesEmbeddedXmp(mediaPath), Is.False);
        Assert.That(XmpSidecarPath.UsesBaseNameSidecar(mediaPath), Is.True);
    }

    [Test]
    public void GetDestinationExternalSidecarPath_WithDngFile_ReturnsBaseNameSidecar()
    {
        var sourcePath = Path.Combine(_directory, "source.dng");
        var destinationPath = Path.Combine(_directory, "destination.dng");

        var result = XmpSidecarPath.GetDestinationExternalSidecarPath(sourcePath, destinationPath);

        Assert.That(result, Is.EqualTo(Path.Combine(_directory, "destination.xmp")));
    }

    [Test]
    public void GetExistingExternalSidecarPath_WithDngFile_ReturnsSidecarWhenPresent()
    {
        var mediaPath = Path.Combine(_directory, "photo.dng");
        var sidecarPath = Path.Combine(_directory, "photo.xmp");
        File.WriteAllText(mediaPath, "raw");
        File.WriteAllText(sidecarPath, "sidecar");

        var result = XmpSidecarPath.GetExistingExternalSidecarPath(mediaPath);

        Assert.That(result, Is.EqualTo(sidecarPath));
    }

    [Test]
    public void EnsureUnambiguousRawOwner_WithDngAndCr3SharingBaseName_Throws()
    {
        var dngPath = Path.Combine(_directory, "photo.dng");
        var cr3Path = Path.Combine(_directory, "photo.CR3");
        File.WriteAllText(dngPath, "dng");
        File.WriteAllText(cr3Path, "cr3");

        Assert.Throws<RawSidecarAmbiguityException>(() => XmpSidecarPath.EnsureUnambiguousRawOwner(dngPath));
        Assert.That(XmpSidecarPath.HasAnotherRawOwner(dngPath), Is.True);
    }
}
