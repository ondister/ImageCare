using ImageCare.Core.Domain.Annotations;

namespace ImageCare.Core.Tests.Domain.Annotations;

[TestFixture]
public class MediaAnnotationsTagQueryTests
{
	[TestCase("золотистая щурка", "щурка")]
	[TestCase("золотистая щурка", "золотистая")]
	[TestCase("золотистая щурка", "золотистая щурка")]
	[TestCase("золотистая щурка", "щурка золотистая")]
	[TestCase("Golden-Bee-eater", "bee")]
	[TestCase("Golden_Bee_eater", "golden eater")]
	[TestCase("Bird / Птица", "птица bird")]
	public void MatchesTagQuery_ReturnsTrue_WhenTokensAppearRegardlessOfOrder(string tag, string query)
	{
		Assert.That(MediaAnnotations.MatchesTagQuery(tag, query), Is.True);
	}

	[TestCase("золотистая щурка", "орёл")]
	[TestCase("золотистая щурка", "щурка орёл")]
	[TestCase("", "щурка")]
	public void MatchesTagQuery_ReturnsFalse_WhenTagDoesNotContainEveryToken(string tag, string query)
	{
		Assert.That(MediaAnnotations.MatchesTagQuery(tag, query), Is.False);
	}

	[Test]
	public void MatchesTagQuery_ReturnsTrue_WhenQueryIsEmpty()
	{
		Assert.That(MediaAnnotations.MatchesTagQuery("золотистая щурка", "   "), Is.True);
	}
}
