using Sandbox.Rendering;

namespace SceneTests;

/// <summary>
/// Which frames are captures, which CMAA2 skips: a cube face, a child view or a reflection, never a camera's own frame.
/// </summary>
[TestClass]
public class CaptureFramesTests
{
	[TestMethod]
	public void CapturesAreCubeFacesChildViewsAndReflections()
	{
		Assert.IsFalse( CaptureFrames.IsCapture( -1, false, false ), "a camera's own frame" );

		for ( int face = 0; face < 6; face++ )
			Assert.IsTrue( CaptureFrames.IsCapture( face, false, false ), $"cube face {face}" );

		Assert.IsTrue( CaptureFrames.IsCapture( -1, true, false ), "a child view" );
		Assert.IsTrue( CaptureFrames.IsCapture( -1, false, true ), "a reflection" );
	}
}
