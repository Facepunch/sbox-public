using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// Skinned models on Borealis.Gpu: posed by their bones, through the vertex shader for one weight and through compute
/// skinning and the vertex cache for more, as Borealis poses them on Source 2's device.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SkinningTests : GpuTest
{
	const string Citizen = "models/citizen/citizen.vmdl";
	const string Human = "models/citizen_human/human.vmdl";
	const int Width = 192, Height = 192;

	/// <summary>
	/// The citizen read by Borealis.Source2 has its skinning: every mesh with bones has them mapped to model bones, and its
	/// many-weight meshes skin through the vertex cache.
	/// </summary>
	[TestMethod]
	public void ModelHasSkinning()
	{
		using var backend = new BackendDevice( Device );
		var mesh = backend.LoadModel( Citizen );
		if ( mesh is null ) Assert.Inconclusive( $"{Citizen} isn't built" );

		Assert.IsTrue( mesh.IsSkinned );
		Assert.IsTrue( mesh.BindPose.Length > 0 );
		var vertexCache = 0;
		for ( int m = 0; m < mesh.ModelMeshCount; m++ )
		{
			if ( mesh.SkinFor( m ) is not { } skin ) continue;
			Assert.AreEqual( skin.InverseBindPoses.Length, skin.MasterBones.Length );
			Assert.IsTrue( skin.MasterBones.All( b => b >= 0 && b < mesh.BindPose.Length ), $"mesh {m}'s bones are model bones" );
			Assert.IsTrue( skin.VertexCount > 0 );
			if ( skin.UsesVertexCache ) vertexCache++;
		}

		Assert.IsTrue( vertexCache > 0, "some meshes skin through the vertex cache" );
	}

	/// <summary>
	/// Bones move the model: the citizen with every bone raised and turned is drawn where the same citizen in its bind
	/// pose is drawn when the object itself is moved that way, and not where it stands unmoved.
	/// </summary>
	[TestMethod]
	public void BonesPoseTheModel()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );

		try
		{
			var mesh = backend.LoadModel( Citizen );
			if ( mesh is null ) Assert.Inconclusive( $"{Citizen} isn't built" );

			var moved = new Transform( new Vector3( 0, 12, 6 ), Rotation.FromYaw( 35 ) );
			var bindPose = mesh.BindPose.Select( ToTransform ).ToArray();
			var posed = bindPose.Select( x => moved.ToWorld( x ) ).ToArray();

			var still = Render( mesh, Transform.Zero, null );
			var objectMoved = Render( mesh, moved, null );
			var bonesMoved = Render( mesh, Transform.Zero, posed );

			var toObject = MeanDifference( bonesMoved, objectMoved );
			var toStill = MeanDifference( bonesMoved, still );
			Assert.IsTrue( MeanDifference( objectMoved, still ) > 2, "moving the object changes the picture" );
			Assert.IsTrue( toObject < 0.25, $"posed by bones as the moved object: mean difference {toObject:F3}" );
			Assert.IsTrue( toStill > 2, $"posed by bones, not standing still: mean difference {toStill:F3}" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Morphs move the face: the human's head with every flex controller at its maximum, composited into the morph atlas
	/// and skinned with <c>D_MORPH</c>, differs from it with none set, and with every controller at 0 it doesn't.
	/// </summary>
	[TestMethod]
	public void MorphsMoveTheFace()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );

		try
		{
			var mesh = backend.LoadModel( Human );
			if ( mesh is null ) Assert.Inconclusive( $"{Human} isn't built" );
			var model = ((BackendModel)mesh.Geometry).File;
			var controllers = model.Meshes.Where( x => x.Morphs is not null ).SelectMany( x => x.Morphs.Controllers ).DistinctBy( x => x.Name ).ToArray();
			Assert.IsTrue( controllers.Length > 0, "the human has flex controllers" );

			// The face, close up
			var head = new Vector3( 0, 0, mesh.Bounds.Mins.z + mesh.Bounds.Size.z * 0.93f );
			var none = Render( mesh, Transform.Zero, null, null, head, 0.25f );

			var zero = new BackendMorphs( backend );
			foreach ( var c in controllers ) zero.Set( c.Name, 0 );
			var unweighted = Render( mesh, Transform.Zero, null, zero, head, 0.25f );

			var full = new BackendMorphs( backend );
			foreach ( var c in controllers ) full.Set( c.Name, c.Max );
			var morphed = Render( mesh, Transform.Zero, null, full, head, 0.25f );
			Save( morphed, Width, Height, "skinning-morphed-face" );

			var toNone = MeanDifference( morphed, none );
			Assert.IsTrue( toNone > 1, $"morphs change the face: mean difference {toNone:F3}" );
			Assert.IsTrue( MeanDifference( unweighted, none ) < 0.05, "nothing weighted, nothing moves" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Skinned draws go GPU-driven (<see cref="BackendDevice.GpuDrivenSkinned"/>): the posed citizen, its many-weight meshes from
	/// the vertex cache and its one-weight ones skinned in the vertex shader, depth, shadows and forward, draws in more
	/// GPU-driven batches than with the switch off, and looks the same.
	/// </summary>
	[TestMethod]
	public void SkinnedDrawsGpuDriven()
	{
		var previousHost = RenderHost.Current;
		try
		{
			(byte[] Pixels, long Batches) Draw( bool skinned )
			{
				using var backend = new BackendDevice( Device ) { GpuDrivenSkinned = skinned, TimeDraws = true };
				RenderHost.Current = new BackendRenderHost( backend );
				var mesh = backend.LoadModel( Citizen );
				if ( mesh is null ) Assert.Inconclusive( $"{Citizen} isn't built" );

				var moved = new Transform( new Vector3( 0, 12, 6 ), Rotation.FromYaw( 35 ) );
				var posed = mesh.BindPose.Select( ToTransform ).Select( x => moved.ToWorld( x ) ).ToArray();
				Render( mesh, Transform.Zero, posed );
				var batches = backend.DrawTimes.GpuDrivenBatches;
				var pixels = Render( mesh, Transform.Zero, posed );
				return (pixels, backend.DrawTimes.GpuDrivenBatches - batches);
			}

			var classic = Draw( false );
			var gpuDriven = Draw( true );
			Save( gpuDriven.Pixels, Width, Height, "skinning-gpudriven" );

			Assert.IsTrue( gpuDriven.Batches > classic.Batches, $"skinned draws go GPU-driven: {gpuDriven.Batches} batches against {classic.Batches}" );
			var difference = MeanDifference( gpuDriven.Pixels, classic.Pixels );
			Assert.IsTrue( difference < 0.25, $"GPU-driven skinned draws look as the classic ones: mean difference {difference:F3}" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// skin.shader (the citizen's skin, which per-vertex curvature keeps off skin.slang) draws GPU-driven too: its variant
	/// loads the face occlusion's <c>EyeOcclusionViewProj</c> float4x4 from the material record as its classic draw reads
	/// it from its constant buffer, so with a projection that isn't symmetric set by the view, GPU-driven and classic look
	/// the same, and closer than with the projection transposed. Set by the object, its draws stay classic.
	/// </summary>
	[TestMethod]
	public void SkinShaderDrawsGpuDriven()
	{
		var previousHost = RenderHost.Current;
		try
		{
			var numerics = new System.Numerics.Matrix4x4( 0, 0.03f, 0, 0, 0, 0, 0.03f, 0, 0.03f, 0, 0, 0, 0, 0, 0, 1 );
			var projection = (Matrix)numerics;
			var transposed = (Matrix)System.Numerics.Matrix4x4.Transpose( numerics );

			(byte[] Pixels, long Batches, string[] Left) Draw( bool skinned, Matrix matrix, bool onObject = false )
			{
				using var backend = new BackendDevice( Device ) { GpuDrivenSkinned = skinned, TimeDraws = true };
				backend.Surfaces.ConvertShaders = false;
				RenderHost.Current = new BackendRenderHost( backend );
				var mesh = backend.LoadModel( Citizen );
				if ( mesh is null ) Assert.Inconclusive( $"{Citizen} isn't built" );

				var skin = backend.LoadMaterial( "skin face occlusion test", new Borealis.Source2.Materials.MaterialFile
				{
					Name = "skin face occlusion test",
					Shader = "shaders/skin.shader",
					Ints = new Dictionary<string, int> { ["F_USE_FACE_OCCLUSION_TEXTURE"] = 1, ["F_USE_PER_VERTEX_CURVATURE"] = 1 },
					Floats = new Dictionary<string, float>(),
					Vectors = new Dictionary<string, float[]>(),
					IntAttributes = new Dictionary<string, int>(),
					Textures = new Dictionary<string, string>
					{
						["g_tColor"] = "models/citizen/skin/citizen_skin_young_color_png_57825f18.generated.vtex",
						// Face occlusion read from a texture that varies everywhere, shown where the combined masks' red is
						// (skin.shader's inside-mouth mask): the colour's is mostly high
						["g_tOcclusion"] = "models/citizen/skin/citizen_skin_young_color_png_57825f18.generated.vtex",
						["g_tCombinedMasks"] = "models/citizen/skin/citizen_skin_young_color_png_57825f18.generated.vtex",
					},
				} );
				if ( skin is not BackendMaterial { Shader: not null } ) Assert.Inconclusive( "skin.shader isn't compiled" );
				if ( backend.Shaders.GpuDriven( ((BackendMaterial)skin).Shader ) is null ) Assert.Inconclusive( "skin.shader's GPU-driven variant isn't built" );

				var attributes = backend.CreateParameters();
				attributes.Set( "EyeOcclusionViewProj", matrix );
				var moved = new Transform( new Vector3( 0, 12, 6 ), Rotation.FromYaw( 35 ) );
				var posed = mesh.BindPose.Select( ToTransform ).Select( x => moved.ToWorld( x ) ).ToArray();
				Render( mesh, Transform.Zero, posed, material: skin, objectAttributes: onObject ? attributes : null, viewAttributes: onObject ? null : attributes );
				var batches = backend.DrawTimes.GpuDrivenBatches;
				backend.GpuDrivenLeftClassic.Clear();
				var pixels = Render( mesh, Transform.Zero, posed, material: skin, objectAttributes: onObject ? attributes : null, viewAttributes: onObject ? null : attributes );
				return (pixels, backend.DrawTimes.GpuDrivenBatches - batches, backend.GpuDrivenLeftClassic.Keys.Where( x => x.Contains( "skin", StringComparison.OrdinalIgnoreCase ) ).ToArray());
			}

			var classic = Draw( false, projection );
			var gpuDriven = Draw( true, projection );
			var classicTransposed = Draw( false, transposed );
			Save( gpuDriven.Pixels, Width, Height, "skin-shader-gpudriven" );
			Save( classic.Pixels, Width, Height, "skin-shader-classic" );

			Assert.IsTrue( gpuDriven.Batches > classic.Batches, $"skin.shader draws go GPU-driven: {gpuDriven.Batches} batches against {classic.Batches}" );
			Assert.AreEqual( 0, gpuDriven.Left.Length, $"no skin.shader draws left classic: {string.Join( ", ", gpuDriven.Left )}" );
			var difference = MeanDifference( gpuDriven.Pixels, classic.Pixels );
			var transposedDifference = MeanDifference( classicTransposed.Pixels, classic.Pixels );
			TestContext.WriteLine( $"GPU-driven against classic {difference:F3}; transposed projection against classic {transposedDifference:F3}" );
			// The citizen covers a small part of the frame, so the projection moves the mean a little (about 0.1)
			Assert.IsTrue( transposedDifference > 0.05, $"the face occlusion's projection shows in the picture: {transposedDifference:F3} with it transposed" );
			Assert.IsTrue( difference < transposedDifference / 4, $"GPU-driven skin.shader looks as the classic one: mean difference {difference:F3}, {transposedDifference:F3} with the projection transposed" );

			var own = Draw( true, projection, onObject: true );
			Assert.IsTrue( own.Left.Any( x => x.Contains( "object sets a matrix constant", StringComparison.Ordinal ) ), $"the object's own projection keeps its draws classic: {string.Join( ", ", own.Left )}" );
			var ownDifference = MeanDifference( own.Pixels, classic.Pixels );
			Assert.IsTrue( ownDifference < transposedDifference / 4, $"set by the object, the projection draws as set by the view: mean difference {ownDifference:F3}" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// With the raw buffer array nearly full, a skinned model made after that still loads (Strike Force's models threw "out
	/// of raw buffer slots" every frame and its frame drew black): its vertex buffers get no slot, leaving the last ones to
	/// the GPU-driven pages and views, its draws draw classic, and it looks as with GPU-driven skinning off.
	/// </summary>
	[TestMethod]
	public void SkinnedModelsLoadWithTheRawBufferArrayNearlyFull()
	{
		var previousHost = RenderHost.Current;
		var fillers = new List<BufferHandle>();
		try
		{
			byte[] classic;
			using ( var backend = new BackendDevice( Device ) { GpuDrivenSkinned = false } )
			{
				RenderHost.Current = new BackendRenderHost( backend );
				var mesh = backend.LoadModel( Citizen );
				if ( mesh is null ) Assert.Inconclusive( $"{Citizen} isn't built" );
				classic = Render( mesh, Transform.Zero, mesh.BindPose.Select( ToTransform ).ToArray() );
			}

			using ( var backend = new BackendDevice( Device ) { GpuDrivenSkinned = true } )
			{
				RenderHost.Current = new BackendRenderHost( backend );

				// Loaded once first, so the GPU-driven pages the second copy's indices go in are there
				var first = backend.LoadModel( Citizen );
				if ( first is null ) Assert.Inconclusive( $"{Citizen} isn't built" );
				Assert.AreEqual( 0, backend.SkinnedBuffersWithoutSlot );

				while ( true )
				{
					var filler = Device.CreateBuffer( new BufferInfo { Size = 16, Usage = BufferUsage.Storage, Name = "raw buffer slot filler" } );
					fillers.Add( filler );
					if ( !Device.TryBindlessIndex( filler, out _, BackendDevice.RawBufferReserve ) ) break;
				}

				var mesh = backend.LoadModel( Citizen );
				Assert.IsNotNull( mesh, "made with the raw buffer array nearly full" );
				Assert.IsTrue( backend.SkinnedBuffersWithoutSlot > 0, "its vertex buffers got no slot" );

				var pixels = Render( mesh, Transform.Zero, mesh.BindPose.Select( ToTransform ).ToArray() );
				Save( pixels, Width, Height, "skinning-no-raw-buffer-slot" );
				var difference = MeanDifference( pixels, classic );
				Assert.IsTrue( difference < 0.25, $"drawn classic, it looks as with GPU-driven skinning off: mean difference {difference:F3}" );

				// With every slot taken it's still made, rather than throwing
				while ( true )
				{
					var filler = Device.CreateBuffer( new BufferInfo { Size = 16, Usage = BufferUsage.Storage, Name = "raw buffer slot filler" } );
					fillers.Add( filler );
					if ( !Device.TryBindlessIndex( filler, out _ ) ) break;
				}

				Assert.IsNotNull( backend.LoadModel( Citizen ), "made with the raw buffer array full" );
			}
		}
		finally
		{
			Device.WaitIdle();
			foreach ( var filler in fillers ) Device.Destroy( filler );
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>A model-space bone (<c>matrix3x4_t</c> rows) as a transform.</summary>
	static Transform ToTransform( Matrix3x4 m )
	{
		var matrix = new System.Numerics.Matrix4x4(
			m.Row0.x, m.Row1.x, m.Row2.x, 0,
			m.Row0.y, m.Row1.y, m.Row2.y, 0,
			m.Row0.z, m.Row1.z, m.Row2.z, 0,
			m.Row0.w, m.Row1.w, m.Row2.w, 1 );
		System.Numerics.Matrix4x4.Decompose( matrix, out _, out var rotation, out var position );
		return new Transform( new Vector3( position.X, position.Y, position.Z ), new Rotation( rotation.X, rotation.Y, rotation.Z, rotation.W ) );
	}

	/// <summary>The model drawn once from a fixed camera, as 8-bit sRGB.</summary>
	byte[] Render( RenderMesh mesh, Transform transform, Transform[] bones, IMorphSource morphs = null, Vector3? focus = null, float distance = 1.4f, GpuMaterial material = null, ShaderParameters objectAttributes = null, ShaderParameters viewAttributes = null, RenderObject extra = null )
	{
		var system = new RenderSystem { ParallelRecording = false };
		var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Skinning" );
		try
		{
			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( -0.4f, 0.6f, -0.7f ).Normal;
			world.Lighting.AmbientColor = new Color( 0.2f, 0.2f, 0.2f, 1 );
			var obj = new MeshObject( mesh, transform ) { MorphSource = morphs, MaterialOverride = material, Attributes = objectAttributes };
			if ( bones is not null )
			{
				obj.SetBones( bones );
				obj.LocalBounds = new BBox( mesh.Bounds.Mins - 40, mesh.Bounds.Maxs + 40 );
			}

			world.Add( obj );
			if ( extra is not null ) world.Add( extra );

			var view = new RenderView { Name = "Skinning", FieldOfView = 60, ClearColor = new Color( 0.1f, 0.2f, 0.9f ), Viewport = new Rect( 0, 0, Width, Height ), ViewAttributes = viewAttributes };
			var size = mesh.Bounds.Size.Length;
			view.Rotation = Rotation.From( 20, 160, 0 );
			view.Position = (focus ?? mesh.Bounds.Center) - view.Rotation.Forward * size * distance;
			view.ZNear = size * 0.05f;
			view.ZFar = size * 10;

			Device.BeginFrame();
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );
			Device.WaitIdle();

			var (texture, layout) = BackendDevice.Unwrap( target.Color );
			return ToRgba8( ReadBack( texture, layout ) );
		}
		finally
		{
			Device.WaitIdle();
			target.Dispose();
			system.Dispose();
		}
	}

	/// <summary>
	/// A frame that needs more of the vertex cache than its region after its skinning was recorded (a nested view, here an
	/// object that draws before the citizen's colour pass) doesn't move the cache to another buffer under it: the draws
	/// recorded after still read what the skinning wrote, and the citizen draws as in a frame that needed no more.
	/// </summary>
	[TestMethod]
	public void VertexCacheRoomMidFrameKeepsWhatTheFrameSkinned()
	{
		using var backend = new BackendDevice( Device ) { GpuDriven = false };
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );

		try
		{
			var mesh = backend.LoadModel( Citizen );
			if ( mesh is null ) Assert.Inconclusive( $"{Citizen} isn't built" );
			var posed = mesh.BindPose.Select( ToTransform ).ToArray();

			var plain = Render( mesh, Transform.Zero, posed );
			Render( mesh, Transform.Zero, posed );
			var hog = new VertexCacheHog( backend ) { IsOpaque = true, LocalBounds = new BBox( new Vector3( -10000, -10000, -10000 ), new Vector3( 10000, 10000, 10000 ) ) };
			var asked = Render( mesh, Transform.Zero, posed, extra: hog );
			Assert.IsTrue( hog.Asked, "the object asked for more of the cache mid-frame" );
			Assert.IsTrue( MeanDifference( plain, asked ) < 0.5, $"the citizen drew as before ({MeanDifference( plain, asked ):F2} apart)" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>Asks for a whole region of the vertex cache the first time it draws.</summary>
	sealed class VertexCacheHog( BackendDevice backend ) : CustomObject
	{
		public bool Asked;

		internal override void Render( GpuContext context, ViewPass view, MeshLayer layer )
		{
			if ( Asked ) return;
			Asked = true;
			var capacity = (int)typeof( BackendDevice ).GetField( "vertexCacheCapacity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance ).GetValue( backend );
			backend.AllocateVertexCache( capacity );
		}
	}

	static double MeanDifference( byte[] a, byte[] b )
	{
		long total = 0;
		for ( int i = 0; i < a.Length; i++ )
		{
			if ( (i & 3) != 3 ) total += Math.Abs( a[i] - b[i] );
		}

		return total / (a.Length * 0.75);
	}

	static byte[] ToRgba8( byte[] halfs )
	{
		var pixels = new byte[Width * Height * 4];
		for ( int i = 0; i < pixels.Length; i++ )
		{
			var linear = Math.Clamp( (float)BitConverter.ToHalf( halfs, i * 2 ), 0, 1 );
			pixels[i] = (byte)(MathF.Pow( linear, 1 / 2.2f ) * 255 + 0.5f);
		}

		return pixels;
	}
}
