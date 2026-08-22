using Sandbox.UI;

namespace UITests.Parsers;

/// <summary>
/// CSS measures gradient angles clockwise from "to top"; the box gradient paths measure
/// clockwise from "to bottom". These pin the conversion, because a mirrored gradient still
/// looks plausible - it just points the wrong way - and that hid the bug for a long time.
/// </summary>
[TestClass]
public class GradientAngleParseTest
{
	/// <summary>The parsed background gradient's angle, in degrees of the engine's convention.</summary>
	static float ParseAngleDegrees( string gradient )
	{
		var sheet = StyleParser.ParseSheet( $".class{{ background-image: {gradient}; }}" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );

		var info = sheet.Nodes[0].Styles.BackgroundGradient;

		Assert.IsFalse( info.ColorOffsets.IsDefaultOrEmpty, $"'{gradient}' didn't parse to a shader gradient" );

		return info.Angle.RadianToDegree();
	}

	[DataTestMethod]
	// 0 is "to bottom" for us, so CSS angles come out mirrored: 0 <-> 180, 90 and 270 stay.
	[DataRow( "0deg", 180f )]
	[DataRow( "90deg", 90f )]
	[DataRow( "180deg", 0f )]
	[DataRow( "270deg", 270f )]
	[DataRow( "45deg", 135f )]
	// Wrapped rather than left negative - the baked path writes through background-angle,
	// which rejects negatives.
	[DataRow( "360deg", 180f )]
	[DataRow( "-90deg", 270f )]
	public void NumericAngle( string angle, float expected )
	{
		Assert.AreEqual( expected, ParseAngleDegrees( $"linear-gradient( {angle}, red, blue )" ), 0.01f );
	}

	[DataTestMethod]
	[DataRow( "to top", 180f )]
	[DataRow( "to right", 90f )]
	[DataRow( "to bottom", 0f )]
	[DataRow( "to left", 270f )]
	public void KeywordDirection( string direction, float expected )
	{
		Assert.AreEqual( expected, ParseAngleDegrees( $"linear-gradient( {direction}, red, blue )" ), 0.01f );
	}

	/// <summary>No angle means "to bottom", same as the web.</summary>
	[TestMethod]
	public void DefaultsToBottom()
	{
		Assert.AreEqual( 0f, ParseAngleDegrees( "linear-gradient( red, blue )" ), 0.01f );
	}

	/// <summary>"to bottom" and "180deg" are the same gradient - they used to disagree.</summary>
	[TestMethod]
	public void KeywordMatchesEquivalentNumber()
	{
		Assert.AreEqual(
			ParseAngleDegrees( "linear-gradient( to bottom, red, blue )" ),
			ParseAngleDegrees( "linear-gradient( 180deg, red, blue )" ),
			0.01f );

		Assert.AreEqual(
			ParseAngleDegrees( "linear-gradient( to right, red, blue )" ),
			ParseAngleDegrees( "linear-gradient( 90deg, red, blue )" ),
			0.01f );
	}

	static GradientInfo ParseBackgroundGradient( string gradient )
	{
		var sheet = StyleParser.ParseSheet( $".class{{ background-image: {gradient}; }}" );

		Assert.IsNotNull( sheet );

		var info = sheet.Nodes[0].Styles.BackgroundGradient;

		Assert.IsFalse( info.ColorOffsets.IsDefaultOrEmpty, $"'{gradient}' didn't parse to a shader gradient" );

		return info;
	}

	/// <summary>Centres are kept as Lengths so the shader can resolve percentages against the box.</summary>
	static void AssertCenter( GradientInfo info, float x, float y )
	{
		Assert.AreEqual( x, info.OffsetX.Value, 0.01f, "centre x" );
		Assert.AreEqual( y, info.OffsetY.Value, 0.01f, "centre y" );
	}

	[TestMethod]
	public void RadialDefaults()
	{
		var info = ParseBackgroundGradient( "radial-gradient( red, blue )" );

		Assert.AreEqual( GradientInfo.GradientTypes.Radial, info.GradientType );
		// CSS defaults to an ellipse reaching the farthest corner, centred.
		Assert.AreEqual( GradientInfo.RadialSizeMode.FarthestCorner, info.SizeMode );
		Assert.IsFalse( info.Circle );
		AssertCenter( info, 50f, 50f );
	}

	// Passed as ints because GradientInfo is internal and the test class isn't.
	[DataTestMethod]
	[DataRow( "closest-side", (int)GradientInfo.RadialSizeMode.ClosestSide )]
	[DataRow( "closest-corner", (int)GradientInfo.RadialSizeMode.ClosestCorner )]
	[DataRow( "farthest-side", (int)GradientInfo.RadialSizeMode.FarthestSide )]
	[DataRow( "farthest-corner", (int)GradientInfo.RadialSizeMode.FarthestCorner )]
	public void RadialSize( string keyword, int expected )
	{
		Assert.AreEqual( (GradientInfo.RadialSizeMode)expected, ParseBackgroundGradient( $"radial-gradient( {keyword}, red, blue )" ).SizeMode );
	}

	[TestMethod]
	public void RadialShapeAndPosition()
	{
		var info = ParseBackgroundGradient( "radial-gradient( circle closest-corner at 20% 80%, red, blue )" );

		Assert.IsTrue( info.Circle );
		Assert.AreEqual( GradientInfo.RadialSizeMode.ClosestCorner, info.SizeMode );
		AssertCenter( info, 20f, 80f );
	}

	/// <summary>Keywords in either order, and a missing axis stays centred.</summary>
	[DataTestMethod]
	[DataRow( "at left top", 0f, 0f )]
	[DataRow( "at top left", 0f, 0f )]
	[DataRow( "at right bottom", 100f, 100f )]
	[DataRow( "at center", 50f, 50f )]
	[DataRow( "at 30%", 30f, 50f )]
	public void RadialPosition( string position, float x, float y )
	{
		AssertCenter( ParseBackgroundGradient( $"radial-gradient( {position}, red, blue )" ), x, y );
	}

	/// <summary>
	/// More stops than the shader can hold used to fall back to a baked texture. Now the
	/// extras are simply dropped, so every gradient stays on the shader path.
	/// </summary>
	[TestMethod]
	public void TooManyStopsAreClamped()
	{
		var many = string.Join( ", ", Enumerable.Range( 0, 12 ).Select( i => $"#{i:x}{i:x}{i:x}" ) );

		var info = ParseBackgroundGradient( $"linear-gradient( {many} )" );

		Assert.AreEqual( GradientInfo.MaxStops, info.ColorOffsets.Length );
	}

	[TestMethod]
	public void ConicDefaults()
	{
		var info = ParseBackgroundGradient( "conic-gradient( red, blue )" );

		Assert.AreEqual( GradientInfo.GradientTypes.Conic, info.GradientType );
		Assert.AreEqual( 0f, info.Angle, 0.01f );
		AssertCenter( info, 50f, 50f );
	}

	/// <summary>
	/// A conic's "from" rotates the sweep rather than pointing it, so unlike a linear
	/// angle it is NOT mirrored - 180deg has to stay 180deg.
	/// </summary>
	[DataTestMethod]
	[DataRow( "0deg", 0f )]
	[DataRow( "90deg", 90f )]
	[DataRow( "180deg", 180f )]
	public void ConicFromAngleIsNotMirrored( string angle, float expected )
	{
		var info = ParseBackgroundGradient( $"conic-gradient( from {angle}, red, blue )" );

		Assert.AreEqual( expected, info.Angle.RadianToDegree(), 0.01f );
	}

	[TestMethod]
	public void ConicFromAndPosition()
	{
		var info = ParseBackgroundGradient( "conic-gradient( from 45deg at 25% 75%, red, blue )" );

		Assert.AreEqual( 45f, info.Angle.RadianToDegree(), 0.01f );
		AssertCenter( info, 25f, 75f );
	}

	// Masks aren't covered here on purpose: a mask gradient always bakes to a Texture,
	// which needs a graphics device this test tier doesn't have. They go through the same
	// TryParseAngle as the cases above, so the conversion is already pinned.

	/// <summary>Text gradients keep CSS degrees - RichTextKit's own rotation is already spec-correct.</summary>
	static float ParseTextAngleDegrees( string gradient )
	{
		var sheet = StyleParser.ParseSheet( $".class{{ color: {gradient}; }}" );

		Assert.IsNotNull( sheet );

		var info = sheet.Nodes[0].Styles.TextGradient;

		Assert.IsFalse( info.ColorOffsets.IsDefaultOrEmpty, $"'{gradient}' didn't parse to a text gradient" );

		return info.Angle;
	}

	[DataTestMethod]
	[DataRow( "to top", 0f )]
	[DataRow( "to right", 90f )]
	[DataRow( "to bottom", 180f )]
	[DataRow( "45deg", 45f )]
	public void TextGradientAngle( string angle, float expected )
	{
		Assert.AreEqual( expected, ParseTextAngleDegrees( $"linear-gradient( {angle}, red, blue )" ), 0.01f );
	}

	/// <summary>An omitted angle is "to bottom" here too - it used to point the opposite way.</summary>
	[TestMethod]
	public void TextGradientDefaultsToBottom()
	{
		Assert.AreEqual( 180f, ParseTextAngleDegrees( "linear-gradient( red, blue )" ), 0.01f );
	}
}
