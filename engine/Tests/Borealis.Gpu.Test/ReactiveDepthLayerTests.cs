using Borealis;
using Borealis.Backend;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// The overlays' reactive depth on Borealis.Gpu: while a temporal upscaler asks (<see cref="RenderView.ReactiveDepth"/>), the
/// overlays are drawn again depth only into a depth of their own, the world left out, as native's reactive depth layers draw
/// them for the upscaler's reactive mask; nothing is drawn when it doesn't ask.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ReactiveDepthLayerTests : RenderGpuTest
{
	const int Width = 256, Height = 256;

	sealed class CameraStages : IRenderStages
	{
		public void Render( RenderStage stage, in StageTarget target ) { }
	}

	[TestMethod]
	public void OverlaysDrawIntoTheReactiveDepthOnlyWhenAsked()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Reactive depth test" );
			var world = new RenderWorld();
			world.Lighting.SunShadowCascades = 0;
			var grey = backend.CreateMaterial( "Reactive test grey", Color.Gray );

			// A world box in the middle of the view, an overlay box (a viewmodel) to its left
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, grey ), new Transform( new Vector3( 200, 0, 0 ), Rotation.Identity, 0.5f ) ) );
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, grey ), new Transform( new Vector3( 100, 40, 0 ), Rotation.Identity, 0.25f ) ) { GameLayers = false, Overlay = true } );

			var view = new RenderView { Name = "Reactive depth test", FieldOfView = 60, ZNear = 4, ZFar = 4000, Viewport = new Rect( 0, 0, Width, Height ) };
			view.Rotation = Rotation.LookAt( Vector3.Forward );

			// Not asked: nothing drawn
			Render( backend, system, world, view, target );
			Assert.IsNull( system.ReactiveDepth, "no reactive depth unless a temporal upscaler asks" );

			view.ReactiveDepth = true;
			Render( backend, system, world, view, target );
			Assert.IsNotNull( system.ReactiveDepth, "the overlays drew their reactive depth" );
			Assert.AreEqual( Width, system.ReactiveDepth.Width );

			var (texture, layout) = BackendDevice.Unwrap( system.ReactiveDepth );
			var depth = ReadBack( texture, layout );
			float Depth( int x, int y ) => BitConverter.ToSingle( depth, (y * Width + x) * 4 );

			// Left of centre (+y is left): the overlay box; the middle: the world box only; a corner: nothing
			var overlayX = Width / 2 - (int)(Width * 0.35f);
			Assert.IsTrue( Depth( overlayX, Height / 2 ) > 0.001f, $"the overlay is in the reactive depth ({Depth( overlayX, Height / 2 )})" );
			Assert.AreEqual( 0.0f, Depth( Width / 2 + 10, Height / 2 ), "the world isn't" );
			Assert.AreEqual( 0.0f, Depth( 4, 4 ), "cleared to the far plane" );

			// Asked no more: none again
			view.ReactiveDepth = false;
			Render( backend, system, world, view, target );
			Assert.IsNull( system.ReactiveDepth );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	void Render( BackendDevice backend, RenderSystem system, RenderWorld world, RenderView view, ViewTarget target )
	{
		backend.BeginFrame();
		RenderHost.Current.NextFrame();
		system.Render( world, view, target, new CameraStages() );
		Device.WaitIdle();
	}
}
