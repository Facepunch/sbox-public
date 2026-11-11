using Basalt;
using Basalt.Backend;
using Basalt.Gpu;
using Sandbox;

namespace BasaltGpuTests;

/// <summary>
/// Per-object attributes on Basalt.Gpu (<c>BackendContext.SetObjectAttributes</c>): an object's own attributes, its
/// lightmap and its light probe reach its draws, and only its draws, through material objects as by reflection, without
/// writing sets frame after frame. Also the draws that used to record nothing: blendable materials' forward mode.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ObjectAttributeTests : GpuTest
{
	const int Width = 480, Height = 240;
	const int Boxes = 4;

	static readonly Vector3[] Centres = [new( -270, 0, 0 ), new( -90, 0, 0 ), new( 90, 0, 0 ), new( 270, 0, 0 )];

	/// <summary>
	/// Four boxes of one material: the first plain, the second and third lit from probe volumes of their own (red, green),
	/// the fourth with a probe's attributes as its own. Only the last three change from the frame without them, each
	/// within its own pixels; material objects draw what binding by reflection draws, and a warm frame writes no sets.
	/// </summary>
	[TestMethod]
	public void ObjectAttributesChangeOnlyTheirObject()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Object attribute test" );
			var material = backend.CreateMaterial( "Boxes", new Color( 0.8f, 0.8f, 0.8f ) );
			Assert.IsTrue( material.NeedsLightProbe, "complex needs a light probe (its NeedsLightProbe attribute)" );
			var box = BoxMesh( backend, material );

			var red = ProbeAttributes( backend, Centres[1], new Color( 4, 0, 0 ) );
			var green = ProbeAttributes( backend, Centres[2], new Color( 0, 4, 0 ) );
			var blue = ProbeAttributes( backend, Centres[3], new Color( 0, 0, 4 ) );

			RenderWorld World( int only, bool lit )
			{
				var world = new RenderWorld();
				world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
				world.Lighting.SunColor = new Color( 1.0f, 0.9f, 0.8f );
				world.Lighting.AmbientColor = new Color( 0.1f, 0.1f, 0.1f, 1 );
				world.Lighting.SunShadowCascades = 0;
				if ( lit )
				{
					world.LightProbeVolumes.Add( new LightProbeVolume { BoxMins = Centres[1] - 60, BoxMaxs = Centres[1] + 60, Attributes = red, LightGroups = [0] } );
					world.LightProbeVolumes.Add( new LightProbeVolume { BoxMins = Centres[2] - 60, BoxMaxs = Centres[2] + 60, Attributes = green, LightGroups = [0] } );
				}

				for ( int b = 0; b < Boxes; b++ )
				{
					if ( only >= 0 && b != only ) continue;
					var obj = new MeshObject( box, new Transform( Centres[b] ) ) { NeedsLightProbe = lit && b is 1 or 2 };
					if ( lit && b == 3 ) obj.Attributes = blue;
					world.Add( obj );
				}

				return world;
			}

			var view = new RenderView { Name = "Object attribute test", FieldOfView = 60, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( 0, -700, 150 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 20 ) );

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

			// Each box's pixels: where it differs from the empty world
			var empty = Frame( World( Boxes, false ), true );
			var masks = new bool[Boxes][];
			for ( int b = 0; b < Boxes; b++ ) masks[b] = Mask( empty, Frame( World( b, false ), true ) );

			var plainWorld = World( -1, false );
			for ( int i = 0; i < 3; i++ ) Frame( plainWorld, true );
			var plain = Frame( plainWorld, true );
			var litWorld = World( -1, true );
			var before = backend.MaterialObjectStats.SetsWritten;
			backend.TimeDraws = true;
			backend.DrawTimes.Reset();
			var lit = Frame( litWorld, true );
			var litSets = backend.MaterialObjectStats.SetsWritten - before;
			var objectChanges = backend.DrawTimes.ObjectChanges;
			backend.TimeDraws = false;
			var reflection = Frame( litWorld, false );

			for ( int i = 0; i < 3; i++ ) Frame( litWorld, true );
			var written = backend.MaterialObjectStats.SetsWritten;
			var again = Frame( litWorld, true );

			var changed = Mask( plain, lit );
			Assert.AreEqual( 0, Count( changed, masks[0] ), "the plain box draws as it did" );
			for ( int b = 1; b < Boxes; b++ ) Assert.IsTrue( Count( changed, masks[b] ) > Count( masks[b], masks[b] ) / 2, $"box {b} changed with its probe ({Count( changed, masks[b] )} of {Count( masks[b], masks[b] )} pixels)" );
			Assert.AreEqual( 0, Count( changed, Outside( masks ) ), "nothing else changed" );
			Assert.IsTrue( objectChanges > 0, "draws of the same material changed object attributes" );

			// The objects' constant buffers are dynamic offsets into the same sets; what's new is the material constants for
			// UsesBakedLightingFromProbe, once (colour's two sets, the prepass's one for the box with its own attributes)
			Assert.IsTrue( litSets <= 3, $"the objects' attributes wrote {litSets} sets, not a set per object" );
			Assert.AreEqual( 0, Different( lit, reflection ), "material objects draw what binding by reflection draws" );
			CollectionAssert.AreEqual( lit, again, "the same frame twice" );
			Assert.AreEqual( written, backend.MaterialObjectStats.SetsWritten, "a warm frame writes no descriptor sets" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A box whose draw is lit from lightmaps, with lightmap attributes as native's <c>SetSceneObjectLightmapAttributes</c>
	/// sets them, draws with the lightmap variant (<c>D_BAKED_LIGHTING_FROM_LIGHTMAP</c>) in colour and the usual one in
	/// the prepass: it changes, the box beside it doesn't, and material objects draw what reflection draws.
	/// </summary>
	[TestMethod]
	public void LightmapsReachOnlyTheirObject()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Lightmap test" );
			var material = backend.CreateMaterial( "Boxes", new Color( 0.8f, 0.8f, 0.8f ) );
			var box = BoxMesh( backend, material );
			var draw = box.DrawsForLod( 0 )[0];
			var lightmappedBox = new RenderMesh( box.Geometry, new BBox( new Vector3( -50, -50, -50 ), new Vector3( 50, 50, 50 ) ), [[draw with { Lightmapped = true }]], [] );
			var lightmap = LightmapAttributes( backend );

			RenderWorld World( bool lit )
			{
				var world = new RenderWorld();
				world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
				world.Lighting.SunShadowCascades = 0;
				world.Add( new MeshObject( box, new Transform( Centres[1] ) ) );
				world.Add( new MeshObject( lightmappedBox, new Transform( Centres[2] ) ) { LightmapAttributes = lit ? lightmap : null } );
				return world;
			}

			var view = new RenderView { Name = "Lightmap test", FieldOfView = 60, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( 0, -700, 150 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 20 ) );

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

			var plain = Frame( World( false ), true );
			var litWorld = World( true );
			var lit = Frame( litWorld, true );
			var reflection = Frame( litWorld, false );
			for ( int i = 0; i < 3; i++ ) Frame( litWorld, true );
			var written = backend.MaterialObjectStats.SetsWritten;
			Frame( litWorld, true );

			var changed = Mask( plain, lit );
			var half = Width / 2;
			int Side( bool right )
			{
				var count = 0;
				for ( int p = 0; p < changed.Length; p++ )
				{
					if ( changed[p] && (p % Width >= half) == right ) count++;
				}

				return count;
			}

			Assert.AreEqual( 0, Side( false ), "the box without lightmaps draws as it did" );
			Assert.IsTrue( Side( true ) > 1000, $"the lightmapped box changed ({Side( true )} pixels)" );
			Assert.AreEqual( 0, Different( lit, reflection ), "material objects draw what binding by reflection draws" );
			Assert.AreEqual( written, backend.MaterialObjectStats.SetsWritten, "a warm frame writes no descriptor sets" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A blendable material (<c>blendable.shader</c>, whose forward mode is <c>VrForward</c>) draws in colour as well as in
	/// the prepass, through material objects as by reflection.
	/// </summary>
	[TestMethod]
	public void BlendableMaterialsDrawForward()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Blendable test" );
			var material = backend.LoadMaterial( "materials/dev/simple/simple_blend.vmat" );
			Assert.AreEqual( "shaders/blendable.shader", ((BackendMaterial)material).File?.Shader, "a blendable material" );
			var world = new RenderWorld();
			world.Lighting.SunShadowCascades = 0;
			world.Add( new MeshObject( BoxMesh( backend, material ), Transform.Zero ) );

			var view = new RenderView { Name = "Blendable test", FieldOfView = 60, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -200, -200, 150 );
			view.Rotation = Rotation.LookAt( -view.Position );

			byte[] Frame( bool materialObjects )
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

			backend.TimeDraws = true;
			backend.DrawTimes.Reset();
			var objects = Frame( true );
			var failed = string.Join( ", ", backend.DrawTimes.Failed.Select( f => $"{f.Key} x{f.Value}" ) );
			var forward = system.Stats.Draws;
			var reflection = Frame( false );

			Assert.AreEqual( "", failed, "every draw recorded" );
			Assert.AreEqual( 1, forward, "the box drew in colour" );
			Assert.AreEqual( 0, Different( objects, reflection ), "material objects draw what binding by reflection draws" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// With GPU occlusion culling on, draws come from the culler's arguments and surviving instance ids (one indirect draw
	/// each) and the frame is the one drawn without culling, through material objects as by reflection. Nothing is hidden
	/// yet: the depth chain the cull tests against is built by compute dispatches with their own parameters, which this
	/// backend doesn't run, so every instance survives.
	/// </summary>
	[TestMethod]
	public void OcclusionCulledDrawsDrawWhatUnculledDrawsDraw()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		var host = new BackendRenderHost( backend );
		RenderHost.Current = host;
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Occlusion test" );
			var material = backend.CreateMaterial( "Boxes", new Color( 0.8f, 0.6f, 0.4f ) );
			var box = BoxMesh( backend, material );
			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunShadowCascades = 0;

			// A wall in front, boxes behind it (hidden), and two boxes beside it (seen)
			world.Add( new MeshObject( box, new Transform( new Vector3( 0, -200, 0 ), Rotation.Identity, new Vector3( 4, 0.2f, 2 ) ) ) );
			for ( int i = 0; i < 6; i++ ) world.Add( new MeshObject( box, new Transform( new Vector3( i * 30 - 75, 100 + i * 60, 0 ), Rotation.Identity, 0.4f ) ) );
			world.Add( new MeshObject( box, new Transform( new Vector3( -300, 0, 0 ) ) ) );
			world.Add( new MeshObject( box, new Transform( new Vector3( 300, 0, 0 ) ) ) );

			var view = new RenderView { Name = "Occlusion test", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( 0, -600, 20 );
			view.Rotation = Rotation.LookAt( Vector3.Left );

			byte[] Frame( bool materialObjects )
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

			var unculled = Frame( true );

			host.Occlusion = true;
			for ( int i = 0; i < 4; i++ ) Frame( true );
			backend.TimeDraws = true;
			backend.DrawTimes.Reset();
			var culled = Frame( true );
			var failed = string.Join( ", ", backend.DrawTimes.Failed.Select( f => $"{f.Key} x{f.Value}" ) );
			var indirect = backend.DrawTimes.IndirectDraws;
			backend.TimeDraws = false;
			var reflection = Frame( false );

			Assert.IsTrue( system.OcclusionActive, "the culler ran" );
			Assert.AreEqual( "", failed, "every culled draw recorded" );
			Assert.IsTrue( indirect > 0, $"draws came from the culler's arguments ({indirect})" );
			Assert.AreEqual( 0, Different( unculled, culled ), "culling draws what drawing everything does" );
			Assert.AreEqual( 0, Different( culled, reflection ), "material objects draw what binding by reflection draws" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Lightmap attributes as native's <c>SetSceneObjectLightmapAttributes</c> sets them, with 2D array lightmaps of one
	/// colour each (direct light indices and strengths, irradiance, directional irradiance, as a map lists them).
	/// </summary>
	ShaderParameters LightmapAttributes( BackendDevice backend )
	{
		string[] names = ["direct_light_indices", "direct_light_strengths", "irradiance", "directional_irradiance"];
		Color[] colours = [new( 0, 0, 0, 0 ), new( 0, 0, 0, 0 ), new( 3, 2, 1, 1 ), new( 2, 2, 2, 2 )];
		var attributes = backend.CreateParameters();
		var constants = new LightmapConstants { UvScale = new Vector4( 1, 1, 0, 0 ), Index4 = 0, Index5 = 0, Index6 = 0, Index7 = 0 };
		Span<int> indices = stackalloc int[4];
		for ( int i = 0; i < names.Length; i++ )
		{
			var texture = TextureArray( backend, colours[i] );
			attributes.Set( names[i], texture );
			indices[i] = texture.BindlessIndex;
		}

		(constants.Index0, constants.Index1, constants.Index2, constants.Index3) = (indices[0], indices[1], indices[2], indices[3]);
		attributes.SetData( "BakedLightingConstantBuffer_t", constants );
		attributes.Set( "UsesBakedLightmaps", true );
		attributes.Set( "UsesBakedLightingFromProbe", false );
		attributes.SetCombo( "D_BAKED_LIGHTING_FROM_PROBE", 0 );
		attributes.SetCombo( "D_BAKED_LIGHTING_FROM_LIGHTMAP", 1 );
		return attributes;
	}

	/// <summary><c>BakedLightingConstantBuffer_t</c>: the uv scale and eight texture indices.</summary>
	struct LightmapConstants
	{
		public Vector4 UvScale;
		public int Index0, Index1, Index2, Index3, Index4, Index5, Index6, Index7;
	}

	/// <summary>A 2x2 one-layer 2D array texture of one colour, ready to sample, as lightmaps are.</summary>
	GpuTexture TextureArray( BackendDevice backend, Color colour )
	{
		var handle = Device.CreateTexture( new TextureInfo { Width = 2, Height = 2, Depth = 1, Layers = 1, Mips = 1, Samples = 1, Dimension = TextureDimension.Texture2DArray, Format = Format.RGBA8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination, Name = "Lightmap" } );
		var texel = new byte[] { Byte( colour.r ), Byte( colour.g ), Byte( colour.b ), Byte( colour.a ) };
		var data = new byte[2 * 2 * 4];
		for ( int i = 0; i < data.Length; i += 4 ) texel.CopyTo( data, i );
		Device.WriteTexture( handle, data );
		return new BackendTexture( backend, handle, new TextureDesc { Width = 2, Height = 2, Mips = 1, Samples = 1, Format = TextureFormat.RGBA8888 }, "Lightmap" ) { Layout = ImageLayout.ShaderRead, Dimension = TextureDimension.Texture2DArray };
	}

	/// <summary>
	/// A light probe's attributes as native's <c>SetLightProbeVolumeAttributes</c> sets them: the volume's constants (its
	/// world-to-texture transform and a probe texture of one colour), <c>UsesBakedLightingFromProbe</c> and the combos.
	/// </summary>
	ShaderParameters ProbeAttributes( BackendDevice backend, Vector3 centre, Color colour )
	{
		var probe = Texture3D( backend, colour );
		var black = Texture3D( backend, new Color( 0, 0, 0, 0 ) );
		var mins = centre - 60;
		var size = new Vector3( 120, 120, 120 );
		var constants = new ProbeConstants
		{
			Row0 = new Vector4( 1 / size.x, 0, 0, -mins.x / size.x ),
			Row1 = new Vector4( 0, 1 / size.y, 0, -mins.y / size.y ),
			Row2 = new Vector4( 0, 0, 1 / size.z, -mins.z / size.z ),
			TextureMin = new Vector4( 0.25f, 0.25f, 1 / 24f, 0 ),
			TextureMax = new Vector4( 0.75f, 0.75f, 1 / 6f - 1 / 24f, 0 ),
			Colour = new Vector4( 1, 1, 1, 0 ),
			Probe = probe.BindlessIndex,
			Indices = black.BindlessIndex,
			Scalars = black.BindlessIndex,
		};

		var attributes = backend.CreateParameters();
		attributes.SetData( "PerBatchLightProbeVolumeConstantBuffer_t", constants );
		attributes.Set( "UsesBakedLightmaps", false );
		attributes.Set( "UsesBakedLightingFromProbe", true );
		attributes.SetCombo( "D_BAKED_LIGHTING_FROM_PROBE", 1 );
		attributes.SetCombo( "D_BAKED_LIGHTING_FROM_LIGHTMAP", 0 );
		return attributes;
	}

	/// <summary><c>PerBatchLightProbeVolumeConstantBuffer_t</c>.</summary>
	struct ProbeConstants
	{
		public Vector4 Row0, Row1, Row2;
		public Vector4 TextureMin, TextureMax, Colour;
		public int Probe, Indices, Scalars;
	}

	/// <summary>A 2x2x12 3D texture of one colour (six layers of two, as a probe volume's are), ready to sample.</summary>
	GpuTexture Texture3D( BackendDevice backend, Color colour )
	{
		var texture = (BackendTexture)backend.CreateTexture( new TextureDesc { Width = 2, Height = 2, Depth = 12, Mips = 1, Samples = 1, Format = TextureFormat.RGBA8888 }, "Probe" );
		var texel = new byte[] { Byte( colour.r ), Byte( colour.g ), Byte( colour.b ), Byte( colour.a ) };
		var data = new byte[2 * 2 * 12 * 4];
		for ( int i = 0; i < data.Length; i += 4 ) texel.CopyTo( data, i );
		Device.WriteTexture( texture.Handle, data );
		texture.Layout = ImageLayout.ShaderRead;
		return texture;
	}

	static byte Byte( float value ) => (byte)Math.Clamp( value * 63.75f, 0, 255 );

	static RenderMesh BoxMesh( BackendDevice backend, GpuMaterial material )
	{
		var vertices = new List<MeshVertex>();
		var indices = new List<uint>();
		Vector3[] normals = [Vector3.Forward, Vector3.Backward, Vector3.Left, Vector3.Right, Vector3.Up, Vector3.Down];
		foreach ( var n in normals )
		{
			var u = MathF.Abs( n.z ) > 0.5f ? Vector3.Forward : Vector3.Cross( Vector3.Up, n );
			var v = Vector3.Cross( n, u );
			var first = (uint)vertices.Count;
			vertices.Add( new MeshVertex( (n - u - v) * 50, n, new Vector2( 0, 1 ) ) );
			vertices.Add( new MeshVertex( (n + u - v) * 50, n, new Vector2( 1, 1 ) ) );
			vertices.Add( new MeshVertex( (n + u + v) * 50, n, new Vector2( 1, 0 ) ) );
			vertices.Add( new MeshVertex( (n - u + v) * 50, n, new Vector2( 0, 0 ) ) );
			indices.AddRange( [first, first + 1, first + 2, first, first + 2, first + 3] );
		}

		return BackendMesh.Create( backend, vertices.ToArray(), indices.ToArray(), material, "Box" );
	}

	/// <summary>Per pixel (8 bytes of RGBA16F), whether the two frames differ there.</summary>
	static bool[] Mask( byte[] a, byte[] b )
	{
		Assert.AreEqual( a.Length, b.Length );
		var mask = new bool[a.Length / 8];
		for ( int p = 0; p < mask.Length; p++ )
		{
			for ( int i = p * 8; i < p * 8 + 8; i++ )
			{
				if ( a[i] != b[i] ) mask[p] = true;
			}
		}

		return mask;
	}

	static bool[] Outside( bool[][] masks )
	{
		var outside = new bool[masks[0].Length];
		for ( int p = 0; p < outside.Length; p++ ) outside[p] = !masks.Any( m => m[p] );
		return outside;
	}

	static int Count( bool[] a, bool[] within )
	{
		var count = 0;
		for ( int p = 0; p < a.Length; p++ )
		{
			if ( a[p] && within[p] ) count++;
		}

		return count;
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
