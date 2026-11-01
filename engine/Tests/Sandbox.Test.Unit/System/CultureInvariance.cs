using System;
using System.Globalization;
using System.Text.Json;

namespace SystemTests;

/// <summary>
/// Values we write as text get read back invariantly, so they have to be written invariantly too.
/// These run under a comma-decimal culture (fr-FR, de-DE) and under tr-TR, where 'I' lowercases to 'ı'.
/// </summary>
[TestClass]
[DoNotParallelize] // Changes the current culture
public class CultureInvariance
{
	static void InCulture( string culture, Action action )
	{
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = new CultureInfo( culture );

		try
		{
			action();
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}
	}

	[TestMethod]
	[DataRow( "en-US" )]
	[DataRow( "fr-FR" )]
	[DataRow( "de-DE" )]
	[DataRow( "tr-TR" )]
	public void StringToNumberIsInvariant( string culture ) => InCulture( culture, () =>
	{
		Assert.AreEqual( 1.5f, "1.5".ToFloat() );
		Assert.AreEqual( 1.5f, (float)"1.5".ToType( typeof( float ) ) );
		Assert.AreEqual( 1.5m, "1.5".ToDecimal() );
		Assert.IsTrue( "0.5".ToBool() );
	} );

	[TestMethod]
	[DataRow( "en-US" )]
	[DataRow( "fr-FR" )]
	[DataRow( "de-DE" )]
	[DataRow( "tr-TR" )]
	public void MathTypesRoundTripThroughStrings( string culture ) => InCulture( culture, () =>
	{
		var v2 = new Vector2( 1.5f, -2.25f );
		var v3 = new Vector3( 1.5f, -2.25f, 3.125f );
		var v4 = new Vector4( 1.5f, -2.25f, 3.125f, 0.5f );

		Assert.AreEqual( "1.5,-2.25,3.125", v3.ToString() );
		Assert.AreEqual( v2, Vector2.Parse( v2.ToString() ) );
		Assert.AreEqual( v3, Vector3.Parse( v3.ToString() ) );
		Assert.AreEqual( v4, Vector4.Parse( v4.ToString() ) );
		Assert.AreEqual( v3, "1.5,-2.25,3.125".ToType( typeof( Vector3 ) ) );
	} );

	[TestMethod]
	[DataRow( "en-US" )]
	[DataRow( "fr-FR" )]
	[DataRow( "de-DE" )]
	[DataRow( "sv-SE" )]
	public void JsonRoundTripsThroughStrings( string culture ) => InCulture( culture, () =>
	{
		var color = new Color( 0.5f, 0.25f, 0.125f, 1.0f );
		var color32 = new Color32( 10, 20, 30, 40 );
		var vector = new Vector3Int( -1, 2, -3 );

		Assert.AreEqual( color, JsonSerializer.Deserialize<Color>( JsonSerializer.Serialize( color ) ) );
		Assert.AreEqual( color32, JsonSerializer.Deserialize<Color32>( JsonSerializer.Serialize( color32 ) ) );
		Assert.AreEqual( vector, JsonSerializer.Deserialize<Vector3Int>( JsonSerializer.Serialize( vector ) ) );
	} );

	[TestMethod]
	[DataRow( "en-US" )]
	[DataRow( "fr-FR" )]
	[DataRow( "tr-TR" )]
	public void ConVarValuesAreWrittenInvariantly( string culture ) => InCulture( culture, () =>
	{
		Assert.AreEqual( "0.5", ConVarSystem.ToInvariantString( 0.5f ) );
		Assert.AreEqual( "0.25", ConVarSystem.ToInvariantString( 0.25 ) );
		Assert.AreEqual( "True", ConVarSystem.ToInvariantString( true ) );
		Assert.IsNull( ConVarSystem.ToInvariantString( null ) );
	} );
}
