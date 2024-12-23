namespace LibRawDotNet.Native;

[Flags]
public enum LibRawOutputColor
{
    Raw = 0,
    Srgb = 1,
    Adobe = 2,
    Wide = 3,
    Prophoto = 4,
    Xyz = 5,
    Aces = 6
}