using ImageCare.Core.Domain.Annotations;

namespace ImageCare.Core.Tests.Domain.Annotations;

[TestFixture]
public class FolderTagsInheritanceTests
{
    private static Func<string, IReadOnlyList<string>?> Reader(
        IReadOnlyDictionary<string, string[]> descriptors) =>
        directory => descriptors.TryGetValue(directory, out var tags) ? tags : null;

    [Test]
    public void Resolve_UsesStartingDirectoryTags_WhenItIsTheOnlyDescriptor()
    {
        var descriptors = new Dictionary<string, string[]>
        {
            [@"C:\Photo\Birds\Bee-eater"] = ["золотистая щурка"],
        };

        var result = FolderTagsInheritance.Resolve(@"C:\Photo\Birds\Bee-eater", Reader(descriptors));

        Assert.That(result, Is.EqualTo(new[] { "золотистая щурка" }));
    }

    [Test]
    public void Resolve_UnionsAllAncestors_NearestFirst()
    {
        var descriptors = new Dictionary<string, string[]>
        {
            [@"C:\Photo"] = ["птицы"],
            [@"C:\Photo\Birds"] = ["группа"],
            [@"C:\Photo\Birds\Bee-eater"] = ["золотистая щурка"],
        };

        // Variant B: every ancestor descriptor contributes, nearest first.
        var result = FolderTagsInheritance.Resolve(@"C:\Photo\Birds\Bee-eater", Reader(descriptors));

        Assert.That(result, Is.EqualTo(new[] { "золотистая щурка", "группа", "птицы" }));
    }

    [Test]
    public void Resolve_CollectsAncestors_WhenStartingDirectoryHasNoDescriptor()
    {
        var descriptors = new Dictionary<string, string[]>
        {
            [@"C:\Photo\Birds"] = ["птицы"],
            [@"C:\Photo\Birds\Bee-eater"] = ["золотистая щурка"],
        };

        var result = FolderTagsInheritance.Resolve(
            @"C:\Photo\Birds\Bee-eater\2024-06",
            Reader(descriptors));

        Assert.That(result, Is.EqualTo(new[] { "золотистая щурка", "птицы" }));
    }

    [Test]
    public void Resolve_SkipsDirectoriesWithoutDescriptors()
    {
        var descriptors = new Dictionary<string, string[]>
        {
            [@"C:\Photo"] = ["дикая природа"],
        };

        var result = FolderTagsInheritance.Resolve(
            @"C:\Photo\Birds\Bee-eater\2024-06",
            Reader(descriptors));

        Assert.That(result, Is.EqualTo(new[] { "дикая природа" }));
    }

    [Test]
    public void Resolve_ReturnsEmpty_WhenNoDescriptorExistsUpToBoundary()
    {
        var descriptors = new Dictionary<string, string[]>
        {
            [@"C:\"] = ["слишком высоко"],
        };

        var result = FolderTagsInheritance.Resolve(
            @"C:\Photo\Birds",
            Reader(descriptors),
            boundaryPath: @"C:\Photo");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Resolve_IncludesBoundaryTags_ButNotAbove()
    {
        var descriptors = new Dictionary<string, string[]>
        {
            [@"C:\Photo"] = ["архив"],
            [@"C:\Photo\Birds"] = ["птицы"],
        };

        var result = FolderTagsInheritance.Resolve(
            @"C:\Photo\Birds\Bee-eater",
            Reader(descriptors),
            boundaryPath: @"C:\Photo");

        Assert.That(result, Is.EqualTo(new[] { "птицы", "архив" }));
    }

    [Test]
    public void Resolve_IgnoresEmptyDescriptorAndContinuesUpwards()
    {
        var descriptors = new Dictionary<string, string[]>
        {
            [@"C:\Photo\Birds\Bee-eater"] = [],
            [@"C:\Photo\Birds"] = ["птицы"],
        };

        var result = FolderTagsInheritance.Resolve(@"C:\Photo\Birds\Bee-eater", Reader(descriptors));

        Assert.That(result, Is.EqualTo(new[] { "птицы" }));
    }

    [Test]
    public void Resolve_DeduplicatesCaseInsensitively_PreservingNearestOccurrence()
    {
        var descriptors = new Dictionary<string, string[]>
        {
            [@"C:\Photo\Birds"] = ["Птицы"],
            [@"C:\Photo\Birds\Bee-eater"] = ["птицы", "щурка"],
        };

        var result = FolderTagsInheritance.Resolve(@"C:\Photo\Birds\Bee-eater", Reader(descriptors));

        Assert.That(result, Is.EqualTo(new[] { "птицы", "щурка" }));
    }
}
