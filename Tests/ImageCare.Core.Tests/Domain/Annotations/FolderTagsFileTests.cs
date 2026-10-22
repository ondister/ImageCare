using ImageCare.Core.Domain.Annotations;

namespace ImageCare.Core.Tests.Domain.Annotations;

[TestFixture]
public class FolderTagsFileTests
{
    [Test]
    public void Parse_IgnoresBlankLinesAndComments()
    {
        var content = "# Птицы\n\nзолотистая щурка\n   \n# ещё комментарий\nптицы\n";

        var tags = FolderTagsFile.Parse(content);

        Assert.That(tags, Is.EqualTo(new[] { "золотистая щурка", "птицы" }));
    }

    [Test]
    public void Parse_KeepsCommasInsideTags()
    {
        var tags = FolderTagsFile.Parse("Золотистая щурка, обыкновенная\nMerops apiaster\n");

        Assert.That(tags, Is.EqualTo(new[] { "Золотистая щурка, обыкновенная", "Merops apiaster" }));
    }

    [Test]
    public void Parse_DeduplicatesCaseInsensitively_PreservingFirstOccurrence()
    {
        var tags = FolderTagsFile.Parse("Птицы\nптицы\nПТИЦЫ\n");

        Assert.That(tags, Is.EqualTo(new[] { "Птицы" }));
    }

    [Test]
    public void Parse_ReturnsEmpty_ForNullContent()
    {
        Assert.That(FolderTagsFile.Parse(null), Is.Empty);
    }

    [Test]
    public void Serialize_RoundTripsThroughParse()
    {
        var original = new[] { "золотистая щурка", "Merops apiaster", "птицы" };

        var serialized = FolderTagsFile.Serialize(original);
        var parsed = FolderTagsFile.Parse(serialized);

        Assert.That(parsed, Is.EqualTo(original));
    }
}
