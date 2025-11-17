namespace LibRawDotNet.Tests;

public class LibRawDataTests
{
	private const string _jpegFilePath = @"Resources\Jpeg\IMG_0529.jpg";
	private const string _canonCr3FilePath = @"Resources\Raws\CanonCr3\IMG_0529.CR3";

	[SetUp]
	public void Setup() { }

	[Test]
	public void LibRawDataTests_CreateInstanceFromJpeg_NotSupportedExceptionThrown()
	{
		Assert.Throws<NotSupportedException>(() =>
		{
			using (var libRawData = LibRawData.OpenFile(_jpegFilePath)) { }
		});
	}

	[Test]
	public void LibRawDataTests_CreateInstanceFromCanonCr3_NoErrors()
	{
		Assert.DoesNotThrow(() =>
		{
			using (var libRawData = LibRawData.OpenFile(_canonCr3FilePath)) { }
		});
	}

	[Test]
	[TestCase(0)]
	[TestCase(1)]
	[TestCase(2)]
	public void LibRawDataTests_GetPreviewJpegStream_WithRightIndex_StreamIsNotNull(int index)
	{
		using (var libRawData = LibRawData.OpenFile(_canonCr3FilePath))
		{
			var stream = libRawData.GetPreviewJpegStream(index);
            Assert.That(stream, Is.Not.Null);
        }
	}

	[Test]
	public void LibRawDataTests_GetPreviewJpegStream_WithWrongIndex_ThrownLibRawException()
	{
		const int wrongPreviewIndex = 4;

		Assert.Throws<LibRawException>(() =>
		{
			using (var libRawData = LibRawData.OpenFile(_canonCr3FilePath))
			{
				_ = libRawData.GetPreviewJpegStream(wrongPreviewIndex);
			}
		});
	}
}