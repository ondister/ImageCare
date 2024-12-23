namespace LibRawDotNet.Native;

[Flags]
public enum LibRawProgress
{
    LibRawProgressStart = 0,
    LibRawProgressOpen = 1,
    LibRawProgressIdentify = 1 << 1,
    LibRawProgressSizeAdjust = 1 << 2,
    LibRawProgressLoadRaw = 1 << 3,
    LibRawProgressRaw2Image = 1 << 4,
    LibRawProgressRemoveZeroes = 1 << 5,
    LibRawProgressBadPixels = 1 << 6,
    LibRawProgressDarkFrame = 1 << 7,
    LibRawProgressFoveonInterpolate = 1 << 8,
    LibRawProgressScaleColors = 1 << 9,
    LibRawProgressPreInterpolate = 1 << 10,
    LibRawProgressInterpolate = 1 << 11,
    LibRawProgressMixGreen = 1 << 12,
    LibRawProgressMedianFilter = 1 << 13,
    LibRawProgressHighlights = 1 << 14,
    LibRawProgressFujiRotate = 1 << 15,
    LibRawProgressFlip = 1 << 16,
    LibRawProgressApplyProfile = 1 << 17,
    LibRawProgressConvertRgb = 1 << 18,
    LibRawProgressStretch = 1 << 19,

    // Reserved.
    LibRawProgressStage20 = 1 << 20,
    LibRawProgressStage21 = 1 << 21,
    LibRawProgressStage22 = 1 << 22,
    LibRawProgressStage23 = 1 << 23,
    LibRawProgressStage24 = 1 << 24,
    LibRawProgressStage25 = 1 << 25,
    LibRawProgressStage26 = 1 << 26,
    LibRawProgressStage27 = 1 << 27,
    LibRawProgressThumbLoad = 1 << 28,
    LibRawProgressTreserved1 = 1 << 29,
    LibRawProgressTreserved2 = 1 << 30
}