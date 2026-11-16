using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// A view rendered for another frame's draws to sample (a water body's refraction on Borealis.Gpu): its target is moved to
/// where shaders read it before the frame that samples it is submitted (<see cref="BackendDevice.MakeReadable"/>).
/// </summary>
[TestClass]
[DoNotParallelize]
public class CaptureTests : GpuTest
{
	const int Width = 64, Height = 64;

	/// <summary>
	/// A frame leaves its colour where it drew it; made readable, it's in <see cref="ImageLayout.ShaderRead"/> with what was
	/// drawn, under synchronisation validation, and making it readable again records nothing.
	/// </summary>
	[TestMethod]
	public void RenderedViewEndsReadable()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false, DrawSky = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Capture test" );
			var world = new RenderWorld();
			world.Lighting.SunShadowCascades = 0;
			world.Lighting.AmbientColor = Color.White;
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, backend.CreateMaterial( "Capture box", Color.Red ) ), new Transform( new Vector3( 200, 0, 0 ) ) ) );
			var view = new RenderView { Name = "Capture test", FieldOfView = 60, ZNear = 4, ZFar = 4000, Viewport = new Rect( 0, 0, Width, Height ), ClearColor = Color.Black };
			view.Rotation = Rotation.LookAt( Vector3.Forward );

			backend.BeginFrame();
			RenderHost.Current.NextFrame();
			system.Render( world, view, target, null );
			Assert.AreNotEqual( ImageLayout.ShaderRead, BackendDevice.Unwrap( target.Resolved ).Layout, "a frame leaves its colour where it drew it" );

			backend.MakeReadable( target.Resolved );
			backend.MakeReadable( target.Resolved );
			backend.Gpu.Flush();
			Device.WaitIdle();

			var (handle, layout) = BackendDevice.Unwrap( target.Resolved );
			Assert.AreEqual( ImageLayout.ShaderRead, layout );
			var texels = ReadBack( handle, layout );
			BackendDevice.SetLayout( target.Resolved, ImageLayout.CopySource );

			// RGBA16F: the box is red in the middle, the clear black in a corner
			float Channel( int x, int y, int c ) => (float)BitConverter.ToHalf( texels, ((y * Width + x) * 4 + c) * 2 );
			Assert.IsTrue( Channel( Width / 2, Height / 2, 0 ) > Channel( Width / 2, Height / 2, 1 ), "the box is drawn" );
			Assert.AreEqual( 0f, Channel( 0, 0, 0 ), "the clear where nothing drew" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A mirrored view (a planar reflection: <see cref="RenderView.FlipX"/>, <see cref="RenderView.Reflection"/>, recorded on
	/// workers) is drawn, then copied flipped into its target by a full-screen draw: the side the box is on swaps.
	/// </summary>
	[TestMethod]
	public void FlippedViewIsCopiedMirrored()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = true, DrawSky = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Flip test" );
			var world = new RenderWorld();
			world.Lighting.SunShadowCascades = 0;
			world.Lighting.AmbientColor = Color.White;
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, backend.CreateMaterial( "Flip box", Color.Red ) ), new Transform( new Vector3( 200, 40, 0 ) ) ) );

			(float Left, float Right) Render( bool flip )
			{
				var view = new RenderView { Name = "Flip test", FieldOfView = 60, ZNear = 4, ZFar = 4000, Viewport = new Rect( 0, 0, Width, Height ), ClearColor = Color.Black, FlipX = flip, Reflection = flip };
				view.Rotation = Rotation.LookAt( Vector3.Forward );

				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target, null );
				backend.MakeReadable( target.Resolved );
				backend.Gpu.Flush();
				Device.WaitIdle();

				var (handle, layout) = BackendDevice.Unwrap( target.Resolved );
				var texels = ReadBack( handle, layout );
				BackendDevice.SetLayout( target.Resolved, ImageLayout.CopySource );

				float Red( int x, int y ) => (float)BitConverter.ToHalf( texels, ((y * Width + x) * 4) * 2 );
				float left = 0, right = 0;
				for ( int y = 0; y < Height; y++ )
				{
					for ( int x = 0; x < Width / 2; x++ ) left += Red( x, y );
					for ( int x = Width / 2; x < Width; x++ ) right += Red( x, y );
				}

				return (left, right);
			}

			var straight = Render( false );
			var flipped = Render( true );
			var boxOnLeft = straight.Left > straight.Right;
			Assert.IsTrue( Math.Max( straight.Left, straight.Right ) > Math.Min( straight.Left, straight.Right ) * 4, $"the box is to one side ({straight})" );
			Assert.AreEqual( boxOnLeft, flipped.Right > flipped.Left, $"the flipped copy has the box on the other side ({straight} then {flipped})" );
			Assert.IsTrue( Math.Max( flipped.Left, flipped.Right ) > Math.Max( straight.Left, straight.Right ) * 0.5f, $"the flipped copy has the box ({straight} then {flipped})" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}
}
