using Borealis;
using GpuViewConstants = Borealis.Gpu.ViewConstants;

namespace EngineTests;

/// <summary>
/// The depth range a camera's stages draw in over a frame on a Borealis.Gpu device, as native's hook layers have it: the
/// view's (where the scene drew), 0 to 1 in a render target of their own, and the view's again when they come back to the
/// frame's targets.
/// </summary>
[TestClass]
public class BorealisGpuStageDepthRangeTest
{
	/// <summary>
	/// Depth of field's back layer places its quad at the focus plane and depth tests it against the scene: drawn from 0 to 1
	/// over a scene drawn from 0.02 it fell behind everything and blurred nothing. An outline effect's own depth targets read
	/// their depth as 0 to 1.
	/// </summary>
	[TestMethod]
	[DataRow( 1 )]
	[DataRow( 4 )]
	public void StagesDrawInTheViewsDepthRange( int samples )
	{
		TestGraphics.Require();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		const int size = 32;
		using var color = borealis.Services.CreateTexture( TextureDesc.Target( size, size, TextureFormat.RGBA16161616F, samples ), "stage colour" );
		using var depth = borealis.Services.CreateTexture( TextureDesc.Target( size, size, TextureFormat.D32FS8, samples ), "stage depth" );
		using var resolved = samples > 1 ? borealis.Services.CreateTexture( TextureDesc.Target( size, size, TextureFormat.RGBA16161616F ), "stage resolved" ) : null;
		using var target = new ViewTarget( color, depth, resolved );
		using var own = Texture.CreateOnBorealisGpu( Texture.CreateRenderTarget().WithSize( size, size ).WithFormat( ImageFormat.RGBA8888 ), "stage's own target" );
		using var ownTarget = RenderTarget.From( own );

		var constants = new GpuViewConstants { ViewportMinZ = 0.02f, ViewportMaxZ = 1.0f };
		using var frame = new BorealisGpuFrame( borealis );
		using var view = new BorealisGpuView( borealis ) { Viewport = new Rect( 0, 0, size, size ) };
		frame.Adopt( target, constants );

		(float Min, float Max) inFrame, inOwn, back;
		using ( frame.Begin( view ) )
		{
			inFrame = Range();
			Graphics.RenderTarget = ownTarget;
			inOwn = Range();
			Graphics.RenderTarget = null;
			back = Range();
		}

		frame.End();
		frame.Release();

		Assert.AreEqual( (0.02f, 1.0f), inFrame, "over the frame, the view's range" );
		Assert.AreEqual( (0.0f, 1.0f), inOwn, "in a target of its own, 0 to 1" );
		Assert.AreEqual( (0.02f, 1.0f), back, "back on the frame, the view's range again" );
		Assert.AreEqual( 0, borealis.ValidationErrors );

		static (float, float) Range()
		{
			var viewport = Graphics.Backend.GetViewport();
			return (viewport.MinZ, viewport.MaxZ);
		}
	}
}
