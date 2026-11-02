using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.Folders;
using ImageCare.UI.Avalonia.ViewModels;
using ImageCare.UI.Avalonia.ViewModels.Domain;

namespace ImageCare.UI.Avalonia.Tests.ViewModels;

[TestFixture]
public sealed class MediaFolderFileIndexTests
{
    [Test]
    public void Reset_ClearsFilesAndUsesNewComparer()
    {
        var index = CreateIndex(MediaSortDirection.OldestFirst);
        index.AddIfMissing(new[]
        {
            File("b.jpg", 2),
            File("a.jpg", 1)
        });

        index.Reset(new FileModelCreationDateTimeComparer(MediaSortDirection.NewestFirst));
        index.AddIfMissing(new[]
        {
            File("a.jpg", 1),
            File("b.jpg", 2)
        });

        Assert.That(Paths(index), Is.EqualTo(new[] { "b.jpg", "a.jpg" }));
    }

    [Test]
    public void Snapshot_ReturnsDetachedOrderedCopy()
    {
        var index = CreateIndex(MediaSortDirection.OldestFirst);
        index.AddIfMissing(new[]
        {
            File("third.jpg", 3),
            File("first.jpg", 1),
            File("second.jpg", 2)
        });

        var snapshot = index.Snapshot();
        snapshot[0] = File("replacement.jpg", 10);

        Assert.That(Paths(index), Is.EqualTo(new[] { "first.jpg", "second.jpg", "third.jpg" }));
    }

    [Test]
    public void AddIfMissing_DeduplicatesExistingAndBatchPaths()
    {
        var index = CreateIndex(MediaSortDirection.OldestFirst);
        Assert.That(index.AddIfMissing(File("known.jpg", 1)), Is.True);

        var added = index.AddIfMissing(new[]
        {
            File("known.jpg", 2),
            File("new.jpg", 3),
            File("new.jpg", 4)
        });

        Assert.Multiple(() =>
        {
            Assert.That(added.Select(file => file.FullName), Is.EqualTo(new[] { "new.jpg" }));
            Assert.That(Paths(index), Is.EqualTo(new[] { "known.jpg", "new.jpg" }));
            Assert.That(index.AddIfMissing(File("known.jpg", 5)), Is.False);
        });
    }

    [TestCase(true, 1)]
    [TestCase(false, 2)]
    public void AddIfMissing_UsesConfiguredPathIdentity(bool ignoreCase, int expectedCount)
    {
        var pathComparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var index = CreateIndex(MediaSortDirection.OldestFirst, pathComparer);

        index.AddIfMissing(File("Photo.jpg", 1));
        index.AddIfMissing(File("photo.jpg", 2));

        Assert.That(index.Snapshot(), Has.Length.EqualTo(expectedCount));
    }

    [Test]
    public void Remove_UsesConfiguredPathIdentity()
    {
        var index = CreateIndex(MediaSortDirection.OldestFirst, StringComparer.OrdinalIgnoreCase);
        index.AddIfMissing(File("Photo.jpg", 1));

        var removed = index.Remove("photo.jpg");

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(index.Snapshot(), Is.Empty);
            Assert.That(index.Remove("PHOTO.JPG"), Is.False);
        });
    }

    [Test]
    public void ChangeComparer_ResortsExistingAndFutureFiles()
    {
        var index = CreateIndex(MediaSortDirection.OldestFirst);
        index.AddIfMissing(new[]
        {
            File("first.jpg", 1),
            File("second.jpg", 2)
        });

        index.ChangeComparer(new FileModelCreationDateTimeComparer(MediaSortDirection.NewestFirst));
        index.AddIfMissing(File("third.jpg", 3));

        Assert.That(Paths(index), Is.EqualTo(new[] { "third.jpg", "second.jpg", "first.jpg" }));
    }

    [Test]
    public void FindStaleAndMissing_ReturnsDifferenceWithoutMutatingIndex()
    {
        var index = CreateIndex(MediaSortDirection.OldestFirst);
        index.AddIfMissing(new[]
        {
            File("kept.jpg", 1),
            File("stale.jpg", 2)
        });

        var difference = index.FindStaleAndMissing(new[]
        {
            File("kept.jpg", 3),
            File("missing.jpg", 4),
            File("missing.jpg", 5)
        });

        Assert.Multiple(() =>
        {
            Assert.That(difference.StalePaths, Is.EqualTo(new[] { "stale.jpg" }));
            Assert.That(difference.MissingFiles.Select(file => file.FullName),
                Is.EqualTo(new[] { "missing.jpg" }));
            Assert.That(Paths(index), Is.EqualTo(new[] { "kept.jpg", "stale.jpg" }));
        });
    }

    [Test]
    public async Task ConcurrentMutationsAndSnapshots_DoNotCorruptOrderingOrUniqueness()
    {
        var index = CreateIndex(MediaSortDirection.OldestFirst);
        var tasks = Enumerable.Range(0, 8)
            .Select(worker => Task.Run(() =>
            {
                for (var item = 0; item < 100; item++)
                {
                    var path = $"{item % 25:D2}.jpg";
                    index.AddIfMissing(File(path, item % 25));
                    if ((worker + item) % 7 == 0)
                    {
                        index.Remove(path);
                    }

                    _ = index.Snapshot();
                }
            }));

        await Task.WhenAll(tasks);
        index.ChangeComparer(new FileModelCreationDateTimeComparer(MediaSortDirection.OldestFirst));
        var snapshot = index.Snapshot();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Select(file => file.FullName).Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(snapshot.Length));
            Assert.That(snapshot.Select(file => file.CreatedDateTime), Is.Ordered.Ascending);
        });
    }

    private static MediaFolderFileIndex CreateIndex(
        MediaSortDirection direction,
        IEqualityComparer<string>? pathComparer = null) =>
        new(new FileModelCreationDateTimeComparer(direction), pathComparer);

    private static FileModel File(string path, int day) =>
        new(path, path, new DateTime(2025, 1, day + 1));

    private static string[] Paths(MediaFolderFileIndex index) =>
        index.Snapshot().Select(file => file.FullName).ToArray();
}
