using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// A mesh moved between frames, with the change written straight into its render object (what the editor widget does: tools
/// move an object, then the world draws), is drawn at its new place in the very next frame - not the frame after.
/// </summary>
[TestClass]
[DoNotParallelize]
public class MovedMeshTests : RenderGpuTest
{
	const int Width = 320, Height = 200;

	/// <summary>
	/// A box moved right before a frame draws where the frames after it do: freshly made (drawn once before) or settled
	/// (drawn for many frames, so a resident list holds it), static or not, GPU-driven or classic.
	/// </summary>
	[TestMethod]
	[DataRow( true, false, 1 )]
	[DataRow( true, false, 12 )]
	[DataRow( true, true, 1 )]
	[DataRow( true, true, 12 )]
	[DataRow( false, false, 1 )]
	[DataRow( false, false, 12 )]
	public void AMovedMeshDrawsAtItsNewPlaceInTheNextFrame( bool gpuDriven, bool isStatic, int framesBefore )
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = gpuDriven, GpuDrivenVisibility = gpuDriven };
		GpuDrivenTests.RequireGpuDrivenDepth( backend );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Moved mesh test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 0;

			var vertices = new List<MeshVertex>();
			var indices = new List<uint>();
			GpuDrivenTests.Box( vertices, indices, Vector3.Zero, 10 );
			var model = BackendMesh.Create( backend, vertices.ToArray(), indices.ToArray(), backend.CreateMaterial( "Moved box", new Color( 0.8f, 0.6f, 0.4f ) ), "Moved box" );

			// Bystanders that stay put, so the world's lists hold more than the box
			for ( int i = 0; i < 3; i++ ) world.Add( new MeshObject( model, new Transform( new Vector3( 100, -60 + i * 60, 40 ) ) ) { IsStatic = true } );

			var box = new MeshObject( model, new Transform( new Vector3( 100, 0, 0 ) ) ) { IsStatic = isStatic };
			world.Add( box );

			var view = new RenderView { Name = "Moved mesh test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -200, 0, 20 );
			view.Rotation = Rotation.LookAt( new Vector3( 1, 0, -0.1f ) );

			byte[] Frame()
			{
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			byte[] before = null;
			for ( int i = 0; i < framesBefore; i++ ) before = Frame();

			// Two moves in a row: the lag, if any, is the previous frame's place
			foreach ( var y in new[] { 70f, -60f } )
			{
				box.Transform = box.Transform.WithPosition( new Vector3( 100, y, 0 ) );
				var moved = Frame();

				byte[] settled = null;
				for ( int i = 0; i < 10; i++ ) settled = Frame();

				Assert.AreNotEqual( 0, Different( before, settled ), $"y={y}: the box moved on screen" );
				Assert.AreEqual( 0, Different( moved, settled ), $"y={y}: the frame right after the move draws the box where the frames after it do" );
				before = settled;
			}

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	static int Different( byte[] a, byte[] b )
	{
		Assert.AreEqual( a.Length, b.Length );
		var count = 0;
		for ( int i = 0; i < a.Length; i++ )
		{
			if ( a[i] != b[i] ) count++;
		}

		return count;
	}
}
