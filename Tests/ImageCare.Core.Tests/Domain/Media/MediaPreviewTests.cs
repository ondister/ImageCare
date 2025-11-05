using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Preview;

namespace ImageCare.Core.Tests.Domain.Media;

[TestFixture]
public class MediaPreviewTests
{
	[Test]
	public void Constructor_WithValidParameters_ShouldSetProperties()
	{
		var title = "Test Title";
		var url = "https://example.com/image.jpg";
		var mediaFormat = MediaFormat.MediaFormatJpg;
		var maxImageHeight = 800;

		var mediaPreview = new MediaPreview(title, url, mediaFormat, maxImageHeight);

		Assert.That(mediaPreview.Title, Is.EqualTo(title));
		Assert.That(mediaPreview.Url, Is.EqualTo(url));
		Assert.That(mediaPreview.MediaFormat, Is.EqualTo(mediaFormat));
		Assert.That(mediaPreview.MaxImageHeight, Is.EqualTo(maxImageHeight));
	}

	[Test]
	public void Constructor_WithNullTitle_ShouldSetTitleToNull()
	{
		string? title = null;
		var url = "https://example.com/image.jpg";
		var mediaFormat = MediaFormat.MediaFormatJpg;
		var maxImageHeight = 600;

		var mediaPreview = new MediaPreview(title, url, mediaFormat, maxImageHeight);

		Assert.That(mediaPreview.Title, Is.Null);
		Assert.That(mediaPreview.Url, Is.EqualTo(url));
		Assert.That(mediaPreview.MediaFormat, Is.EqualTo(mediaFormat));
		Assert.That(mediaPreview.MaxImageHeight, Is.EqualTo(maxImageHeight));
	}

	[Test]
	public void Empty_Property_ShouldReturnCorrectEmptyInstance()
	{
		var empty = MediaPreview.Empty;

		Assert.That(empty.Title, Is.EqualTo(string.Empty));
		Assert.That(empty.Url, Is.EqualTo(string.Empty));
		Assert.That(empty.MediaFormat, Is.EqualTo(MediaFormat.MediaFormatUnknown));
		Assert.That(empty.MaxImageHeight, Is.EqualTo(0));
	}

	[Test]
	public void Equals_WithSameUrl_ShouldReturnTrue()
	{
		var preview1 = new MediaPreview("Title 1", "https://example.com/image.jpg", MediaFormat.MediaFormatJpg, 800);
		var preview2 = new MediaPreview("Title 2", "https://example.com/image.jpg", MediaFormat.MediaFormatJpg, 600);

		Assert.That(preview1.Equals(preview2), Is.True);
		Assert.That(preview1 == preview2, Is.True);
	}

	[Test]
	public void Equals_WithDifferentUrls_ShouldReturnFalse()
	{
		var preview1 = new MediaPreview("Title", "https://example.com/image1.jpg", MediaFormat.MediaFormatJpg, 800);
		var preview2 = new MediaPreview("Title", "https://example.com/image2.jpg", MediaFormat.MediaFormatJpg, 800);

		Assert.That(preview1.Equals(preview2), Is.False);
		Assert.That(preview1 != preview2, Is.True);
	}

	[Test]
	public void Equals_WithNull_ShouldReturnFalse()
	{
		var preview = new MediaPreview("Title", "https://example.com/image.jpg", MediaFormat.MediaFormatJpg, 800);

		Assert.That(preview.Equals(null), Is.False);
		Assert.That(preview == null, Is.False);
		Assert.That(null == preview, Is.False);
	}

	[Test]
	public void Equals_WithSameReference_ShouldReturnTrue()
	{
		var preview = new MediaPreview("Title", "https://example.com/image.jpg", MediaFormat.MediaFormatJpg, 800);

		Assert.That(preview.Equals(preview), Is.True);
		Assert.That(preview == preview, Is.True);
	}

	[Test]
	public void Equals_WithDifferentType_ShouldReturnFalse()
	{
		var preview = new MediaPreview("Title", "https://example.com/image.jpg", MediaFormat.MediaFormatJpg, 800);
		var obj = new object();

		Assert.That(preview.Equals(obj), Is.False);
	}

	[Test]
	public void GetHashCode_ForSameUrl_ShouldReturnSameValue()
	{
		var url = "https://example.com/image.jpg";
		var preview1 = new MediaPreview("Title 1", url, MediaFormat.MediaFormatJpg, 800);
		var preview2 = new MediaPreview("Title 2", url, MediaFormat.MediaFormatJpg, 600);

		Assert.That(preview1.GetHashCode(), Is.EqualTo(preview2.GetHashCode()));
	}

	[Test]
	public void GetHashCode_ForDifferentUrls_ShouldReturnDifferentValues()
	{
		var preview1 = new MediaPreview("Title", "https://example.com/image1.jpg", MediaFormat.MediaFormatJpg, 800);
		var preview2 = new MediaPreview("Title", "https://example.com/image2.jpg", MediaFormat.MediaFormatJpg, 800);

		Assert.That(preview1.GetHashCode(), Is.Not.EqualTo(preview2.GetHashCode()));
	}

	[Test]
	public void Empty_Comparison_WithEmptyInstance_ShouldReturnTrue()
	{
		var empty1 = MediaPreview.Empty;
		var empty2 = MediaPreview.Empty;

		Assert.That(empty1.Equals(empty2), Is.True);
		Assert.That(empty1 == empty2, Is.True);
		Assert.That(empty1 == MediaPreview.Empty, Is.True);
	}

	[Test]
	public void Empty_Comparison_WithEmptyUrlInstance_ShouldReturnTrue()
	{
		var empty1 = MediaPreview.Empty;
		var empty2 = new MediaPreview("empty", string.Empty, MediaFormat.MediaFormatUnknown, 0);

		Assert.That(empty1.Equals(empty2), Is.True);
		Assert.That(empty1 == empty2, Is.True);
		Assert.That(empty1 == MediaPreview.Empty, Is.True);
	}

	[Test]
	public void Empty_Comparison_WithSelectedPreviewEmptyUrlInstance_ShouldReturnTrue()
	{
		var empty1 = MediaPreview.Empty;
		var empty2 = new SelectedMediaPreview("empty", string.Empty, MediaFormat.MediaFormatUnknown, 0, FileManagerPanel.Right);

		Assert.That(empty1.Equals(empty2), Is.True);
		Assert.That(empty1 == empty2, Is.True);
		Assert.That(empty1 == MediaPreview.Empty, Is.True);
	}

	[Test]
	public void Empty_Comparison_WithNonEmptyInstance_ShouldReturnFalse()
	{
		var empty = MediaPreview.Empty;
		var nonEmpty = new MediaPreview("Title", "https://example.com/image.jpg", MediaFormat.MediaFormatJpg, 800);

		Assert.That(empty.Equals(nonEmpty), Is.False);
		Assert.That(empty == nonEmpty, Is.False);
		Assert.That(nonEmpty == empty, Is.False);
	}

	[Test]
	public void OperatorEquals_WithBothNull_ShouldReturnTrue()
	{
		MediaPreview? left = null;
		MediaPreview? right = null;

		Assert.That(left == right, Is.True);
	}

	[Test]
	public void OperatorNotEquals_WithBothNull_ShouldReturnFalse()
	{
		MediaPreview? left = null;
		MediaPreview? right = null;

		Assert.That(left != right, Is.False);
	}
}