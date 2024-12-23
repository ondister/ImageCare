namespace LibRawDotNet.Native;

[Flags]
public enum LibRawErrors
{
    LibRawSuccess = 0,
    LibRawUnspecifiedError = -1,
    LibRawFileUnsupported = -2,
    LibRawRequestForNonexistentImage = -3,
    LibRawOutOfOrderCall = -4,
    LibRawNoThumbnail = -5,
    LibRawUnsupportedThumbnail = -6,
    LibRawInputClosed = -7,
    LibRawNotImplemented = -8,
    LibRawInsufficientMemory = -100007,
    LibRawDataError = -100008,
    LibRawIoError = -100009,
    LibRawCancelledByCallback = -100010,
    LibRawBadCrop = -100011,
    LibRawTooBig = -100012,
    LibRawMemPoolOverflow = -100013
}