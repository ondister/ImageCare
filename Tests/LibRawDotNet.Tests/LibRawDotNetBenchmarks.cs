using BenchmarkDotNet.Attributes;

namespace LibRawDotNet.Tests;

public class LibRawDotNetBenchmarks
{
    private const string _canonCr3FilePath = @"Resources\Raws\CanonCr3\IMG_0529.CR3";

    [Benchmark]
    public async Task GetPreviewJpegStreamForSmallPreview()
    {
        using (var libRawData = LibRawData.OpenFile(_canonCr3FilePath))
        {
            _ = await libRawData.GetPreviewJpegStream(0);
        }
    }

    [Benchmark]
    public async Task GetPreviewJpegStreamForMediumPreview()
    {
        using (var libRawData = LibRawData.OpenFile(_canonCr3FilePath))
        {
            _ = await libRawData.GetPreviewJpegStream(1);
        }
    }

    [Benchmark]
    public async Task GetPreviewJpegStreamForLargePreview()
    {
        using (var libRawData = LibRawData.OpenFile(_canonCr3FilePath))
        {
            _ = await libRawData.GetPreviewJpegStream(2);
        }
    }
}