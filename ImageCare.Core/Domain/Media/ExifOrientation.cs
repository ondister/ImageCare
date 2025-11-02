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
		return imageOrientation switch
		{
			ExifOrientation.TopLeft => 0, // Normal
			ExifOrientation.TopRight => 0, // Mirror horizontal (flip handled separately)
			ExifOrientation.BottomRight => 180, // Rotate 180°
			ExifOrientation.BottomLeft => 180, // Mirror horizontal + rotate 180°
			ExifOrientation.LeftTop => 270, // Mirror horizontal + rotate 270° CW
			ExifOrientation.RightTop => 90, // Rotate 90° CW
			ExifOrientation.RightBottom => 90, // Mirror horizontal + rotate 90° CW
			ExifOrientation.LeftBottom => 270, // Rotate 270° CW (90° CCW)
			_ => 0
		};
	}
}