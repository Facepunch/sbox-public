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
		Assert.AreEqual( 200, Marshal.SizeOf<ToolSceneViews.Request>() );
		Assert.AreEqual( 72, (int)Marshal.OffsetOf<ToolSceneViews.Request>( nameof( ToolSceneViews.Request.Window ) ) );
		Assert.AreEqual( 80, (int)Marshal.OffsetOf<ToolSceneViews.Request>( nameof( ToolSceneViews.Request.Width ) ) );
		Assert.AreEqual( 112, (int)Marshal.OffsetOf<ToolSceneViews.Request>( nameof( ToolSceneViews.Request.FovX ) ) );
		Assert.AreEqual( 132, (int)Marshal.OffsetOf<ToolSceneViews.Request>( nameof( ToolSceneViews.Request.Background ) ) );
		Assert.AreEqual( 152, (int)Marshal.OffsetOf<ToolSceneViews.Request>( nameof( ToolSceneViews.Request.Pixels ) ) );
		Assert.AreEqual( 164, (int)Marshal.OffsetOf<ToolSceneViews.Request>( nameof( ToolSceneViews.Request.Handled ) ) );
		Assert.AreEqual( 168, (int)Marshal.OffsetOf<ToolSceneViews.Request>( nameof( ToolSceneViews.Request.GetGeometry ) ) );
		Assert.AreEqual( 176, (int)Marshal.OffsetOf<ToolSceneViews.Request>( nameof( ToolSceneViews.Request.GetText ) ) );
		Assert.AreEqual( 184, (int)Marshal.OffsetOf<ToolSceneViews.Request>( nameof( ToolSceneViews.Request.ScreenSpaceWorld ) ) );
		Assert.AreEqual( 188, (int)Marshal.OffsetOf<ToolSceneViews.Request>( nameof( ToolSceneViews.Request.ToolsVisMode ) ) );
		Assert.AreEqual( 192, (int)Marshal.OffsetOf<ToolSceneViews.Request>( nameof( ToolSceneViews.Request.DrawFlags ) ) );

		// ToolSceneText_t
		Assert.AreEqual( 48, Marshal.SizeOf<ToolSceneViews.Text>() );
		Assert.AreEqual( 8, (int)Marshal.OffsetOf<ToolSceneViews.Text>( nameof( ToolSceneViews.Text.World ) ) );
		Assert.AreEqual( 40, (int)Marshal.OffsetOf<ToolSceneViews.Text>( nameof( ToolSceneViews.Text.Color ) ) );
		Assert.AreEqual( 44, (int)Marshal.OffsetOf<ToolSceneViews.Text>( nameof( ToolSceneViews.Text.InWorld ) ) );

		// ToolSceneGeometry_t and ColoredVertex_t
		Assert.AreEqual( 32, Marshal.SizeOf<ToolSceneViews.Geometry>() );
		Assert.AreEqual( 56, Marshal.SizeOf<ToolSceneViews.ToolVertex>() );
		Assert.AreEqual( 52, (int)Marshal.OffsetOf<ToolSceneViews.ToolVertex>( nameof( ToolSceneViews.ToolVertex.Color ) ) );
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

	/// <summary>
	/// Hammer's selection draws in native's colours: its overlay rectangle's vertex colour and the outline's colour are sRGB,
	/// made linear as <c>tools_selection_overlay</c> and <c>SRGBColorToLinearColor</c> make them, with the alpha kept.
	/// </summary>
	[TestMethod]
	public void SelectionColoursAreNativesMadeLinear()
	{
		var tint = ToolSceneViews.ToolSelection.TintFor( new Color32( 255, 183, 45, 96 ) );
		Assert.AreEqual( 1.0f, tint.x, 1e-4f );
		Assert.AreEqual( 0.4735f, tint.y, 1e-3f );
		Assert.AreEqual( 0.0262f, tint.z, 1e-3f );
		Assert.AreEqual( 96 / 255.0f, tint.w, 1e-4f );

		var outline = ToolSceneViews.ToolSelection.SelectedOutlineColor;
		Assert.AreEqual( 0.4735f, outline.y, 1e-3f );
		Assert.AreEqual( 1.0f, outline.w );
	}
}
