using ImageCare.Core.Domain.Annotations;

namespace ImageCare.Core.Tests.Domain.Annotations;

[TestFixture]
public class FolderTagApplicationLogTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "ImageCareFolderTagLog", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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
    public void Append_WritesDailyFile_WithAllFields()
    {
        var log = new FolderTagApplicationLog(_root);

        log.Append("move", @"C:\Photo\shot.cr3", @"C:\Photo\Birds", ["птицы", "щурка"]);

        var files = Directory.GetFiles(_root, "tags-*.log");
        Assert.That(files, Has.Length.EqualTo(1));
        var content = File.ReadAllText(files[0]);
        Assert.That(content, Does.Contain("move"));
        Assert.That(content, Does.Contain(@"C:\Photo\shot.cr3"));
        Assert.That(content, Does.Contain(@"C:\Photo\Birds"));
        Assert.That(content, Does.Contain("птицы, щурка"));
    }

    [Test]
    public void Append_DoesNothing_WhenNoTagsAdded()
    {
        var log = new FolderTagApplicationLog(_root);

        log.Append("move", @"C:\Photo\shot.cr3", @"C:\Photo\Birds", []);

        Assert.That(Directory.GetFiles(_root, "tags-*.log"), Is.Empty);
    }

    [Test]
    public void Append_PrunesFilesOlderThanRetentionWindow()
    {
        var today = DateTime.Now.Date;
        var stale = Path.Combine(_root, $"tags-{today.AddDays(-(FolderTagApplicationLog.RetentionDays + 1)):yyyyMMdd}.log");
        var recent = Path.Combine(_root, $"tags-{today.AddDays(-1):yyyyMMdd}.log");
        File.WriteAllText(stale, "old");
        File.WriteAllText(recent, "recent");

        var log = new FolderTagApplicationLog(_root);
        log.Append("manual", @"C:\Photo\shot.cr3", @"C:\Photo\Birds", ["птицы"]);

        Assert.That(File.Exists(stale), Is.False);
        Assert.That(File.Exists(recent), Is.True);
    }
}
