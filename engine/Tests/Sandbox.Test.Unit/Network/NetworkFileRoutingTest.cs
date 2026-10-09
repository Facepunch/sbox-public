namespace NetworkTests;

// Issue #11195: small compiled shaders (.shader_c < 64KB) took the in-memory small-file path and
// failed to load on joining clients (ERROR_FILEOPEN). Native-loaded formats must always go large.
[TestClass]
public class NetworkFileRoutingTest
{
	[TestMethod]
	public void SmallShaderUsesLargeDownload()
	{
		Assert.IsTrue( GameInstanceDll.ShouldUseLargeDownload( "shaders/toon_postprocess.shader_c", 1024 ) );
	}

	[DataTestMethod]
	[DataRow( 0L )]
	[DataRow( 1695L )]
	[DataRow( 65535L )]
	public void SmallPhysicsUsesLargeDownload( long size )
	{
		Assert.IsTrue( GameInstanceDll.ShouldUseLargeDownload( "scenes/world_scene_data/compiled/generation/collision_0.vphys_c", size ) );
	}

	[TestMethod]
	public void SmallNonEngineFileUsesSmallDownload()
	{
		Assert.IsFalse( GameInstanceDll.ShouldUseLargeDownload( "styles/menu.scss", 1024 ) );
	}

	[TestMethod]
	public void LargeFileUsesLargeDownload()
	{
		Assert.IsTrue( GameInstanceDll.ShouldUseLargeDownload( "styles/menu.scss", 1024 * 64 ) );
	}
}
