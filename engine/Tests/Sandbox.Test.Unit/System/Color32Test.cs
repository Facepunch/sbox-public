namespace SystemTests;

[TestClass]
public class Color32Test
{
	/// <summary>
	/// Constructors should populate the channels, with alpha defaulting
	/// to fully opaque.
	/// </summary>
	[TestMethod]
	public void Construct()
	{
		var c = new Color32( 10, 20, 30 );

		Assert.AreEqual( 10, c.r );
		Assert.AreEqual( 20, c.g );
		Assert.AreEqual( 30, c.b );
		Assert.AreEqual( 255, c.a );

		var translucent = new Color32( 1, 2, 3, 4 );
		Assert.AreEqual( 4, translucent.a );
	}

	/// <summary>
	/// FromRgb and FromRgba should unpack the channels from the packed
	/// big-endian representation.
	/// </summary>
	[TestMethod]
	public void FromPacked()
	{
		var rgb = Color32.FromRgb( 0xFF8000 );
		Assert.AreEqual( new Color32( 255, 128, 0 ), rgb );

		var rgba = Color32.FromRgba( 0xFF800040 );
		Assert.AreEqual( new Color32( 255, 128, 0, 0x40 ), rgba );
	}

	/// <summary>
	/// The packed integer properties should round-trip with FromRgb/FromRgba.
	/// </summary>
	[TestMethod]
	public void PackedRoundTrip()
	{
		var c = new Color32( 12, 34, 56, 78 );

		Assert.AreEqual( c, Color32.FromRgba( c.RgbaInt ) );
		Assert.AreEqual( c.RgbInt, Color32.FromRgb( c.RgbInt ).RgbInt );
	}

	/// <summary>
	/// Hex should format as #RRGGBB for opaque colors and append alpha
	/// when translucent.
	/// </summary>
	[TestMethod]
	public void HexFormat()
	{
		Assert.AreEqual( "#FF8000", new Color32( 255, 128, 0 ).Hex );
		Assert.AreEqual( "#FF800040", new Color32( 255, 128, 0, 0x40 ).Hex );
	}

	/// <summary>
	/// Parse should accept space or comma separated byte components, with
	/// an optional alpha, and reject other shapes.
	/// </summary>
	[TestMethod]
	public void Parse()
	{
		Assert.AreEqual( new Color32( 1, 2, 3 ), Color32.Parse( "1 2 3" ) );
		Assert.AreEqual( new Color32( 1, 2, 3, 4 ), Color32.Parse( "1,2,3,4" ) );
		Assert.IsNull( Color32.Parse( "1 2" ) );
	}

	/// <summary>
	/// Lerp should return the end points at 0 and 1 and the midpoint at 0.5.
	/// </summary>
	[TestMethod]
	public void Lerp()
	{
		var black = new Color32( 0, 0, 0 );
		var white = new Color32( 255, 255, 255 );

		Assert.AreEqual( black, Color32.Lerp( black, white, 0 ) );
		Assert.AreEqual( white, Color32.Lerp( black, white, 1 ) );

		var mid = Color32.Lerp( black, white, 0.5f );
		Assert.IsTrue( mid.r is >= 126 and <= 129, $"midpoint red was {mid.r}" );
	}

	/// <summary>
	/// Min and Max should operate per channel.
	/// </summary>
	[TestMethod]
	public void MinMax()
	{
		var a = new Color32( 10, 200, 30, 255 );
		var b = new Color32( 100, 20, 40, 0 );

		Assert.AreEqual( new Color32( 10, 20, 30, 0 ), Color32.Min( a, b ) );
		Assert.AreEqual( new Color32( 100, 200, 40, 255 ), Color32.Max( a, b ) );
	}

	/// <summary>
	/// Converting to Color and back should preserve the channels.
	/// </summary>
	[TestMethod]
	public void ColorRoundTrip()
	{
		var c = new Color32( 12, 34, 56, 78 );

		Color32 back = c.ToColor();

		Assert.AreEqual( c, back );
	}
}
