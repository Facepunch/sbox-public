using Basalt;
using Basalt.Backend;
using Basalt.Gpu;
using Sandbox;

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
	/// bucket for all three materials instead of a bind each; and with a bucket per material.
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

			var view = new RenderView { Name = "GPU-driven test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -500, -300, 300 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );

			byte[] Frame( bool gpuDriven, out int binds, bool materialBuckets )
			{
				backend.GpuDriven = gpuDriven;
				backend.GpuDrivenMaterialBuckets = materialBuckets;
				backend.TimeDraws = true;
				backend.DrawTimes.Reset();
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				binds = (int)backend.DrawTimes.Binds;
				backend.TimeDraws = false;
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			var classic = Frame( false, out var classicBinds, false );
			var driven = Frame( true, out var drivenBinds, false );
			var drawRecords = backend.GpuDrivenTables.Records.Draws;
			var again = Frame( true, out _, false );
			Assert.AreEqual( drawRecords, backend.GpuDrivenTables.Records.Draws, "the second frame reuses the aggregates' draw records" );
			var perMaterial = Frame( true, out var perMaterialBinds, true );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			Assert.AreEqual( 0, Different( classic, perMaterial ), "a bucket per material draws the classic pixels" );
			Assert.IsTrue( perMaterialBinds > drivenBinds, $"a bucket per material binds more ({perMaterialBinds}) than one bucket ({drivenBinds})" );
			Assert.IsTrue( backend.GpuDrivenTables.Records.Draws > 1, $"the fragments have draw records ({backend.GpuDrivenTables.Records.Draws})" );
			Assert.IsTrue( drivenBinds < classicBinds, $"fewer binds GPU-driven ({drivenBinds}) than classic ({classicBinds})" );
			Assert.AreEqual( 0, Different( classic, driven ), "GPU-driven draws the classic pixels" );
			CollectionAssert.AreEqual( driven, again, "the same frame twice" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>Four boxes along x at <paramref name="y"/>, in world space in one mesh, and a fragment per box.</summary>
	static (RenderMesh Mesh, AggregateObject.FragmentDraw[] Fragments) Boxes( BackendDevice backend, GpuMaterial material, float y )
	{
		const int count = 4;
		var vertices = new List<MeshVertex>();
		var indices = new List<uint>();
		var fragments = new AggregateObject.FragmentDraw[count];
		for ( int b = 0; b < count; b++ )
		{
			var centre = new Vector3( b * 120 - 180, y, 40 );
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
