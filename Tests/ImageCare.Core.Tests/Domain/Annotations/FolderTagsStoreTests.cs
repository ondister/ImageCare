using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Services.FileSystemService;

using Moq;

namespace ImageCare.Core.Tests.Domain.Annotations;

[TestFixture]
public class FolderTagsStoreTests
{
    private string _root = null!;
    private FolderTagsStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "ImageCareFolderTagsStore", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var fileSystemService = new Mock<IFileSystemService>();
        fileSystemService.Setup(x => x.FileExists(It.IsAny<string>())).Returns<string>(File.Exists);
        fileSystemService.Setup(x => x.CreateDirectory(It.IsAny<string>()))
            .Callback<string>(path => Directory.CreateDirectory(path));
        fileSystemService.Setup(x => x.OpenRead(It.IsAny<string>()))
            .Returns<string>(path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        fileSystemService.Setup(x => x.DeleteFile(It.IsAny<string>())).Callback<string>(File.Delete);

        _store = new FolderTagsStore(fileSystemService.Object);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void Read_ReturnsNull_WhenDescriptorMissing()
    {
        Assert.That(_store.Read(_root), Is.Null);
        Assert.That(_store.HasDescriptor(_root), Is.False);
    }

    [Test]
    public async Task WriteAsync_RoundTripsTags()
    {
        await _store.WriteAsync(_root, ["золотистая щурка", "птицы"]);

        Assert.That(_store.HasDescriptor(_root), Is.True);
        Assert.That(_store.Read(_root), Is.EqualTo(new[] { "золотистая щурка", "птицы" }));
    }

    [Test]
    public async Task WriteAsync_WritesUtf8Bom()
    {
        await _store.WriteAsync(_root, ["щурка"]);

        var bytes = await File.ReadAllBytesAsync(_store.GetDescriptorPath(_root));
        Assert.That(bytes, Has.Length.GreaterThanOrEqualTo(3));
        Assert.That(bytes[0], Is.EqualTo(0xEF));
        Assert.That(bytes[1], Is.EqualTo(0xBB));
        Assert.That(bytes[2], Is.EqualTo(0xBF));
    }

    [Test]
    public async Task WriteAsync_RemovesDescriptor_WhenNoTagsRemain()
    {
        await _store.WriteAsync(_root, ["птицы"]);
        await _store.WriteAsync(_root, []);

        Assert.That(_store.HasDescriptor(_root), Is.False);
    }
}
