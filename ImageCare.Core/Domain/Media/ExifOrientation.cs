namespace ImageCare.Core.Domain.Media;

public enum ExifOrientation
{
    Unknown = 0,
    TopLeft = 1,
    TopRight = 2,
    BottomRight = 3,
    BottomLeft = 4,
    LeftTop = 5,
    RightTop = 6,
    RightBottom = 7,
    LeftBottom = 8
}

public static class ExifOrientationExtensions
{

    public static double ToRotationAngle(this ExifOrientation imageOrientation)
    {
        switch (imageOrientation)
        {
            case ExifOrientation.Unknown:
                break;
            case ExifOrientation.TopLeft:
                break;
            case ExifOrientation.TopRight:
                break;
            case ExifOrientation.BottomRight:
                break;
            case ExifOrientation.BottomLeft:
                break;
            case ExifOrientation.LeftTop:
                break;
            case ExifOrientation.RightTop:
                return 90;
            case ExifOrientation.RightBottom:
                break;
            case ExifOrientation.LeftBottom:
                return 270;
            default:
                return 0;
        }

        return 0;
    }
}