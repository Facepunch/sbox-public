namespace SceneTests;

/// <summary>
/// Map strings retain their saved identity unless a published identifier resolves to a Workshop item.
/// </summary>
[TestClass]
public class MapReferenceTests
{
	/// <summary>
	/// Files, mounted scenes and Workshop IDs support joining without a download. Invalid selections stay unloadable.
	/// </summary>
	[TestMethod]
	[DataRow( "3819030036", null, true )]
	[DataRow( "flatgrass.scene", "flatgrass", true )]
	[DataRow( "maps/sub/flatgrass.scene", "flatgrass", true )]
	[DataRow( "maps/flatgrass.VMAP", "flatgrass", true )]
	[DataRow( "maps/flatgrass.vpk", "flatgrass", true )]
	[DataRow( "mount://hl2/maps/canals.scene", "canals", true )]
	[DataRow( "mount://hl2", null, false )]
	[DataRow( "0", null, false )]
	[DataRow( "flatgrass", null, false )]
	[DataRow( "/maps/flatgrass.scene", null, false )]
	[DataRow( "../maps/flatgrass.scene", null, false )]
	[DataRow( "C:/maps/flatgrass.vpk", null, false )]
	[DataRow( "maps/flatgrass.scene_c", null, false )]
	[DataRow( "", null, false )]
	[DataRow( " ", null, false )]
	[DataRow( null, null, false )]
	public async Task JoinMap( string text, string name, bool valid )
	{
		var map = await MapReference.ResolveAsync( text, _ => throw new AssertFailedException( "Unexpected identifier lookup" ) );
		Assert.AreEqual( valid, await map.LaunchAsync( joining: true, null, default ) );
		Assert.AreEqual( text, map.Text );
		Assert.AreEqual( name, map.ShortName );
		Assert.AreEqual( text == "3819030036" ? 3819030036ul : 0ul, map.WorkshopId );
		var isGameFile = valid && name is not null && !text.StartsWith( "mount://" );
		Assert.AreEqual( isGameFile, MapReference.IsAssetPath( text ) );
		Assert.AreEqual( isGameFile ? text : null, map.Asset );
		Assert.AreEqual( text?.Contains( ':' ) == false ? text : "", map.ServerTag );
		Assert.IsNull( map.Title );
	}

	/// <summary>
	/// The backend receives org.name for every spelling. Unpublished identifiers keep their original text.
	/// </summary>
	[TestMethod]
	[DataRow( "facepunch.flatgrass", 42ul )]
	[DataRow( "facepunch/flatgrass", 42ul )]
	[DataRow( "https://sbox.game/facepunch/flatgrass", 42ul )]
	[DataRow( "facepunch.flatgrass#12", 0ul )]
	public async Task ResolveIdentifier( string text, ulong id )
	{
		var map = await MapReference.ResolveAsync( text, ident =>
		{
			Assert.AreEqual( "facepunch.flatgrass", ident );
			return Task.FromResult( id );
		} );

		Assert.AreEqual( id, map.WorkshopId );
		Assert.AreEqual( id == 0 ? text : id.ToString(), map.Text );
		Assert.AreEqual( id == 0 ? "flatgrass" : null, map.ShortName );
		Assert.AreEqual( id != 0, await map.LaunchAsync( joining: true, null, default ) );
	}
}
