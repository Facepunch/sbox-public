namespace SceneTests;

/// <summary>
/// <c>r_skip_hidden_cameras</c>: which camera hides everything drawn to the screen before it in a frame.
/// </summary>
[TestClass]
public class CameraCoverTests
{
	static readonly Vector4 Full = new( 0, 0, 1, 1 );

	[TestMethod]
	public void FullScreenOpaqueColorClearHides()
	{
		Assert.IsTrue( SceneCameraCover.HidesScreen( true, true, false, Full, ClearFlags.All, 1 ) );
		Assert.IsTrue( SceneCameraCover.HidesScreen( true, true, false, Full, ClearFlags.Color, 1 ) );
		Assert.IsTrue( SceneCameraCover.HidesScreen( true, true, false, new Vector4( -0.1f, 0, 1.2f, 1 ), ClearFlags.All, 1 ) );
	}

	[TestMethod]
	public void WhatLeavesTheScreenVisibleDoesNotHide()
	{
		// Not rendering, to a texture, at its own size
		Assert.IsFalse( SceneCameraCover.HidesScreen( false, true, false, Full, ClearFlags.All, 1 ) );
		Assert.IsFalse( SceneCameraCover.HidesScreen( true, false, false, Full, ClearFlags.All, 1 ) );
		Assert.IsFalse( SceneCameraCover.HidesScreen( true, true, true, Full, ClearFlags.All, 1 ) );
		// Part of the screen: a picture in picture, a split screen
		Assert.IsFalse( SceneCameraCover.HidesScreen( true, true, false, new Vector4( 0, 0, 0.5f, 1 ), ClearFlags.All, 1 ) );
		Assert.IsFalse( SceneCameraCover.HidesScreen( true, true, false, new Vector4( 0.75f, 0.75f, 0.25f, 0.25f ), ClearFlags.All, 1 ) );
		Assert.IsFalse( SceneCameraCover.HidesScreen( true, true, false, new Vector4( 0.1f, 0, 0.9f, 1 ), ClearFlags.All, 1 ) );
		// An overlay that keeps the color (a view model), or clears it to a see-through background
		Assert.IsFalse( SceneCameraCover.HidesScreen( true, true, false, Full, ClearFlags.Depth | ClearFlags.Stencil, 1 ) );
		Assert.IsFalse( SceneCameraCover.HidesScreen( true, true, false, Full, ClearFlags.None, 1 ) );
		Assert.IsFalse( SceneCameraCover.HidesScreen( true, true, false, Full, ClearFlags.All, 0.5f ) );
		Assert.IsFalse( SceneCameraCover.HidesScreen( true, true, false, Full, ClearFlags.All, 0 ) );
	}

	[TestMethod]
	public void TheLastCoverHidesTheCamerasBeforeIt()
	{
		static bool Hides( bool hides ) => hides;

		// The last camera that hides the screen, in render order
		Assert.AreEqual( 2, SceneCameraCover.LastCover( new[] { false, true, true, false }, Hides ) );
		Assert.AreEqual( 1, SceneCameraCover.LastCover( new[] { true, true }, Hides ) );
		// A cover first in the frame, or none: nothing before it to skip
		Assert.AreEqual( -1, SceneCameraCover.LastCover( new[] { true, false, false }, Hides ) );
		Assert.AreEqual( -1, SceneCameraCover.LastCover( new[] { false, false }, Hides ) );
		Assert.AreEqual( -1, SceneCameraCover.LastCover( new[] { true }, Hides ) );
		Assert.AreEqual( -1, SceneCameraCover.LastCover( System.Array.Empty<bool>(), Hides ) );
	}
}
