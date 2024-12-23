namespace LibRawDotNet.Native;

[Flags]
public enum LibRawInitFlags : uint
{
    LibRawOptionsNone = 0,
    LibRawOptionsNoMemErrCallback = 1,
    LibRawOptionsNoDataErrCallback = 1 << 1
}