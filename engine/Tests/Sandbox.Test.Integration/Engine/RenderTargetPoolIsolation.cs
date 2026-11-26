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

	[TestMethod]
	public void NonMatchingDescriptorsNeverReuseATarget()
	{
		const string name = "DescriptorEquality";
		var original = RenderTarget.GetTemporary( 64, 32, ImageFormat.RGBA8888, ImageFormat.None, targetName: name );
		original.Dispose();
		using var otherWidth = RenderTarget.GetTemporary( 32, 32, ImageFormat.RGBA8888, ImageFormat.None, targetName: name );
		using var otherFormat = RenderTarget.GetTemporary( 64, 32, ImageFormat.R16F, ImageFormat.None, targetName: name );
		using var matching = RenderTarget.GetTemporary( 64, 32, ImageFormat.RGBA8888, ImageFormat.None, targetName: name );
		Assert.AreNotSame( original, otherWidth );
		Assert.AreNotSame( original, otherFormat );
		Assert.AreSame( original, matching );
	}

	[TestMethod]
	public void CaptureTargetsOutliveOrdinaryFrameTemporaries()
	{
		const string name = "CaptureLifetime";
		var capture = RenderTarget.GetCaptureTemporary( 64, 32, ImageFormat.RGBA8888, ImageFormat.None, name );
		using var ordinary = RenderTarget.GetTemporary( 64, 32, ImageFormat.RGBA8888, ImageFormat.None, targetName: name );
		Assert.AreNotSame( capture, ordinary );

		for ( int i = 0; i < 8; i++ ) RenderTarget.EndOfFrame();
		using var again = RenderTarget.GetCaptureTemporary( 64, 32, ImageFormat.RGBA8888, ImageFormat.None, name );
		Assert.AreSame( capture, again );
		Assert.IsNull( ordinary.ColorTarget );
	}
}
