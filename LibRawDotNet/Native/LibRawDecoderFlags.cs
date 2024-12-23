namespace LibRawDotNet.Native;

[Flags]
public enum LibRawDecoderFlags : uint
{
    LibRawDecoderHasCurve = 1 << 4,
    LibRawDecoderSonyArw2 = 1 << 5,
    LibRawDecoderTryRawSpeed = 1 << 6,
    LibRawDecoderOwnAlloc = 1 << 7,
    LibRawDecoderFixedMaxc = 1 << 8,
    LLibRawDecoderAdobeCopyPixel = 1 << 9,
    LibRawDecoderLegacyWithMargins = 1 << 10,
    LibRawDecoder3Channel = 1 << 11,
    LibRawDecoderSinar4Shot = 1 << 11,
    LibRawDecoderFlatData = 1 << 12,
    LibRawDecoderFlatBg2Swapped = 1 << 13,
    LibRawDecoderNotSet = 1 << 15
}