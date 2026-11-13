using Basalt;
using Basalt.Backend;
using Basalt.Gpu;
using Basalt.Source2;
using Basalt.Source2.Shaders;
using Sandbox;
using System.IO;
using System.Threading;

namespace BasaltGpuTests;

/// <summary>
/// GPU-driven aggregates (<see cref="BackendDevice.GpuDriven"/>, docs/managed/basalt-gpu-driven.md): the shaders'
/// <c>.gpudriven.shader_c</c> variants pull vertices and read materials from tables, and a pass's fragments of every
/// material draw as one multi-draw per pipeline.
/// </summary>
[TestClass]
[DoNotParallelize]
public class GpuDrivenTests : GpuTest
{
	const int Width = 480, Height = 300;
	const int Rows = 3;

	/// <summary>
	/// Aggregates of boxes in three materials (prepass, forward) draw the same pixels GPU-driven as the classic way, one
	/// bucket for all three materials instead of a bind each; with a bucket per material; culled on the CPU instead of the
	/// GPU; and culled on the GPU with and without compacting the commands.
	/// </summary>
	[TestMethod]
	public void AggregatesDrawTheSamePixelsGpuDriven()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDrivenMeshes = true };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "GPU-driven test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 0;

			Color[] colours = [new( 0.8f, 0.6f, 0.4f ), new( 0.3f, 0.7f, 0.4f ), new( 0.4f, 0.5f, 0.9f )];
			for ( int row = 0; row < Rows; row++ )
			{
				var (mesh, fragments) = Boxes( backend, backend.CreateMaterial( $"Boxes {row}", colours[row] ), row * 120 - 120 );
				world.Add( new AggregateObject( mesh, fragments ) );
			}

			// Static meshes too: one drawn twice (an instanced run), and one of a material the aggregates also have
			var (shared, _) = Boxes( backend, backend.CreateMaterial( "Mesh boxes", new Color( 0.9f, 0.3f, 0.3f ) ), 240 );
			world.Add( new MeshObject( shared, Transform.Zero ) );
			world.Add( new MeshObject( shared, new Transform( new Vector3( 0, 0, 90 ) ) ) );
			var (own, _) = Boxes( backend, backend.CreateMaterial( "Mesh boxes 2", colours[0] ), -240 );
			world.Add( new MeshObject( own, Transform.Zero ) );

			var view = new RenderView { Name = "GPU-driven test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			long gpuDrivenBatches = 0;
			byte[] Frame( bool gpuDriven, out int binds, bool materialBuckets, bool gpuCulling = true, bool compaction = true, bool positions = true, bool visibility = false )
			{
				backend.GpuDriven = gpuDriven;
				backend.GpuDrivenVisibility = visibility;
				backend.GpuDrivenCulling = gpuCulling;
				backend.GpuDrivenCompaction = compaction;
				backend.PositionStreams = positions;
				backend.GpuDrivenMaterialBuckets = materialBuckets;
				backend.TimeDraws = true;
				backend.DrawTimes.Reset();
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				binds = (int)backend.DrawTimes.Binds;
				gpuDrivenBatches = backend.DrawTimes.GpuDrivenBatches;
				backend.TimeDraws = false;
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			var classic = Frame( false, out var classicBinds, false );
			var driven = Frame( true, out var drivenBinds, false );
			// The GPU culls them, so preparing the view tests none: each aggregate's batch has all five fragments, the one
			// beyond the far plane too
			var preparedFragments = system.GetFeature<Basalt.Features.AggregateRenderFeature>().BatchesFor( system.MainPass ).Select( b => b.Count ).ToArray();
			var drivenBatches = gpuDrivenBatches;
			var drawRecords = backend.GpuDrivenTables.Records.Draws;
			var again = Frame( true, out _, false );
			Assert.AreEqual( drawRecords, backend.GpuDrivenTables.Records.Draws, "the second frame reuses the aggregates' draw records" );
			var perMaterial = Frame( true, out var perMaterialBinds, true );
			var cpuCulled = Frame( true, out _, false, gpuCulling: false );
			var uncompacted = Frame( true, out _, false, compaction: false );
			var fullVertices = Frame( true, out _, false, positions: false );
			var visible = Frame( true, out _, false, visibility: true );
			var visibleAgain = Frame( true, out _, false, visibility: true );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			CollectionAssert.AreEqual( Enumerable.Repeat( 5, Rows ).ToArray(), preparedFragments, "every fragment prepared, for the GPU to cull" );
			Assert.IsTrue( drivenBatches > Rows * 2, $"the static meshes drew GPU-driven with the aggregates ({drivenBatches} batches)" );
			Assert.AreEqual( 0, Different( classic, perMaterial ), "a bucket per material draws the classic pixels" );
			Assert.IsTrue( perMaterialBinds > drivenBinds, $"a bucket per material binds more ({perMaterialBinds}) than one bucket ({drivenBinds})" );
			Assert.IsTrue( backend.GpuDrivenTables.Records.Draws > 1, $"the fragments have draw records ({backend.GpuDrivenTables.Records.Draws})" );
			Assert.IsTrue( drivenBinds < classicBinds, $"fewer binds GPU-driven ({drivenBinds}) than classic ({classicBinds})" );
			Assert.AreEqual( 0, Different( classic, driven ), "GPU-driven draws the classic pixels" );
			CollectionAssert.AreEqual( driven, again, "the same frame twice" );
			Assert.AreEqual( 0, Different( classic, cpuCulled ), "culled on the CPU, the classic pixels too" );
			Assert.AreEqual( 0, Different( classic, uncompacted ), "culled on the GPU without compacting, the classic pixels too" );
			Assert.AreEqual( 0, Different( classic, fullVertices ), "the prepass from full vertices rather than positions alone, the classic pixels too" );
			Assert.IsNotNull( backend.GpuDrivenVisibilityShaders.Ids, "the id prepass shader is built" );
			Assert.AreEqual( 0, Different( classic, visible ), "forward over only the triangles that own a sample, the classic pixels too" );
			Assert.AreEqual( 0, Different( classic, visibleAgain ), "and the next frame" );
			Assert.IsNotNull( backend.GpuDrivenCullShader, "the culling shader is built, so the GPU-driven frames culled on the GPU" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Aggregates of one material drawn GPU-driven by segments recording on several threads at once (the prepass, the
	/// sun's cascades and forward, each reading the same material and frame attributes) finish every frame and draw what
	/// serial recording does.
	/// </summary>
	[TestMethod]
	public void AggregatesRecordFromParallelSegments()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem();
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "GPU-driven parallel test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 3;

			var material = backend.CreateMaterial( "Shared boxes", new Color( 0.8f, 0.6f, 0.4f ) );
			for ( int row = 0; row < Rows; row++ )
			{
				var (mesh, fragments) = Boxes( backend, material, row * 120 - 120 );
				world.Add( new AggregateObject( mesh, fragments ) );
			}

			// Enough classic draws besides, each its own mesh and material, for the frame to record in parallel
			for ( int i = 0; i < 80; i++ )
			{
				var (small, _) = Boxes( backend, backend.CreateMaterial( $"Mesh boxes {i}", new Color( 0.3f, 0.5f + i / 200.0f, 0.4f ) ), 360 + i / 10 * 20 );
				world.Add( new MeshObject( small, new Transform( new Vector3( 0, 0, i % 10 * 15 ), Rotation.Identity, 0.2f ) ) );
			}

			var view = new RenderView { Name = "GPU-driven parallel test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			(byte[] Pixels, double WorkerMs) Frame( bool parallel )
			{
				system.ParallelRecording = parallel;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();

				// A frame that never finishes recording fails here instead of hanging the run
				var render = System.Threading.Tasks.Task.Run( () => system.Render( world, view, target ) );
				Assert.IsTrue( render.Wait( TimeSpan.FromSeconds( 60 ) ), "the frame finished recording" );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return (pixels, system.Stats.WorkerRecordMs);
			}

			var serial = Frame( false );
			var workerMs = 0.0;
			byte[] parallel = null;
			for ( int i = 0; i < 20; i++ )
			{
				var frame = Frame( true );
				workerMs += frame.WorkerMs;
				parallel ??= frame.Pixels;
				CollectionAssert.AreEqual( parallel, frame.Pixels, $"parallel frame {i} draws what the first did" );
			}

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			Assert.IsTrue( workerMs > 0, "segments recorded on workers" );
			Assert.AreEqual( 0, Different( serial.Pixels, parallel ), "parallel recording draws what serial recording does" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A draw census (<see cref="BackendDevice.Census"/>) of a GPU-driven frame puts every draw call each pass recorded on a
	/// draw it counted: the aggregates' three materials as one forward bucket, with the commands and triangles the GPU drew
	/// read back, and the static mesh as a classic draw that says why it isn't GPU-driven. Counting doesn't change the frame.
	/// </summary>
	[TestMethod]
	public void CensusCountsEveryDraw()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true, GpuDrivenMeshes = false };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "GPU-driven census test" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
			world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
			world.Lighting.SunShadowCascades = 0;

			Color[] colours = [new( 0.8f, 0.6f, 0.4f ), new( 0.3f, 0.7f, 0.4f ), new( 0.4f, 0.5f, 0.9f )];
			for ( int row = 0; row < Rows; row++ )
			{
				var (mesh, fragments) = Boxes( backend, backend.CreateMaterial( $"Census boxes {row}", colours[row] ), row * 120 - 120 );
				world.Add( new AggregateObject( mesh, fragments ) );
			}

			var (own, _) = Boxes( backend, backend.CreateMaterial( "Census mesh boxes", new Color( 0.9f, 0.3f, 0.3f ) ), 240 );
			world.Add( new MeshObject( own, Transform.Zero ) );

			var view = new RenderView { Name = "Census", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			byte[] Frame( DrawCensus census )
			{
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				backend.Census = census;
				try
				{
					system.Render( world, view, target );
				}
				finally
				{
					backend.Census = null;
				}

				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			// The view's first frame draws without ids; the second is the one counted
			Frame( null );
			var plain = Frame( null );
			var census = new DrawCensus();
			var counted = Frame( census );
			census.Resolve( Device );
			var after = Frame( null );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			Assert.AreEqual( 0, Different( plain, counted ), "counting draws the same frame" );
			Assert.AreEqual( 0, Different( plain, after ), "and the frame after it too" );

			var calls = census.Passes.Values.Sum( p => p.Draws );
			Assert.IsTrue( calls > 0, "the passes recorded draws" );
			Assert.AreEqual( calls, census.Draws.Sum( d => d.Calls ), "every draw call a pass recorded is a draw the census counted" );

			var forward = census.Draws.Where( d => d.Path == DrawCensus.Path.Bucket && d.Pass.EndsWith( "Opaque Forward", StringComparison.Ordinal ) ).ToArray();
			Assert.IsTrue( forward.Length > 0, "forward drew buckets" );
			Assert.AreEqual( 1, forward.Select( d => d.Bucket ).Distinct().Count(), "the three materials share one forward bucket" );
			Assert.AreEqual( Rows, forward[0].BucketMaterials, "of three materials" );
			Assert.IsTrue( forward.All( d => d.Commands >= 0 && d.Shader == "complex" ), "its multi-draws' commands were read back" );
			Assert.IsTrue( census.Draws.Where( d => d.Path == DrawCensus.Path.BucketIds ).Sum( d => d.Triangles ) >= Rows * 4 * 12, "the prepass drew every box in view as ids" );

			var meshDraws = census.Draws.Where( d => d.Path == DrawCensus.Path.Classic && d.Material == "Census mesh boxes" ).ToArray();
			Assert.IsTrue( meshDraws.Length > 0, "the static mesh drew classic" );
			Assert.IsTrue( meshDraws.All( d => d.Kind == "static mesh: GPU-driven meshes off" ), $"and says why ({string.Join( ", ", meshDraws.Select( d => d.Kind ).Distinct() )})" );
			Assert.IsTrue( meshDraws.All( d => d.Triangles == 5 * 12 ), "all five of its boxes" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A shader with no variant beside it (a package's) gets one from the library's compiler: none while it compiles,
	/// then the compiled one, cached, so another library loads it without compiling.
	/// </summary>
	[TestMethod]
	public void PackageShaderVariantCompilesAtLoad()
	{
		var files = ShaderFileTests.Files;
		var compiled = files.Find( "shaders/depth_only.gpudriven.shader" );
		if ( compiled is null ) Assert.Inconclusive( "depth_only has no GPU-driven variant: ShaderCompiler -gpudriven" );

		var folder = Path.Combine( Path.GetTempPath(), $"basalt-gpudriven-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( Path.Combine( folder, "package", "shaders" ) );
		File.Copy( files.Find( "shaders/depth_only.shader" ), Path.Combine( folder, "package", "shaders", "depth_only.shader_c" ) );

		try
		{
			using var started = new ManualResetEventSlim();
			using var go = new ManualResetEventSlim();
			var compiles = 0;

			using ( var library = new ShaderLibrary( Device, new ContentFiles( Path.Combine( folder, "package" ) ) ) )
			{
				library.GpuDrivenCache = Path.Combine( folder, "cache" );
				library.GpuDrivenCompiler = ( source, output ) =>
				{
					Interlocked.Increment( ref compiles );
					started.Set();
					go.Wait( 10000 );
					Directory.CreateDirectory( Path.GetDirectoryName( output ) );
					File.Copy( compiled, output );
					return true;
				};

				var shader = library.Load( "shaders/depth_only.shader" );
				Assert.IsNull( library.GpuDriven( shader ), "none while it compiles" );
				Assert.IsTrue( started.Wait( 10000 ) && library.GpuDrivenPending( shader ), "compiling" );
				Assert.IsNull( library.GpuDriven( shader ), "still none" );
				go.Set();

				ShaderFile variant = null;
				for ( int i = 0; i < 1000 && variant is null; i++ )
				{
					variant = library.GpuDriven( shader );
					if ( variant is null ) Thread.Sleep( 10 );
				}

				Assert.IsNotNull( variant, "the compiled variant" );
				Assert.IsFalse( library.GpuDrivenPending( shader ) );
				Assert.AreEqual( 1, compiles, "compiled once" );
			}

			using ( var library = new ShaderLibrary( Device, new ContentFiles( Path.Combine( folder, "package" ) ) ) )
			{
				library.GpuDrivenCache = Path.Combine( folder, "cache" );
				library.GpuDrivenCompiler = ( _, _ ) => throw new AssertFailedException( "the cached variant compiled again" );
				Assert.IsNotNull( library.GpuDriven( library.Load( "shaders/depth_only.shader" ) ), "from the cache" );

				// Another compiler version's variants go
				var stale = Path.Combine( folder, "cache", "depth_only.0000.ffffffff.gpudriven.shader_c" );
				File.WriteAllText( stale, "" );
				library.PruneGpuDrivenCache();
				Assert.IsFalse( File.Exists( stale ), "another version's variant is pruned" );
				Assert.AreEqual( 1, Directory.GetFiles( Path.Combine( folder, "cache" ) ).Length, "this version's variant stays" );
			}
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// Four boxes along x at <paramref name="y"/> and a fifth beyond the far plane, which culling drops, in world space in
	/// one mesh, and a fragment per box.
	/// </summary>
	static (RenderMesh Mesh, AggregateObject.FragmentDraw[] Fragments) Boxes( BackendDevice backend, GpuMaterial material, float y )
	{
		const int count = 5;
		var vertices = new List<MeshVertex>();
		var indices = new List<uint>();
		var fragments = new AggregateObject.FragmentDraw[count];
		for ( int b = 0; b < count; b++ )
		{
			var centre = b < 4 ? new Vector3( b * 120 - 180, y, 40 ) : new Vector3( 0, y, -6000 );
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
