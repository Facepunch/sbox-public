namespace BorealisTests;

/// <summary>
/// An attribute set kept in managed code (a renderer's under -borealisgpu) gives back what was set on it, of every type its
/// getters read: a game reading a vector back gets the vector, not the default.
/// </summary>
[TestClass]
public class ManagedAttributeGetterTests
{
	[TestMethod]
	public void GettersReadTheManagedValues()
	{
		var set = RenderAttributes.CreateManaged();
		var matrix = Matrix.CreateTranslation( new Vector3( 1, 2, 3 ) );
		set.Set( "Origin", new Vector4( 191.5f, -43.25f, 17.5f, 255 ) );
		set.Set( "Direction", new Vector3( 0.5f, 0.25f, -1 ) );
		set.Set( "Turn", new Angles( 10, 20, 30 ) );
		set.Set( "Size", new Vector2( 13, 17 ) );
		set.Set( "Mask", 7u );
		set.Set( "Transform", matrix );
		set.Set( "Piece", 1.0f );

		Assert.AreEqual( new Vector4( 191.5f, -43.25f, 17.5f, 255 ), set.GetVector4( "Origin" ) );
		Assert.AreEqual( new Vector3( 0.5f, 0.25f, -1 ), set.GetVector( "Direction" ) );
		Assert.AreEqual( new Angles( 10, 20, 30 ), set.GetAngles( "Turn" ) );
		Assert.AreEqual( new Vector4( 13, 17, 0, 0 ), set.GetVector4( "Size" ) );
		Assert.AreEqual( new Vector4( 0.5f, 0.25f, -1, 0 ), set.GetVector4( "Direction" ) );
		Assert.AreEqual( 7u, set.GetUInt( "Mask" ) );
		Assert.AreEqual( matrix, set.GetMatrix( "Transform" ) );
		Assert.AreEqual( 1.0f, set.GetFloat( "Piece" ) );

		// Missing values, and values of another shape, give the default
		Assert.AreEqual( new Vector4( 1, 2, 3, 4 ), set.GetVector4( "Missing", new Vector4( 1, 2, 3, 4 ) ) );
		Assert.AreEqual( new Vector4( 9, 9, 9, 9 ), set.GetVector4( "Piece", new Vector4( 9, 9, 9, 9 ) ) );
	}

	[TestMethod]
	public void IntegerReadsKeepExactBitsThroughOverwriteAndMerge()
	{
		var source = RenderAttributes.CreateManaged();
		var destination = RenderAttributes.CreateManaged();
		const int roundedValue = 0x01000000;
		const int exactValue = roundedValue + 1;
		source.Set( "Value", roundedValue );
		source.MergeTo( destination );

		source.Set( "Value", exactValue );
		Assert.AreEqual( exactValue, source.GetInt( "Value" ) );
		Assert.AreEqual( (float)roundedValue, source.GetFloat( "Value" ), "The shader lowering's float projection remains unchanged." );
		Assert.AreEqual( roundedValue, destination.GetInt( "Value" ) );
		Assert.IsFalse( source.Managed.SameValues( destination.Managed ), "The values differ even though their float projections match." );

		source.Set( "Mask", uint.MaxValue );
		source.MergeTo( destination );
		Assert.AreEqual( exactValue, destination.GetInt( "Value" ) );
		Assert.AreEqual( uint.MaxValue, destination.GetUInt( "Mask" ) );

		destination.Set( "Value", (float)roundedValue );
		Assert.AreEqual( roundedValue, destination.GetInt( "Value" ), "A float write replaces any exact integer bits." );
	}
}
