using Borealis;
using Borealis.Backend;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// The bloom layer on Borealis.Gpu: bloom objects drawn at a quarter of the resolution, depth tested against the scene's
/// depth downsampled there, as native's bloom layer is.
/// </summary>
[TestClass]
[DoNotParallelize]
public class BloomLayerTests : RenderGpuTest
{
	const int Width = 256, Height = 256;

	sealed class CameraStages : IRenderStages
	{
		public void Render( RenderStage stage, in StageTarget target ) { }
	}

	/// <summary>
	/// A glowing box (bloom only) behind an opaque one that covers the middle of the view: the quarter-resolution depth holds
	/// the opaque box, and the glow is hidden behind it, showing only around it, from a multisampled frame too.
	/// </summary>
	[TestMethod]
	[DataRow( 1 )]
	[DataRow( 4 )]
	public void BloomObjectsHideBehindTheScene( int samples )
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), samples, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Bloom layer test" );
			var world = new RenderWorld();
			world.Lighting.SunShadowCascades = 0;
			world.Lighting.AmbientColor = new Color( 0.2f, 0.2f, 0.2f, 1 );
			var grey = backend.CreateMaterial( "Bloom test grey", Color.Gray );
			var glow = backend.CreateMaterial( "Bloom test glow", Color.White );

			// The opaque box in the middle of the view, the glowing one behind it filling the view
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, grey ), new Transform( new Vector3( 200, 0, 0 ), Rotation.Identity, 0.5f ) ) );
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, glow ), new Transform( new Vector3( 1000, 0, 0 ), Rotation.Identity, 8 ) ) { GameLayers = false, Bloom = true } );

			var view = new RenderView { Name = "Bloom layer test", FieldOfView = 60, ZNear = 4, ZFar = 4000, Viewport = new Rect( 0, 0, Width, Height ) };
			view.Rotation = Rotation.LookAt( Vector3.Forward );

			backend.BeginFrame();
			RenderHost.Current.NextFrame();
			system.Render( world, view, target, new CameraStages() );
			Device.WaitIdle();

			var bloom = system.Layers.OfType<BloomLayer>().Single();
			Assert.AreSame( bloom.BloomColor, system.BloomObjects, "the frame drew bloom objects" );

			var (depthTexture, depthLayout) = BackendDevice.Unwrap( bloom.BloomDepth );
			var depth = ReadBack( depthTexture, depthLayout );
			var (colorTexture, colorLayout) = BackendDevice.Unwrap( bloom.BloomColor );
			var color = ReadBack( colorTexture, colorLayout );
			int w = bloom.BloomColor.Width, h = bloom.BloomColor.Height;

			float Depth( int x, int y ) => BitConverter.ToSingle( depth, (y * w + x) * 4 );
			uint Packed( int x, int y ) => BitConverter.ToUInt32( color, (y * w + x) * 4 );

			Assert.AreNotEqual( 0.0f, Depth( w / 2, h / 2 ), "the opaque box is in the quarter-resolution depth" );
			Assert.AreEqual( 0u, Packed( w / 2, h / 2 ) & 0x3FFFFFFFu, "the glow is hidden behind it" );
			Assert.AreNotEqual( 0u, Packed( w / 2, h / 8 ) & 0x3FFFFFFFu, "and shows around it" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}
}
