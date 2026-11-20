using System;

namespace EngineTests;

/// <summary>
/// Video on a Borealis.Gpu device: the decoder's planes come to the player on the CPU and are converted into its texture on
/// the device, as the decoder converts them on Source 2's.
/// </summary>
[TestClass]
public class BorealisGpuVideoTest
{
	/// <summary>
	/// Limited-range BT.709 planes for white, then red, land in the player's texture as those colours, with no validation
	/// errors; the player asks for planes only while Borealis.Gpu is the device.
	/// </summary>
	[TestMethod]
	public void PlanesConvertIntoThePlayersTexture()
	{
		TestGraphics.Require();

		using var player = new VideoPlayer();
		if ( RenderBackend.Default is Source2RenderBackend )
			Assert.IsFalse( player.WantsPlanes(), "Source 2's device converts its own frames" );

		var messages = new System.Collections.Concurrent.ConcurrentQueue<string>();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true, message: m => messages.Enqueue( $"{m.Severity} {m.Text}" ) );
		var previous = RenderBackend.Default;
		RenderBackend.SetDefault( borealis );
		try
		{
			Assert.IsTrue( player.WantsPlanes() );

			const int width = 16, height = 8;
			Present( player, width, height, 235, 128, 128 );
			var white = borealis.ReadPixels( player.Texture );
			Assert.AreEqual( width, player.Texture.Width );
			AssertNear( new Color32( 255, 255, 255, 255 ), white[3 * width + 5], "white" );

			// BT.709 limited red: Y 63, Cb 102, Cr 240
			Present( player, width, height, 63, 102, 240 );
			var red = borealis.ReadPixels( player.Texture );
			AssertNear( new Color32( 255, 0, 0, 255 ), red[3 * width + 5], "red" );

			Assert.AreEqual( 0, borealis.ValidationErrors, string.Join( "; ", messages ) );
		}
		finally
		{
			RenderBackend.SetDefault( previous );
		}
	}

	static unsafe void Present( VideoPlayer player, int width, int height, byte y, byte u, byte v )
	{
		var planeY = new byte[width * height];
		var planeU = new byte[width * height / 4];
		var planeV = new byte[width * height / 4];
		Array.Fill( planeY, y );
		Array.Fill( planeU, u );
		Array.Fill( planeV, v );

		fixed ( byte* py = planeY )
		fixed ( byte* pu = planeU )
		fixed ( byte* pv = planeV )
		{
			player.OnPlanesInternal( (IntPtr)py, (IntPtr)pu, (IntPtr)pv, IntPtr.Zero, width, height, width, width / 2, width / 2, 0, false, true );
		}
	}

	static void AssertNear( Color32 expected, Color32 actual, string what )
	{
		Assert.IsTrue( Math.Abs( expected.r - actual.r ) <= 4 && Math.Abs( expected.g - actual.g ) <= 4 && Math.Abs( expected.b - actual.b ) <= 4,
			$"{what}: expected {expected}, got {actual}" );
	}
}
