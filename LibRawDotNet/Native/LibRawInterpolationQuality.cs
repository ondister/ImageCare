namespace LibRawDotNet.Native;

[Flags]
public enum LibRawInterpolationQuality
{
    Linear = 0,
    Vng = 1,
    Ppg = 2,
    Ahd = 3,
    Dcb = 4,
    Dht = 11,
    ModifiedAhd = 12
}