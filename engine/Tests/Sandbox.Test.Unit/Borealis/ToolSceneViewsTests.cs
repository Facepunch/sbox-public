using Editor;
using System;
using System.Runtime.InteropServices;

namespace BorealisTests;

/// <summary>
/// The hand-over from a native tool scene view (<c>ToolSceneManagedRender_t</c>, src/public/toolscene/toolscene.h) to
/// <see cref="ToolSceneViews"/>, which draws it on Borealis.Gpu.
/// </summary>
[TestClass]
public class ToolSceneViewsTests
{
	/// <summary>
	/// The request is laid out as native lays it out (x64): a size or offset changed on one side only reads garbage.
	/// </summary>
	[TestMethod]
	public void RequestMatchesNativeLayout()
	{
		Assert.AreEqual( 168, Marshal.SizeOf<ToolSceneViews.Request>() );
		Assert.AreEqual( 72, (int)Marshal.OffsetOf<ToolSceneViews.Request>( nameof( ToolSceneViews.Request.Window ) ) );
		Assert.AreEqual( 80, (int)Marshal.OffsetOf<ToolSceneViews.Request>( nameof( ToolSceneViews.Request.Width ) ) );
		Assert.AreEqual( 112, (int)Marshal.OffsetOf<ToolSceneViews.Request>( nameof( ToolSceneViews.Request.FovX ) ) );
		Assert.AreEqual( 132, (int)Marshal.OffsetOf<ToolSceneViews.Request>( nameof( ToolSceneViews.Request.Background ) ) );
		Assert.AreEqual( 152, (int)Marshal.OffsetOf<ToolSceneViews.Request>( nameof( ToolSceneViews.Request.Pixels ) ) );
		Assert.AreEqual( 164, (int)Marshal.OffsetOf<ToolSceneViews.Request>( nameof( ToolSceneViews.Request.Handled ) ) );
	}

	/// <summary>
	/// Read-backs come back as RGBA8 and are written in the format native asked for.
	/// </summary>
	[TestMethod]
	public void PixelsConvertToTheAskedFormat()
	{
		byte[] rgba = [10, 20, 30, 40, 50, 60, 70, 80];

		var rgb = new byte[6];
		ToolSceneViews.Convert( rgba, rgb, ToolSceneViews.PixelFormat.Rgb888 );
		CollectionAssert.AreEqual( new byte[] { 10, 20, 30, 50, 60, 70 }, rgb );

		var bgra = new byte[8];
		ToolSceneViews.Convert( rgba, bgra, ToolSceneViews.PixelFormat.Bgra8888 );
		CollectionAssert.AreEqual( new byte[] { 30, 20, 10, 40, 70, 60, 50, 80 }, bgra );

		var same = new byte[8];
		ToolSceneViews.Convert( rgba, same, ToolSceneViews.PixelFormat.Rgba8888 );
		CollectionAssert.AreEqual( rgba, same );
	}
}
