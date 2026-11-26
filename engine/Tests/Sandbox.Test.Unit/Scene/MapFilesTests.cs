namespace SceneTests;

/// <summary>
/// Checks which files each kind of map loads from, and how a Workshop item's map is found.
/// </summary>
[TestClass]
public class MapFilesTests
{
	/// <summary>
	/// Scenes may ship as source or compiled assets. Hammer maps require the VPK for the selected map.
	/// </summary>
	[TestMethod]
	[DataRow( "test.scene", "test.scene", true )]
	[DataRow( "test.scene", "test.scene_c", true )]
	[DataRow( "test.scene", "other.scene_c", false )]
	[DataRow( "test.vmap", "test.vpk", true )]
	[DataRow( "test.vpk", "test.vpk", true )]
	[DataRow( "test.vmap", "test.vmap", false )]
	[DataRow( "test.vpk", "other.vpk", false )]
	public async Task FindsSelectedMapInItsFileSystem( string selection, string shippedFile, bool expected )
	{
		var map = new MapReference( $"maps/{selection}" );
		Assert.AreEqual( expected, await map.LaunchAsync( joining: false, FilesWith( shippedFile ), default ) );
		var info = await map.GetInfoAsync();
		Assert.AreEqual( "test", info.Title );
		Assert.IsNull( info.Thumb );
		Assert.IsNull( info.Thumbnail );
	}

	/// <summary>
	/// A Workshop item's map is the primary asset its metadata names, as long as the item has it.
	/// </summary>
	[TestMethod]
	[DataRow( "maps/test.vmap", "maps/test.vmap" )]
	[DataRow( "maps/test.vpk", "maps/test.vpk" )]
	[DataRow( "maps/missing.vmap", null )]
	[DataRow( "../maps/test.vmap", null )]
	[DataRow( "maps/test.txt", null )]
	public void WorkshopItemUsesTheAssetItsMetadataNames( string primaryAsset, string expected )
	{
		var meta = $"{{\"PrimaryAsset\":\"{primaryAsset}\"}}";

		Assert.AreEqual( expected, WorkshopItems.FindPrimaryAsset( FilesWith( "test.vpk", "test.txt" ), meta ) );
	}

	/// <summary>
	/// An item that doesn't name its map uses a scene if it has one, then its shortest-named VPK so a skybox VPK is skipped.
	/// </summary>
	[TestMethod]
	public void WorkshopItemWithoutMetadataFindsItsMap()
	{
		Assert.AreEqual( "maps/test.vpk", WorkshopItems.FindPrimaryAsset( FilesWith( "test_skybox.vpk", "test.vpk" ), null ) );
		Assert.AreEqual( "maps/test.scene", WorkshopItems.FindPrimaryAsset( FilesWith( "a.vpk", "test.scene_c" ), null ) );
		Assert.AreEqual( "maps/test.scene", WorkshopItems.FindPrimaryAsset( FilesWith( "test.scene" ), "{}" ) );
		Assert.IsNull( WorkshopItems.FindPrimaryAsset( FilesWith( "readme.txt" ), null ) );
	}

	static MemoryFileSystem FilesWith( params string[] shippedFiles )
	{
		var files = new MemoryFileSystem();
		files.CreateDirectory( "maps" );

		foreach ( var file in shippedFiles )
			files.WriteAllText( $"maps/{file}", "" );

		return files;
	}
}
