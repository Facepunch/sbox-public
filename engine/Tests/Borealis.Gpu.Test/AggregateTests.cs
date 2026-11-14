using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// Aggregates on Borealis.Gpu (<see cref="AggregateObject"/>, drawn by <c>BackendContext.DrawModelFragments</c>): each view's
/// visible fragments as one multi-draw indirect, through material objects and by reflection.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AggregateTests : GpuTest
{
	const int Width = 480, Height = 300;
	const int Boxes = 4;

	/// <summary>
	/// Boxes in world space in one mesh, drawn as an aggregate of one fragment each (prepass, shadows, forward), draw the
	/// pixels the same mesh drawn as one object does, through material objects and by reflection alike, with one multi-draw
	/// per pass; a warm frame writes no sets.
	/// </summary>
	[TestMethod]
	public void FragmentsDrawWhatTheWholeMeshDraws()
	{
		// The classic fragment path: GpuDrivenTests compares the GPU-driven one with it
		using var backend = new BackendDevice( Device ) { GpuDriven = false };
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Aggregate test" );
			var material = backend.CreateMaterial( "Boxes", new Color( 0.8f, 0.6f, 0.4f ) );
			var (mesh, fragments) = BoxesMesh( backend, material );
			var ground = BackendMesh.Create( backend, Quad( 900 ), [0, 1, 2, 0, 2, 3], backend.CreateMaterial( "Ground", new Color( 0.5f, 0.5f, 0.5f ) ), "Ground" );

			RenderWorld World( bool aggregate )
			{
				var world = new RenderWorld();
				world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
				world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
				world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
				world.Lighting.SunShadowCascades = 2;
				world.Add( new MeshObject( ground, Transform.Zero ) );
				world.Add( aggregate ? new AggregateObject( mesh, fragments ) : new MeshObject( mesh, Transform.Zero ) );
				return world;
			}

			var view = new RenderView { Name = "Aggregate test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			byte[] Frame( RenderWorld world, bool materialObjects )
			{
				backend.MaterialObjects = materialObjects;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			var whole = World( aggregate: false );
			var aggregated = World( aggregate: true );

			var reference = Frame( whole, true );
			backend.TimeDraws = true;
			backend.DrawTimes.Reset();
			var objects = Frame( aggregated, true );
			var indirect = backend.DrawTimes.IndirectDraws;
			backend.TimeDraws = false;
			var reflection = Frame( aggregated, false );

			// Warm: per-frame buffers go round their ring of three
			for ( int i = 0; i < 3; i++ ) Frame( aggregated, true );
			var written = backend.MaterialObjectStats.SetsWritten;
			var again = Frame( aggregated, true );

			Assert.IsTrue( indirect >= Boxes, $"the fragments drew as multi-draws ({indirect} fragment draws)" );
			Assert.AreEqual( 0, Different( reference, objects ), "fragments draw what the whole mesh does, through material objects" );
			Assert.AreEqual( 0, Different( reference, reflection ), "fragments draw what the whole mesh does, by reflection" );
			CollectionAssert.AreEqual( objects, again, "the same frame twice" );
			Assert.AreEqual( written, backend.MaterialObjectStats.SetsWritten, "a warm frame writes no descriptor sets" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Fragments out of view aren't drawn: an aggregate whose fragments are all behind the camera draws nothing, and one
	/// with some in view draws only those.
	/// </summary>
	[TestMethod]
	public void FragmentsOutOfViewAreCulled()
	{
		// The classic fragment path, culled on the CPU: GPU-driven drawing culls on the GPU (GpuDrivenTests)
		using var backend = new BackendDevice( Device ) { GpuDriven = false };
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Aggregate cull test" );
			var (mesh, fragments) = BoxesMesh( backend, backend.CreateMaterial( "Boxes", new Color( 0.8f, 0.6f, 0.4f ) ) );
			var world = new RenderWorld();
			world.Lighting.SunShadowCascades = 0;
			world.Add( new AggregateObject( mesh, fragments ) );

			// Looking along the row from its first box: only the boxes ahead are in view
			var view = new RenderView { Name = "Aggregate cull test", FieldOfView = 40, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( 150, 0, 40 );
			view.Rotation = Rotation.LookAt( Vector3.Forward );

			backend.TimeDraws = true;
			backend.DrawTimes.Reset();
			backend.BeginFrame();
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );
			Device.WaitIdle();
			var drawn = backend.DrawTimes.IndirectDraws;

			Assert.IsTrue( drawn > 0 && drawn < Boxes * 2, $"some fragments drew, not all ({drawn} fragment draws over prepass and forward)" );
			Assert.AreEqual( 1, system.Stats.Draws, "one multi-draw in the forward pass" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Sun cascades with nothing to draw (a map whose static geometry casts no shadows, as a baked sun's doesn't) record
	/// only their clears, in segments sharing the frame's list; they're flushed in order, and the frame draws its ground
	/// unshadowed, the same twice.
	/// </summary>
	[TestMethod]
	public void PassesThatDrawNothingStillClear()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 4, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Empty passes test" );
			var (mesh, fragments) = BoxesMesh( backend, backend.CreateMaterial( "Boxes", new Color( 0.8f, 0.6f, 0.4f ) ) );
			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunShadowCascades = 3;
			world.Add( new AggregateObject( mesh, fragments ) { CastShadows = false } );

			var view = new RenderView { Name = "Empty passes test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			byte[] Frame()
			{
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Resolved );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Resolved, ImageLayout.CopySource );
				return pixels;
			}

			var first = Frame();
			CollectionAssert.AreEqual( first, Frame(), "the same frame twice" );
			Assert.AreEqual( 0, system.Stats.ShadowDraws, "nothing drew into the cascades" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// <see cref="Boxes"/> boxes in a row, in world space, in one mesh of one draw; and a fragment per box over that draw's
	/// index range, each with its own bounds, as a compiled map's aggregate lays its draws out.
	/// </summary>
	static (RenderMesh Mesh, AggregateObject.FragmentDraw[] Fragments) BoxesMesh( BackendDevice backend, GpuMaterial material )
	{
		var vertices = new List<MeshVertex>();
		var indices = new List<uint>();
		var fragments = new AggregateObject.FragmentDraw[Boxes];
		for ( int b = 0; b < Boxes; b++ )
		{
			var centre = new Vector3( b * 120 - 180, 0, 40 );
			var firstIndex = indices.Count;
			Box( vertices, indices, centre, 40 );
			fragments[b] = new AggregateObject.FragmentDraw
			{
				Transform = Matrix3x4.Identity,
				BoundsMin = centre - 40,
				BoundsMax = centre + 40,
				DrawTint = Vector3.One,
				Args = new GpuBuffer.IndirectDrawIndexedArguments { IndexCount = (uint)(indices.Count - firstIndex), InstanceCount = 1, FirstIndex = (uint)firstIndex, BaseVertex = 0, FirstInstance = (uint)b },
			};
		}

		return (BackendMesh.Create( backend, vertices.ToArray(), indices.ToArray(), material, "Boxes" ), fragments);
	}

	static void Box( List<MeshVertex> vertices, List<uint> indices, Vector3 centre, float half )
	{
		Vector3[] normals = [Vector3.Forward, Vector3.Backward, Vector3.Left, Vector3.Right, Vector3.Up, Vector3.Down];
		foreach ( var n in normals )
		{
			var u = MathF.Abs( n.z ) > 0.5f ? Vector3.Forward : Vector3.Cross( Vector3.Up, n );
			var v = Vector3.Cross( n, u );
			var first = (uint)vertices.Count;
			vertices.Add( new MeshVertex( centre + (n - u - v) * half, n, new Vector2( 0, 1 ) ) );
			vertices.Add( new MeshVertex( centre + (n + u - v) * half, n, new Vector2( 1, 1 ) ) );
			vertices.Add( new MeshVertex( centre + (n + u + v) * half, n, new Vector2( 1, 0 ) ) );
			vertices.Add( new MeshVertex( centre + (n - u + v) * half, n, new Vector2( 0, 0 ) ) );
			indices.AddRange( [first, first + 1, first + 2, first, first + 2, first + 3] );
		}
	}

	static MeshVertex[] Quad( float size )
	{
		var h = size / 2;
		return
		[
			new( new Vector3( -h, -h, 0 ), Vector3.Up, new Vector2( 0, 0 ) ),
			new( new Vector3( h, -h, 0 ), Vector3.Up, new Vector2( 1, 0 ) ),
			new( new Vector3( h, h, 0 ), Vector3.Up, new Vector2( 1, 1 ) ),
			new( new Vector3( -h, h, 0 ), Vector3.Up, new Vector2( 0, 1 ) ),
		];
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
