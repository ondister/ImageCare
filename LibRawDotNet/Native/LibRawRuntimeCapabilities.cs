namespace LibRawDotNet.Native;

[Flags]
public enum LibRawRuntimeCapabilities : uint
{
    LibRawCapsUndefined = 0,
    LibRawCapsRawSpeed = 1,
    LibRawCapsDngSdk = 2,
    LibRawCapsGprSdk = 4,
    LibRawCapsUnicodePaths = 8,
    LibRawCapsX3FTools = 16,
    LibRawCapsRpi6By9 = 32
}