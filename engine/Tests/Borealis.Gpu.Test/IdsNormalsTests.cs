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
public class IdsNormalsTests : RenderGpuTest
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
		GpuDrivenTests.RequireGpuDrivenDepth( backend );

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
	/// Object motion from the ids (<see cref="IdsNormalsLayer.LastMotion"/>): a turned box slid since last frame has the slide back
	/// at every pixel, one that stayed has zeros with its object known, and the sky has none.
	/// </summary>
	[TestMethod]
	public void MovedBoxesGetTheirOwnMotion()
	{
		MeshObject slider = null;
		WithMotion( backend =>
		{
			var model = backend.LoadModel( "models/dev/box.vmdl" );
			if ( model is null ) Assert.Inconclusive( "models/dev/box.vmdl isn't built" );
			var draw = model.DrawsForLod( 0 ).ToArray().MaxBy( d => d.IndexCount ) with { Material = backend.CreateMaterial( "Object motion box", new Color( 0.6f, 0.6f, 0.6f ) ) };
			var mesh = new RenderMesh( model.Geometry, model.Bounds, [[draw]] );
			var scale = 90 / MathF.Max( MathF.Max( model.Bounds.Size.x, model.Bounds.Size.y ), model.Bounds.Size.z );

			// Left of the view (+y) and right of it
			var world = new RenderWorld();
			world.Lighting.SunShadowCascades = 0;
			world.Add( slider = new MeshObject( mesh, new Transform( new Vector3( 0, 120, 0 ), Rotation.From( 10, 25, 0 ), scale ) ) );
			world.Add( new MeshObject( mesh, new Transform( new Vector3( 0, -120, 0 ), Rotation.From( 20, 40, 10 ), scale ) ) );
			var view = new RenderView { Name = "Object motion test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = Color.Black, Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -480, 0, 120 );
			view.Rotation = Rotation.LookAt( new Vector3( 480, 0, -120 ).Normal );
			return (world, view);
		}, frame =>
		{
			var slide = new Vector3( 0, 6, 0 );
			slider.Transform = slider.Transform.WithPosition( slider.Transform.Position + slide );
			var texels = Texels( frame() );
			var slid = texels.Where( t => t.Known && t.X < Width / 2 ).ToArray();
			var stayed = texels.Where( t => t.Known && t.X >= Width / 2 ).ToArray();

			Assert.IsTrue( slid.Length > 1000 && stayed.Length > 1000, $"the boxes cover their halves ({slid.Length}, {stayed.Length})" );
			Assert.IsTrue( slid.All( t => (t.Motion + slide).Length < 0.05f ), "the slid box's pixels move by the slide back" );
			Assert.IsTrue( stayed.All( t => t.Motion.Length < 1e-3f ), "the still box's pixels don't move" );
			Assert.IsTrue( texels.All( t => t.Known || t.Motion == Vector3.Zero ), "the sky has no motion" );
		} );
	}

	/// <summary>
	/// Skinned object motion from the ids, from each pixel's first vertex's bones this frame and last: the citizen's turned
	/// skeleton slid as a whole gives every pixel the slide back (one-weight meshes posed in the vertex shader, many-weight ones
	/// through the vertex cache); then only its head bone nudged moves only pixels near its top, each by up to the nudge back.
	/// </summary>
	[TestMethod]
	public void SkinnedBonesGetTheirOwnMotion()
	{
		RenderMesh mesh = null;
		MeshObject citizen = null;
		Transform[] bindPose = null;
		var pose = new Transform( Vector3.Zero, Rotation.FromYaw( 30 ) );
		WithMotion( backend =>
		{
			mesh = backend.LoadModel( "models/citizen/citizen.vmdl" );
			if ( mesh is null ) Assert.Inconclusive( "models/citizen/citizen.vmdl isn't built" );
			bindPose = mesh.BindPose.Select( SkinningTests.ToTransform ).ToArray();
			citizen = new MeshObject( mesh, Transform.Zero ) { LocalBounds = new BBox( mesh.Bounds.Mins - 200, mesh.Bounds.Maxs + 200 ) };
			citizen.SetBones( bindPose.Select( b => pose.ToWorld( b ) ).ToArray() );

			var world = new RenderWorld();
			world.Lighting.SunShadowCascades = 0;
			world.Add( citizen );
			var view = new RenderView { Name = "Skinned motion test", FieldOfView = 50, ZNear = 4, ZFar = 2000, ClearColor = Color.Black, Viewport = new Rect( 0, 0, Width, Height ) };
			view.Rotation = Rotation.From( 10, 180, 0 );
			view.Position = mesh.Bounds.Center - view.Rotation.Forward * mesh.Bounds.Size.Length * 1.3f;
			return (world, view);
		}, frame =>
		{
			var slide = new Vector3( 0, 3, 1 );
			var bones = bindPose.Select( b => pose.WithPosition( slide ).ToWorld( b ) ).ToArray();
			citizen.SetBones( bones );
			var texels = Texels( frame() ).Where( t => t.Known ).ToArray();
			Assert.IsTrue( texels.Length > 2000, $"the citizen's pixels know their motion ({texels.Length})" );
			Assert.IsTrue( texels.All( t => (t.Motion + slide).Length < 0.05f ), "each moved by the slide back" );

			// The highest bone the skins weight: a wrong bone index would move the wrong pixels
			var head = Enumerable.Range( 0, mesh.ModelMeshCount ).Select( m => mesh.SkinFor( m ) ).Where( s => s is not null ).SelectMany( s => s.MasterBones ).MaxBy( b => bindPose[b].Position.z );
			var nudge = new Vector3( 0, 4, 0 );
			bones[head] = bones[head].WithPosition( bones[head].Position + nudge );
			citizen.SetBones( bones );
			texels = Texels( frame() ).Where( t => t.Known ).ToArray();
			var moved = texels.Where( t => t.Motion.Length >= 0.05f ).ToArray();
			var (top, bottom) = (texels.Min( t => t.Y ), texels.Max( t => t.Y ));
			bool Nudged( Vector3 m )
			{
				var along = -Vector3.Dot( m, nudge ) / nudge.LengthSquared;
				return along is >= 0 and <= 1.01f && (m + nudge * along).Length < 0.05f;
			}

			Assert.IsTrue( moved.Length >= 20 && moved.Length * 2 < texels.Length, $"the head's pixels move and the rest stay ({moved.Length} of {texels.Length})" );
			Assert.IsTrue( moved.All( t => Nudged( t.Motion ) ), "each moved back along the nudge by its weight" );
			Assert.IsTrue( moved.Average( t => t.Y ) < top + (bottom - top) * 0.3f, $"the moved pixels are near the top (rows {top} to {bottom})" );
		} );
	}

	/// <summary><c>Motion::Get</c>'s callers bind the object motion: <c>motion_vectors</c>, the upscalers' input, reads it.</summary>
	[TestMethod]
	public void MotionVectorsReadObjectMotion()
	{
		using var backend = new BackendDevice( Device );
		using var drawer = new ShaderDrawer( backend );
		var shader = backend.Shaders.Load( "shaders/motion_vectors.shader" );
		if ( shader is null ) Assert.Inconclusive( "motion_vectors isn't built" );
		var variant = drawer.Variant( shader, new ShaderAttributes() );
		Assert.IsTrue( variant?.Resources.Any( r => r.Name == "BorealisObjectMotion" ) == true, "motion_vectors reads the object motion" );
	}

	/// <summary>
	/// <paramref name="test"/> on the scene <paramref name="scene"/> makes, drawn GPU-driven with the ids resolved: it gets a function
	/// that draws a frame and returns its object motion (RGBA16F texels), once the meshlets are published.
	/// </summary>
	void WithMotion( Func<BackendDevice, (RenderWorld World, RenderView View)> scene, Action<Func<byte[]>> test )
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault();
		if ( core is not null ) Borealis.Backend.Meshlets.MeshOptimizer.Directory = EngineFolders.NativeIn( Path.GetDirectoryName( core ) );

		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenMeshes = true, GpuDrivenSkinned = true, GpuDrivenVisibility = true, Meshlets = true, IdsNormals = true };
		GpuDrivenTests.RequireGpuDrivenDepth( backend );
		if ( backend.IdsNormalsShader is null ) Assert.Inconclusive( "borealis_ids_normals_cs isn't built, or pass modules are off" );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			var (world, view) = scene( backend );
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Object motion test" );
			byte[] Frame()
			{
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				if ( system.Layers.OfType<IdsNormalsLayer>().First().LastMotion is not { } motion ) return null;
				var (texture, layout) = BackendDevice.Unwrap( motion );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( motion, ImageLayout.CopySource );
				return pixels;
			}

			for ( int i = 0; i < 300 && (i < 4 || backend.MeshletStore is { Pending: > 0 }); i++ )
			{
				Frame();
				if ( backend.MeshletStore is { Pending: > 0 } ) Thread.Sleep( 10 );
			}

			test( () => Frame() ?? throw new AssertFailedException( "the layer resolved the ids" ) );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>The object motion's texels: where each is, its world motion, and whether its object's last frame was known.</summary>
	static (int X, int Y, Vector3 Motion, bool Known)[] Texels( byte[] motion ) => Enumerable.Range( 0, Width * Height ).Select( i =>
	{
		float Half( int c ) => (float)BitConverter.ToHalf( motion, i * 8 + c * 2 );
		return (i % Width, i / Width, new Vector3( Half( 0 ), Half( 1 ), Half( 2 ) ), Half( 3 ) > 0);
	} ).ToArray();

	/// <summary>
	/// The ids' readers declare their image with an unknown format: the ids target is <c>R32_UINT</c> without normals from the
	/// ids and <c>R32G32_UINT</c> with them, and an image typed with a format (Slang types a <c>uint2</c> texture
	/// <c>Rg32ui</c>) reads undefined values through a view of another.
	/// </summary>
	[TestMethod]
	public void IdsReadersTakeEitherTarget()
	{
		using var backend = new BackendDevice( Device );
		using var drawer = new ShaderDrawer( backend );
		var images = 0;
		foreach ( var path in (string[])["shaders/borealis_gpu_driven_mark_cs.shader", "shaders/borealis_ids_normals_cs.shader"] )
		{
			var shader = backend.Shaders.Load( path );
			if ( shader is null ) Assert.Inconclusive( $"{path} isn't built" );
			foreach ( var msaa in (int[])[0, 1] )
			{
				var attributes = new ShaderAttributes();
				attributes.SetCombo( "D_MSAA", msaa );
				var variant = drawer.Variant( shader, attributes );
				var spirv = variant?.Compute?.Bytecode?.Spirv ?? variant?.Compute?.Bytecode?.Shared?.Spirv;
				Assert.IsNotNull( spirv, $"{path} D_MSAA {msaa} compiled" );
				var ids = variant.Resources.Single( r => r.Name == "BorealisIds" );
				Assert.AreEqual( 0u, ImageFormatAt( spirv, ids.Set, ids.Binding ), $"{path} D_MSAA {msaa}: g_tIds's image format (0 is Unknown)" );
				images++;
			}
		}

		Assert.AreEqual( 4, images );
	}

	/// <summary>The format operand of the image type of the SPIR-V's variable at a set and binding.</summary>
	static uint ImageFormatAt( byte[] bytes, int set, int binding )
	{
		var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>( bytes ).ToArray();
		var sets = new Dictionary<uint, uint>();
		var bindings = new Dictionary<uint, uint>();
		var types = new Dictionary<uint, uint[]>();
		var variables = new Dictionary<uint, uint>();
		for ( int at = 5; at < words.Length; )
		{
			var (op, count) = (words[at] & 0xffff, (int)(words[at] >> 16));
			var instruction = words.AsSpan( at, count );
			if ( op == 71 && instruction[2] == 34 ) sets[instruction[1]] = instruction[3];       // OpDecorate DescriptorSet
			if ( op == 71 && instruction[2] == 33 ) bindings[instruction[1]] = instruction[3];   // OpDecorate Binding
			if ( op is 25 or 27 or 28 or 29 or 32 ) types[instruction[1]] = instruction.ToArray();   // image, sampled image, arrays, pointer
			if ( op == 59 ) variables[instruction[2]] = instruction[1];                         // OpVariable: its pointer type
			at += count;
		}

		var variable = variables.Keys.Single( v => sets.TryGetValue( v, out var s ) && s == set && bindings.TryGetValue( v, out var b ) && b == binding );
		var type = types[variables[variable]];
		while ( (type[0] & 0xffff) != 25 ) type = types[(type[0] & 0xffff) == 32 ? type[3] : type[2]];
		return type[8];
	}

	/// <summary>
	/// One box model drawn five times in one aggregate, each turned and tipped its own way, and a view of them: what each
	/// instance's transform is, and the model's bounds. Inconclusive without the model.
	/// </summary>
	internal static (RenderWorld World, RenderView View, Transform[] Placed, BBox Box) BoxScene( BackendDevice backend )
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
			GpuDrivenTests.RequireGpuDrivenDepth( backend );
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
