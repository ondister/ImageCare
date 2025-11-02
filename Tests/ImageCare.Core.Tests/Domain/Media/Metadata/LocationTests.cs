using ImageCare.Core.Domain.Media.Metadata;

namespace ImageCare.Core.Tests.Domain.Media.Metadata;

[TestFixture]
public class LocationTests
{
	[Test]
	public void Constructor_RoundsCoordinatesToFiveDecimalPlaces()
	{
		var longitude = 12.3456789;
		var latitude = 98.7654321;
		var altitude = 123.456789;

		var location = new Location(longitude, latitude, altitude);

		Assert.That(location.Longitude, Is.EqualTo(12.34568));
		Assert.That(location.Latitude, Is.EqualTo(98.76543));
		Assert.That(location.Altitude, Is.EqualTo(123.45679));
	}

	[Test]
	public void Equals_WithSameRoundedValues_ReturnsTrue()
	{
		var location1 = new Location(12.345678, 98.765432, 123.456789);
		var location2 = new Location(12.345679, 98.765433, 123.456788);

		Assert.That(location1.Equals(location2), Is.True);
		Assert.That(location1 == location2, Is.True);
	}

	[Test]
	public void Equals_WithDifferentValues_ReturnsFalse()
	{
		var location1 = new Location(12.34567, 98.76543, 123.45678);
		var location2 = new Location(12.34568, 98.76543, 123.45678);

		Assert.That(location1.Equals(location2), Is.False);
		Assert.That(location1 != location2, Is.True);
	}

	[Test]
	public void GetHashCode_ForSameRoundedValues_IsEqual()
	{
		var location1 = new Location(12.345678, 98.765432, 123.456789);
		var location2 = new Location(12.345679, 98.765433, 123.456788);

		Assert.That(location1.GetHashCode(), Is.EqualTo(location2.GetHashCode()));
	}

	[Test]
	public void Empty_HasExpectedValues()
	{
		Assert.That(Location.Empty.Longitude, Is.EqualTo(-1.0));
		Assert.That(Location.Empty.Latitude, Is.EqualTo(-1.0));
		Assert.That(Location.Empty.Altitude, Is.EqualTo(-1.0));
	}

	[Test]
	public void ToString_ReturnsExpectedFormat()
	{
		var location = new Location(12.34567, 98.76543, 123.45678);

		var result = location.ToString();

		Assert.That(result, Is.EqualTo("98.76543, 12.34567"));
	}
}