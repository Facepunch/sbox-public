using System;

namespace PackageTests;

/// <summary>
/// Fetches real packages of every type via package/get and makes sure the returned DTO
/// deserializes cleanly. These guard against serialization mismatches between the website
/// (which writes the package metadata) and the engine (which reads it) - e.g. a metadata
/// field whose JSON shape one side doesn't understand. Such a bug used to only surface if
/// a test happened to download a package of the affected type.
///
/// Note: these hit the live API, so a package being deleted will fail the test - prefer
/// stable facepunch packages where one exists for the type.
/// </summary>
[TestClass]
[TestCategory( "LiveBackend" )]
public class PackageParsingTest
{
	/// <summary>
	/// A model package - exercises ModelMetaData, including the BoundsMin/BoundsMax vectors
	/// which the website serializes as "x,y,z" strings.
	/// </summary>
	[TestMethod]
	public async Task ParseModelPackage()
	{
		var package = await Package.FetchAsync( "facepunch.watermelon", false );

		Assert.IsNotNull( package, "Failed to fetch model package" );

		var meta = package.Metadata as Package.ModelMetaData;
		Assert.IsNotNull( meta, "Expected ModelMetaData" );

		// Bounds should have parsed from the "x,y,z" string into a real vector, not zero.
		Assert.AreNotEqual( Vector3.Zero, meta.BoundsMin, "BoundsMin didn't parse" );
		Assert.AreNotEqual( Vector3.Zero, meta.BoundsMax, "BoundsMax didn't parse" );
	}

	/// <summary>
	/// A material package - exercises MaterialMetaData.
	/// </summary>
	[TestMethod]
	public async Task ParseMaterialPackage()
	{
		var package = await Package.FetchAsync( "facepunch.glass_a", false );

		Assert.IsNotNull( package, "Failed to fetch material package" );
		Assert.IsInstanceOfType( package.Metadata, typeof( Package.MaterialMetaData ), "Expected MaterialMetaData" );
	}

	/// <summary>
	/// A clothing package - exercises ClothingMetaData.
	/// </summary>
	[TestMethod]
	public async Task ParseClothingPackage()
	{
		var package = await Package.FetchAsync( "ducksworkshop.mobbosstop", false );

		Assert.IsNotNull( package, "Failed to fetch clothing package" );
		Assert.IsInstanceOfType( package.Metadata, typeof( Package.ClothingMetaData ), "Expected ClothingMetaData" );
	}

	/// <summary>
	/// Fetch a spread of packages across every type and make sure each one deserializes without
	/// throwing. FetchAsync swallows network/not-found errors (returns null) but lets a JSON
	/// deserialization exception propagate, so a parse regression fails this test.
	/// </summary>
	[TestMethod]
	[DataRow( "facepunch.watermelon" )]        // model
	[DataRow( "facepunch.oildrumexplosive" )]  // model
	[DataRow( "facepunch.glass_a" )]           // material
	[DataRow( "facepunch.metal_01a" )]         // material
	[DataRow( "ducksworkshop.mobbosstop" )]    // clothing
	[DataRow( "lies.swagchain" )]              // clothing
	[DataRow( "facepunch.flatgrass" )]         // map
	[DataRow( "facepunch.sandbox" )]           // game
	[DataRow( "facepunch.shatterglass" )]      // library
	[DataRow( "igrotronika.explosion2" )]      // sound
	[DataRow( "wmfg.powerkart" )]              // prefab
	public async Task FetchAndParse( string ident )
	{
		var package = await Package.FetchAsync( ident, false );

		Assert.IsNotNull( package, $"Failed to fetch/parse {ident}" );
		Console.WriteLine( $"{ident} -> {package.TypeName}, metadata: {package.Metadata?.GetType().Name ?? "none"}" );
	}
}
