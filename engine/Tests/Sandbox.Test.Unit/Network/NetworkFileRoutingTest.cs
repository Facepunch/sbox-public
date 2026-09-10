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
