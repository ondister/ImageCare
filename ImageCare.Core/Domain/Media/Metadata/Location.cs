namespace ImageCare.Core.Domain.Media.Metadata;

public class Location : IEquatable<Location>
{
	public Location(double longitude, double latitude, double altitude)
	{
		Longitude = longitude;
		Latitude = latitude;
		Altitude = altitude;
	}

	public static Location Empty { get; } = new(-1.0, -1.0, -1.0);

	public double Longitude { get; }

	public double Latitude { get; }

	public double Altitude { get; }

	/// <inheritdoc />
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

	/// <inheritdoc />
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

	/// <inheritdoc />
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

	/// <inheritdoc />
	public override string ToString()
	{
		return $" {Latitude}, {Longitude}";
	}
}