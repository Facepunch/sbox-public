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
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
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
