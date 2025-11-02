using System.Globalization;

namespace ImageCare.Core.Domain.Media.Metadata;

public class Location : IEquatable<Location>
{
	public Location(double longitude, double latitude, double altitude)
	{
		// Round precision to 1 meter
		Longitude = Math.Round(longitude, 5);
		Latitude = Math.Round(latitude, 5);
		Altitude = Math.Round(altitude, 5);
	}

	public static Location Empty { get; } = new(-1.0, -1.0, -1.0);

	public double Longitude { get; }

	public double Latitude { get; }

	public double Altitude { get; }

	public bool Equals(Location? other)
	{
		if (ReferenceEquals(null, other))
		{
			return false;
		}

		if (ReferenceEquals(this, other))
		{
			return true;
		}

		return Longitude.Equals(other.Longitude)
		    && Latitude.Equals(other.Latitude)
		    && Altitude.Equals(other.Altitude);
	}

	public override bool Equals(object? obj)
	{
		if (ReferenceEquals(null, obj))
		{
			return false;
		}

		if (ReferenceEquals(this, obj))
		{
			return true;
		}

		if (obj.GetType() != GetType())
		{
			return false;
		}

		return Equals((Location)obj);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Longitude, Latitude, Altitude);
	}

	public static bool operator ==(Location? left, Location? right)
	{
		return Equals(left, right);
	}

	public static bool operator !=(Location? left, Location? right)
	{
		return !Equals(left, right);
	}

	public override string ToString()
	{
		return $"{Latitude.ToString(CultureInfo.InvariantCulture)}, {Longitude.ToString(CultureInfo.InvariantCulture)}";
	}
}