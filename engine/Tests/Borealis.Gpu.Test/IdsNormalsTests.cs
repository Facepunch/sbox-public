using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2;
using Sandbox;
using System.IO;
using System.Threading;
using N = System.Numerics;

namespace BorealisGpuTests;

/// <summary>
/// Normals from the visibility ids (<see cref="BackendDevice.IdsNormals"/>; docs/reference/borealis-gpu-driven.md, "Normals
/// from the ids"): each pixel's triangle found from its id, as a ray cast on the CPU finds it.
/// </summary>
[TestClass]
[DoNotParallelize]
public class IdsNormalsTests : GpuTest
{
	const int Width = 320, Height = 200;

	/// <summary>
	/// Instances of one box in one aggregate, each turned its own way, share their vertex bits: each pixel's normal is still
	/// its own instance's face's, as a ray from the camera through the pixel meets it, and almost every pixel inside a face has
	/// one. Pixels on no box have none.
	/// </summary>
	[TestMethod]
	[DataRow( 1 )]
	[DataRow( 4 )]
	public void EachPixelGetsItsInstancesFace( int samples )
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault();
		if ( core is not null ) Borealis.Backend.Meshlets.MeshOptimizer.Directory = EngineFolders.NativeIn( Path.GetDirectoryName( core ) );

		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true, Meshlets = true, IdsNormals = true };
		foreach ( var shader in (string[])["shaders/complex.shader", "shaders/depth_only.shader"] )
		{
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( shader ) ) is null ) Assert.Inconclusive( $"{shader} has no GPU-driven variant: ShaderCompiler -gpudriven" );
		}

		if ( backend.IdsNormalsShader is null ) Assert.Inconclusive( "borealis_ids_normals_cs isn't built, or pass modules are off" );
		if ( backend.MeshletStore is null ) Assert.Inconclusive( "meshopt isn't built" );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), samples, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Ids normals test" );

			var (world, view, placed, box) = BoxScene( backend );

			IdsNormalsLayer layer = null;
			byte[] Frame()
			{
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				layer ??= system.Layers.OfType<IdsNormalsLayer>().First();
				if ( layer.LastResolved is null ) return null;
				var (texture, layout) = BackendDevice.Unwrap( layer.LastResolved );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( layer.LastResolved, ImageLayout.CopySource );
				return pixels;
			}

			// The box's meshlets and first-corner table built and published
			byte[] normals = null;
			for ( int i = 0; i < 300 && (i < 4 || backend.MeshletStore is { Pending: > 0 }); i++ )
			{
				normals = Frame();
				if ( backend.MeshletStore is { Pending: > 0 } ) Thread.Sleep( 10 );
			}

			normals = Frame();
			Assert.IsNotNull( normals, "the layer resolved the ids" );
			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );

			// What a ray through each pixel's centre meets: the instance, the face and its normal
			N.Matrix4x4.Invert( view.WorldToProjection, out var clipToWorld );
			var hits = new (int Instance, int Face, Vector3 Normal)[Width * Height];
			for ( int y = 0; y < Height; y++ )
			{
				for ( int x = 0; x < Width; x++ )
				{
					var ndc = new N.Vector2( (x + 0.5f) / Width * 2 - 1, 1 - (y + 0.5f) / Height * 2 );
					var near = N.Vector4.Transform( new N.Vector4( ndc, 1, 1 ), clipToWorld );
					var far = N.Vector4.Transform( new N.Vector4( ndc, 0.01f, 1 ), clipToWorld );
					var origin = new Vector3( near.X, near.Y, near.Z ) / near.W;
					var direction = new Vector3( far.X, far.Y, far.Z ) / far.W - origin;
					hits[y * Width + x] = Cast( placed, box, origin, direction );
				}
			}

			var (inside, found, wrong, outside, stray) = (0, 0, 0, 0, 0);
			var firstWrong = new List<string>();
			for ( int y = 1; y < Height - 1; y++ )
			{
				for ( int x = 1; x < Width - 1; x++ )
				{
					var at = y * Width + x;
					var hit = hits[at];
					var n = new Vector3( (float)BitConverter.ToHalf( normals, at * 8 ), (float)BitConverter.ToHalf( normals, at * 8 + 2 ), (float)BitConverter.ToHalf( normals, at * 8 + 4 ) );
					var has = (float)BitConverter.ToHalf( normals, at * 8 + 6 ) > 0;
					// Inside a face, or off every box: its neighbours meet the same (sample 0 isn't the pixel's centre with MSAA)
					var edge = false;
					foreach ( var neighbour in (int[])[at - 1, at + 1, at - Width, at + Width] ) edge |= hits[neighbour].Instance != hit.Instance || hits[neighbour].Face != hit.Face;
					if ( edge ) continue;

					if ( hit.Instance < 0 )
					{
						outside++;
						if ( has ) stray++;
						continue;
					}

					inside++;
					if ( !has ) continue;
					found++;
					if ( Vector3.Dot( n, hit.Normal ) < 0.999f )
					{
						wrong++;
						if ( firstWrong.Count < 6 ) firstWrong.Add( $"({x},{y}) {n} against {hit.Normal}" );
					}
				}
			}

			Assert.IsTrue( inside > 2000, $"the boxes cover the frame ({inside} pixels inside faces)" );
			Assert.AreEqual( 0, wrong, $"each pixel's normal is its own instance's face's: {string.Join( ", ", firstWrong )}" );
			Assert.IsTrue( found >= inside * 0.99, $"almost every pixel inside a face has one ({found} of {inside})" );
			Assert.AreEqual( 0, stray, $"pixels on no box have none ({stray} of {outside})" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// One box model drawn five times in one aggregate, each turned and tipped its own way, and a view of them: what each
	/// instance's transform is, and the model's bounds. Inconclusive without the model.
	/// </summary>
	static (RenderWorld World, RenderView View, Transform[] Placed, BBox Box) BoxScene( BackendDevice backend )
	{
		var model = backend.LoadModel( "models/dev/box.vmdl" );
		if ( model is null ) Assert.Inconclusive( "models/dev/box.vmdl isn't built" );
		var draw = model.DrawsForLod( 0 ).ToArray().MaxBy( d => d.IndexCount ) with { Material = backend.CreateMaterial( "Ids normals box", new Color( 0.6f, 0.6f, 0.6f ) ) };
		var baseVertex = ((IBackendGeometry)model.Geometry).For( draw.Mesh, draw.DrawCall ).BaseVertex;
		var box = model.Bounds;
		var scale = 80 / MathF.Max( MathF.Max( box.Size.x, box.Size.y ), box.Size.z );
		var placed = Enumerable.Range( 0, 5 ).Select( i => new Transform( new Vector3( 0, i * 130 - 260, 0 ), Rotation.From( 15 + i * 9, 20 + i * 17, 5 * i ), scale ) ).ToArray();
		var fragments = placed.Select( ( transform, i ) =>
		{
			var bounds = box.Transform( transform );
			return new AggregateObject.FragmentDraw
			{
				Transform = Matrix3x4.From( transform ),
				BoundsMin = bounds.Mins,
				BoundsMax = bounds.Maxs,
				DrawTint = Vector3.One,
				Args = new GpuBuffer.IndirectDrawIndexedArguments { IndexCount = (uint)draw.IndexCount, InstanceCount = 1, FirstIndex = (uint)draw.StartIndex, BaseVertex = baseVertex, FirstInstance = (uint)i },
			};
		} ).ToArray();

		var world = new RenderWorld();
		world.Lighting.SunShadowCascades = 0;
		world.Lighting.AmbientColor = new Color( 0.3f, 0.3f, 0.3f, 1 );
		world.Add( new AggregateObject( new RenderMesh( model.Geometry, model.Bounds, [[draw]] ), fragments ) { IsStatic = true } );

		var view = new RenderView { Name = "Ids normals test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = Color.Black, Viewport = new Rect( 0, 0, Width, Height ) };
		view.Position = new Vector3( -600, 30, 160 );
		view.Rotation = Rotation.LookAt( new Vector3( 600, -30, -160 ).Normal );
		return (world, view, placed, box);
	}

	/// <summary>
	/// The frame is the same with normals from the ids as without: the wider ids, cleared once a view instead of by each call,
	/// and each call's mark marking only its own ids, draw what the visibility-driven forward pass drew before.
	/// </summary>
	[TestMethod]
	public void TheFrameIsTheSameWithIdsNormals()
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault();
		if ( core is not null ) Borealis.Backend.Meshlets.MeshOptimizer.Directory = EngineFolders.NativeIn( Path.GetDirectoryName( core ) );

		byte[] Draw( bool idsNormals )
		{
			using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true, Meshlets = true, IdsNormals = idsNormals };
			if ( backend.Shaders.GpuDriven( backend.Shaders.Load( "shaders/complex.shader" ) ) is null ) Assert.Inconclusive( "complex.shader has no GPU-driven variant: ShaderCompiler -gpudriven" );
			var previousHost = RenderHost.Current;
			RenderHost.Current = new BackendRenderHost( backend );
			try
			{
				using var system = new RenderSystem { ParallelRecording = false };
				using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 4, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Ids normals frame test" );
				var (world, view, _, _) = BoxScene( backend );
				for ( int i = 0; i < 300 && (i < 4 || backend.MeshletStore is { Pending: > 0 }); i++ )
				{
					backend.BeginFrame();
					RenderHost.Current.NextFrame();
					system.Render( world, view, target );
					Device.WaitIdle();
					if ( backend.MeshletStore is { Pending: > 0 } ) Thread.Sleep( 10 );
				}

				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Resolved );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Resolved, ImageLayout.CopySource );
				return pixels;
			}
			finally
			{
				Device.WaitIdle();
				RenderHost.Current = previousHost;
			}
		}

		var without = Draw( false );
		var with = Draw( true );
		Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
		var differ = 0;
		for ( int i = 0; i < without.Length; i++ )
		{
			if ( without[i] != with[i] ) differ++;
		}

		Assert.AreEqual( 0, differ, "bytes differ" );
	}

	/// <summary>The nearest of the placed boxes a ray meets: its instance, its face (axis * 2 + side) and its world normal, or instance -1.</summary>
	static (int Instance, int Face, Vector3 Normal) Cast( Transform[] placed, BBox box, Vector3 origin, Vector3 direction )
	{
		var best = (Instance: -1, Face: -1, Normal: Vector3.Zero);
		var nearest = float.MaxValue;
		for ( int i = 0; i < placed.Length; i++ )
		{
			var o = placed[i].Rotation.Inverse * (origin - placed[i].Position) / placed[i].Scale.x;
			var d = placed[i].Rotation.Inverse * direction / placed[i].Scale.x;
			var (enter, leave, face) = (float.MinValue, float.MaxValue, -1);
			for ( int axis = 0; axis < 3; axis++ )
			{
				var (oa, da) = (o[axis], d[axis]);
				if ( MathF.Abs( da ) < 1e-12f )
				{
					if ( oa < box.Mins[axis] || oa > box.Maxs[axis] ) enter = float.MaxValue;
					continue;
				}

				var (t0, t1) = ((box.Mins[axis] - oa) / da, (box.Maxs[axis] - oa) / da);
				var side = t0 < t1 ? 0 : 1;
				if ( MathF.Min( t0, t1 ) > enter ) (enter, face) = (MathF.Min( t0, t1 ), axis * 2 + side);
				leave = MathF.Min( leave, MathF.Max( t0, t1 ) );
			}

			if ( enter > leave || enter < 0 || enter >= nearest ) continue;
			nearest = enter;
			var local = Vector3.Zero;
			local[face / 2] = face % 2 == 0 ? -1 : 1;
			best = (i, face, placed[i].Rotation * local);
		}

		return best;
	}
}
