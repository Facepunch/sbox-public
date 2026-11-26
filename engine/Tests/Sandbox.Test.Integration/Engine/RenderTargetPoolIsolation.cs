namespace EngineTests;

/// <summary>
/// Pooled targets keep their pixels. Game code asking for the same size and name must not be handed
/// one the menu UI or a screenshot drew into.
/// </summary>
[TestClass]
public class RenderTargetPoolIsolationTest
{
	[TestMethod]
	public void PublicRequestsNeverGetEngineTargets()
	{
		var engine = RenderTarget.GetEngineTemporary( 64, 64, ImageFormat.RGBA8888, ImageFormat.None, targetName: "FrameBufferCopyTexture" );
		RenderTarget.EndOfFrame();

		var game = RenderTarget.GetTemporary( 64, 64, ImageFormat.RGBA8888, ImageFormat.None, targetName: "FrameBufferCopyTexture" );
		RenderTarget.EndOfFrame();

		var engineAgain = RenderTarget.GetEngineTemporary( 64, 64, ImageFormat.RGBA8888, ImageFormat.None, targetName: "FrameBufferCopyTexture" );
		RenderTarget.EndOfFrame();

		Assert.AreNotSame( engine, game );
		Assert.AreSame( engine, engineAgain, "Engine requests still reuse their own targets" );
	}
}
