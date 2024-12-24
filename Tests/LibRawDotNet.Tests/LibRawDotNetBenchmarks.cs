using BenchmarkDotNet.Attributes;

namespace LibRawDotNet.Tests;

public class LibRawDotNetBenchmarks
{
    private const string _canonCr3FilePath = @"Resources\Raws\CanonCr3\IMG_0529.CR3";

    [Benchmark]
    public void GetPreviewJpegStreamForSmallPreview()
    {
        using (var libRawData = LibRawData.OpenFile(_canonCr3FilePath))
        {
            _ = libRawData.GetPreviewJpegStream(0);
        }
    }

    [Benchmark]
    public void GetPreviewJpegStreamForMediumPreview()
    {
        using (var libRawData = LibRawData.OpenFile(_canonCr3FilePath))
        {
            _ = libRawData.GetPreviewJpegStream(1);
        }
    }

    [Benchmark]
    public void GetPreviewJpegStreamForLargePreview()
    {
        using (var libRawData = LibRawData.OpenFile(_canonCr3FilePath))
        {
            _ = libRawData.GetPreviewJpegStream(2);
        }
    }
}