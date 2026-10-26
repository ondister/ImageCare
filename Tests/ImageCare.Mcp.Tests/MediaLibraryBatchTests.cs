using System;

using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Services.FileSystemService;

using ImageCare.Mcp.Media;
using ImageCare.Mcp.Sessions;

using Moq;

using Serilog;

namespace ImageCare.Mcp.Tests;

[TestFixture]
public sealed class MediaLibraryBatchTests
{
    private static readonly DateTime BaseTime = new(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private string _directory = null!;
    private MediaLibrary _library = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = TestSupport.CreateTempDirectory();
        _library = new MediaLibrary(Mock.Of<IFileSystemService>(), new LoggerConfiguration().CreateLogger());
    }

    [TearDown]
    public void TearDown()
    {
        TestSupport.DeleteTempDirectory(_directory);
    }

    [Test]
    public void PackBatches_KeepsBurstsTogether()
    {
        var firstBurst = new[]
        {
            TestSupport.CreateMediaFile(_directory, "a1.jpg", BaseTime),
            TestSupport.CreateMediaFile(_directory, "a2.jpg", BaseTime.AddSeconds(10)),
            TestSupport.CreateMediaFile(_directory, "a3.jpg", BaseTime.AddSeconds(20))
        };

        var secondBurst = new[]
        {
            TestSupport.CreateMediaFile(_directory, "b1.jpg", BaseTime.AddHours(3)),
            TestSupport.CreateMediaFile(_directory, "b2.jpg", BaseTime.AddHours(3).AddSeconds(10)),
            TestSupport.CreateMediaFile(_directory, "b3.jpg", BaseTime.AddHours(3).AddSeconds(20))
        };

        var batches = _library.PackBatches([.. firstBurst, .. secondBurst], batchSize: 4);

        Assert.That(batches, Has.Count.EqualTo(2), "a burst must not be split to fill a batch");
        Assert.That(batches[0], Is.EquivalentTo(firstBurst));
        Assert.That(batches[1], Is.EquivalentTo(secondBurst));
    }

    [Test]
    public void PackBatches_ChunksOversizedCluster()
    {
        var burst = Enumerable.Range(0, 5)
            .Select(index => TestSupport.CreateMediaFile(
                _directory,
                $"c{index}.jpg",
                BaseTime.AddSeconds(index * 10)))
            .ToArray();

        var batches = _library.PackBatches(burst, batchSize: 2);

        Assert.That(batches.Select(batch => batch.Count), Is.EqualTo(new[] { 2, 2, 1 }));
        Assert.That(batches.SelectMany(batch => batch), Is.EqualTo(burst));
    }

    [Test]
    public void PackBatches_DoesNotLoseIsolatedFiles()
    {
        var burst = new[]
        {
            TestSupport.CreateMediaFile(_directory, "d1.jpg", BaseTime),
            TestSupport.CreateMediaFile(_directory, "d2.jpg", BaseTime.AddSeconds(10))
        };

        var isolated = new[]
        {
            TestSupport.CreateMediaFile(_directory, "d3.jpg", BaseTime.AddHours(5)),
            TestSupport.CreateMediaFile(_directory, "d4.jpg", BaseTime.AddHours(10))
        };

        var all = burst.Concat(isolated).ToArray();
        var batches = _library.PackBatches(all, batchSize: 2);

        Assert.That(batches.SelectMany(batch => batch), Is.EquivalentTo(all));
        Assert.That(batches, Has.Count.EqualTo(2));
    }

    [Test]
    public void PackBatches_ReturnsEmptyForEmptyInput()
    {
        Assert.That(_library.PackBatches([], batchSize: 10), Is.Empty);
    }

    [Test]
    public void EnumerateMedia_IgnoresUnsupportedExtensionsAndFolderTags()
    {
        TestSupport.CreateMediaFile(_directory, "keep.jpg", BaseTime);
        TestSupport.CreateMediaFile(_directory, "keep.cr3", BaseTime);
        TestSupport.CreateMediaFile(_directory, "keep.dng", BaseTime);
        File.WriteAllText(Path.Combine(_directory, "notes.txt"), "not media");
        File.WriteAllText(Path.Combine(_directory, "folder-tags.txt"), "wildlife");

        var files = _library.EnumerateMedia(_directory, recursive: false);

        Assert.That(
            files.Select(Path.GetFileName),
            Is.EquivalentTo(new[] { "keep.cr3", "keep.dng", "keep.jpg" }));
    }

    [TestCase("photo.dng")]
    [TestCase("photo.DNG")]
    public void ResolveFormat_WithDngFile_ReturnsDngFormat(string fileName)
    {
        var result = ReviewSessionService.ResolveFormat(fileName);

        Assert.That(result, Is.EqualTo(MediaFormat.MediaFormatDng));
    }

    [Test]
    public void ResolveFormat_WithUnsupportedFile_ReturnsUnknownFormat()
    {
        var result = ReviewSessionService.ResolveFormat("photo.tiff");

        Assert.That(result, Is.EqualTo(MediaFormat.MediaFormatUnknown));
    }
}
