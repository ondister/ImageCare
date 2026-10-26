using ImageCare.Core.Domain.Configuration;

using ImageCareConfiguration = ImageCare.Core.Domain.Configuration.Configuration;

namespace ImageCare.Core.Tests.Domain.Configuration;

[TestFixture]
public class DefaultFileApplicationAssociationsTests
{
    [Test]
    public void EnsureInitialized_AddsDngAssociationsAndUpdatesVersion()
    {
        var configuration = new ImageCareConfiguration();

        var changed = DefaultFileApplicationAssociations.EnsureInitialized(configuration);

        Assert.That(changed, Is.True);
        Assert.That(
            configuration.ApplicationAssociationsDefaultsVersion,
            Is.EqualTo(DefaultFileApplicationAssociations.CurrentVersion));
        Assert.That(
            configuration.ApplicationAssociationPairs.Any(pair =>
                pair.FileExtension.Equals(".dng", StringComparison.OrdinalIgnoreCase)),
            Is.True);
    }

    [Test]
    public void EnsureInitialized_WhenCurrentVersion_DoesNotChangeConfiguration()
    {
        var configuration = new ImageCareConfiguration
        {
            ApplicationAssociationsDefaultsVersion = DefaultFileApplicationAssociations.CurrentVersion
        };

        var changed = DefaultFileApplicationAssociations.EnsureInitialized(configuration);

        Assert.That(changed, Is.False);
        Assert.That(configuration.ApplicationAssociationPairs, Is.Empty);
    }
}
