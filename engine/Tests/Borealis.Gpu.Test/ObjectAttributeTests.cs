using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// Per-object attributes on Borealis.Gpu (<c>BackendContext.SetObjectAttributes</c>): an object's own attributes, its
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
		using var backend = new BackendDevice( Device ) { GpuDriven = false };
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
	/// A blendable material compiled for shader version 1 (<c>m_shaderVersion</c>, which blendable.slang's <c>Version</c>
	/// reads through <c>Source( "ShaderVersion" )</c>) takes its roughness from its normal map's blue channel. Version 0's
	/// layout would read it from the metalness channel (black in <c>simple_blend</c>), so its roughness view
	/// (<c>ToolsVisMode</c> 12) is lit only with the version filled in. The view is a specialization constant: changing it
	/// picks another variant, and changing it back draws the lit frame again, with material objects on and off.
	/// </summary>
	[TestMethod]
	public void BlendableMaterialsReadTheirShaderVersion()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Shader version test" );
			if ( backend.Content.Find( "materials/dev/simple/simple_blend.vmat" ) is null ) Assert.Inconclusive( "simple_blend isn't built" );
			var material = backend.LoadMaterial( "materials/dev/simple/simple_blend.vmat" ) as ConvertedMaterial;
			Assert.IsNotNull( material, "simple_blend draws as the blendable surface" );
			Assert.AreEqual( 1, material.File.ShaderVersion, "simple_blend is compiled for blendable's version 1" );

			var world = new RenderWorld();
			world.Lighting.SunShadowCascades = 0;
			world.Add( new MeshObject( BoxMesh( backend, material ), Transform.Zero ) );

			var view = new RenderView { Name = "Shader version test", FieldOfView = 60, ZNear = 4, ZFar = 4000, ClearColor = Color.Black, Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -200, -200, 150 );
			view.Rotation = Rotation.LookAt( -view.Position );

			byte[] Frame( bool materialObjects, int toolsVis )
			{
				backend.MaterialObjects = materialObjects;
				view.ToolsVisMode = toolsVis;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			const int Roughness = 12;
			var lit = Frame( true, 0 );
			var roughness = Frame( true, Roughness );
			var litAgain = Frame( true, 0 );
			var roughnessByReflection = Frame( false, Roughness );

			// The box's roughness: the brightest red (RGBA16F) over the frame, which is black around it
			var brightest = 0f;
			for ( int i = 0; i + 8 <= roughness.Length; i += 8 ) brightest = Math.Max( brightest, (float)BitConverter.ToHalf( roughness, i ) );

			Assert.IsTrue( Different( lit, roughness ) > 0, "the roughness view draws another frame" );
			Assert.AreEqual( 0, Different( lit, litAgain ), "turning the view off draws the lit frame again" );
			Assert.AreEqual( 0, Different( roughness, roughnessByReflection ), "material objects draw the view binding by reflection draws" );
			Assert.IsTrue( brightest > 0.05f, $"the box has roughness (brightest {brightest})" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// blendable.slang reads either packing a material's <c>ShaderVersion</c> names: version 1's hemi-octahedral normal with
	/// roughness in its blue, and version 0's normal map with roughness, metalness, occlusion and the blend mask in the
	/// second texture. The same textures read the other way draw another normal and roughness. Each frame, lit and in the
	/// albedo, roughness, occlusion and normal views, with the vertex paint's tint in the tint masks or after the layers, is
	/// saved (borealis-gpu-tests in the temp folder) to compare with another build's.
	/// </summary>
	[TestMethod]
	public void BlendableReadsEitherPacking()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Blendable packing test" );
			if ( backend.Content.Find( "materials/dev/simple/simple_blend.vmat" ) is null ) Assert.Inconclusive( "simple_blend isn't built" );
			var material = backend.LoadMaterial( "materials/dev/simple/simple_blend.vmat" ) as ConvertedMaterial;
			Assert.IsNotNull( material, "simple_blend draws as the blendable surface" );

			// The rope's textures in layer A, so every pixel decodes another texel (simple_blend's normal map is flat)
			Assert.IsTrue( material.Set( "g_tColorA", backend.LoadTexture( Fixtures.Rope.Color ) ), "blendable has layer A's colour" );
			Assert.IsTrue( material.Set( "g_tNormalA", backend.LoadTexture( Fixtures.Rope.Normal ) ), "blendable has layer A's normal map" );
			Assert.IsTrue( material.Set( "g_tRmaA", backend.LoadTexture( Fixtures.Rope.Rma ) ), "blendable has layer A's second texture" );

			var world = new RenderWorld();
			world.Lighting.SunShadowCascades = 0;
			world.Add( new MeshObject( BoxMesh( backend, material ), Transform.Zero ) );

			var view = new RenderView { Name = "Blendable packing test", FieldOfView = 60, ZNear = 4, ZFar = 4000, ClearColor = Color.Black, Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -200, -200, 150 );
			view.Rotation = Rotation.LookAt( -view.Position );

			byte[] Frame( int version, bool tintInPaint, int toolsVis )
			{
				Assert.IsTrue( material.Set( "ShaderVersion", [version] ), "blendable has a field for the shader version" );
				Assert.IsTrue( material.Set( "F_USE_TINT_MASKS_IN_VERTEX_PAINT", [tintInPaint ? 1 : 0] ), "blendable has a field for the tint feature" );
				view.ToolsVisMode = toolsVis;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );

				var rgba = new byte[Width * Height * 4];
				for ( int i = 0; i < rgba.Length; i++ ) rgba[i] = i % 4 == 3 ? (byte)255 : (byte)Math.Clamp( MathF.Pow( Math.Max( (float)BitConverter.ToHalf( pixels, i * 2 ), 0 ), 1 / 2.2f ) * 255 + 0.5f, 0, 255 );
				Save( rgba, Width, Height, $"blendable-v{version}-tint{(tintInPaint ? 1 : 0)}-vis{toolsVis}" );
				return pixels;
			}

			var black = new byte[Width * Height * 8];
			foreach ( var vis in new[] { 0, 10, 12, 14, 21 } )
			{
				var packed = Frame( 1, false, vis );
				var mapped = Frame( 0, false, vis );
				Frame( 1, true, vis );
				Frame( 0, true, vis );
				Assert.IsTrue( Different( packed, black ) > Width * Height / 10, $"view {vis}: the box draws" );
				if ( vis is 12 or 21 ) Assert.IsTrue( Different( packed, mapped ) > 0, $"view {vis}: the two packings read the textures differently" );
			}
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
	/// Resident meshes lit by probe volumes of their own (red, green, blue) share one group, each drawing with its own
	/// lighting record, and draw what the views' own batches draw; a box outside every volume is a group of its own. A box
	/// removed lets its group's owner go, whose records are reused, and works out again only the box that moved index.
	/// </summary>
	[TestMethod]
	public void ResidentMeshesLitApartShareAGroup()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true };
		GpuDrivenTests.RequireGpuDrivenDepth( backend );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Resident lit boxes test" );
			var box = BoxMesh( backend, backend.CreateMaterial( "Resident lit boxes", new Color( 0.8f, 0.8f, 0.8f ) ) );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.0f, 0.9f, 0.8f );
			world.Lighting.AmbientColor = new Color( 0.1f, 0.1f, 0.1f, 1 );
			world.Lighting.SunShadowCascades = 0;
			Color[] colours = [new( 4, 0, 0 ), new( 0, 4, 0 ), new( 0, 0, 4 )];
			for ( int b = 1; b < Boxes; b++ ) world.LightProbeVolumes.Add( new LightProbeVolume { BoxMins = Centres[b] - 60, BoxMaxs = Centres[b] + 60, Attributes = ProbeAttributes( backend, Centres[b], colours[b - 1] ), LightGroups = [0] } );

			var boxes = new List<MeshObject>();
			for ( int b = 0; b < Boxes; b++ )
			{
				var obj = new MeshObject( box, new Transform( Centres[b] ) ) { NeedsLightProbe = true, IsStatic = true };
				world.Add( obj );
				boxes.Add( obj );
			}

			var view = new RenderView { Name = "Resident lit boxes test", FieldOfView = 60, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( 0, -700, 150 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 20 ) );

			byte[] Frame( bool resident )
			{
				backend.GpuDrivenResidentMeshes = resident;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			var aggregates = system.GetFeature<Borealis.Features.AggregateRenderFeature>();
			void Same( string change, int groups )
			{
				Frame( false );
				var own = Frame( false );
				var hits = backend.GpuDrivenPlanHits;
				byte[] resident = null;
				for ( int i = 0; i < 4 && backend.GpuDrivenPlanHits == hits; i++ ) resident = Frame( true );
				Assert.IsTrue( backend.GpuDrivenPlanHits > hits, $"{change}: a call drawn from a plan" );
				var list = aggregates.ResidentFor( system.MainPass );
				Assert.AreEqual( groups, list?.MeshGroups.Count ?? 0, $"{change}: boxes lit apart share a group" );
				Assert.IsTrue( list.MeshGroups.Any( g => g.Owner.Probes is { Length: > 1 } ), $"{change}: a group keeps each box's probe" );
				Assert.AreEqual( 0, Different( own, resident ), $"{change}: resident draws what the views' own batches do" );
			}

			Same( "first", 2 );

			// A box gone: the box last in the world moves into its index, the only one worked out again
			var checkedBefore = aggregates.ResidentCandidatesChecked;
			var releasedBefore = aggregates.ResidentOwnersReleased;
			var runsBefore = backend.GpuDrivenTables.FragmentRunsReleased;
			world.Remove( boxes[1] );
			Same( "a box gone", 2 );
			Assert.IsTrue( aggregates.ResidentCandidatesChecked - checkedBefore <= 2, $"a box gone worked out {aggregates.ResidentCandidatesChecked - checkedBefore} boxes again" );
			Assert.IsTrue( aggregates.ResidentOwnersReleased > releasedBefore, "the changed group's owner was let go" );
			Assert.IsTrue( backend.GpuDrivenTables.FragmentRunsReleased > runsBefore, "its records were released" );

			for ( int i = 0; i < 6; i++ ) Frame( true );
			var visibilityBefore = backend.GpuDrivenTables.VisibilityRunsReleased;
			world.Remove( boxes[0] );
			Same( "the unlit box gone", 1 );
			Assert.IsTrue( backend.GpuDrivenTables.FragmentRunsReused > 0, "released records were reused" );
			Assert.IsTrue( backend.GpuDrivenTables.VisibilityRunsReleased > visibilityBefore, "the changed group's vertex bits were released" );

			// The feature disposed, as a map's systems are when it unloads: the groups it kept are let go, so the next map's reuse
			// their records
			var ownersAtEnd = aggregates.ResidentOwnersReleased;
			var runsAtEnd = backend.GpuDrivenTables.FragmentRunsReleased;
			aggregates.Dispose();
			Assert.IsTrue( aggregates.ResidentOwnersReleased > ownersAtEnd, "a disposed feature's groups were let go" );
			Assert.IsTrue( backend.GpuDrivenTables.FragmentRunsReleased > runsAtEnd, "their records were released" );
			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A rescan that changes nothing of a resident group (another model's box added) keeps the group's owner, and so its
	/// records and vertex bits: dropping the lists twice before any was made (the rescan, then the slots it found) let every
	/// group's owner go and made it again, which ran the visibility bits out on Construct.
	/// </summary>
	[TestMethod]
	public void ResidentGroupsKeepTheirOwnersAcrossARescan()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true, GpuDrivenResidentMeshes = true };
		GpuDrivenTests.RequireGpuDrivenDepth( backend );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Resident rescan test" );
			var material = backend.CreateMaterial( "Resident rescan boxes", new Color( 0.8f, 0.8f, 0.8f ) );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.0f, 0.9f, 0.8f );
			world.Lighting.AmbientColor = new Color( 0.1f, 0.1f, 0.1f, 1 );
			world.Lighting.SunShadowCascades = 0;
			var box = BoxMesh( backend, material );
			for ( int b = 0; b < 3; b++ ) world.Add( new MeshObject( box, new Transform( Centres[b] ) ) { IsStatic = true } );

			var view = new RenderView { Name = "Resident rescan test", FieldOfView = 60, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( 0, -700, 150 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 20 ) );

			void Frames( int count )
			{
				for ( int i = 0; i < count; i++ )
				{
					backend.BeginFrame();
					RenderHost.Current.NextFrame();
					system.Render( world, view, target );
				}

				Device.WaitIdle();
			}

			var aggregates = system.GetFeature<Borealis.Features.AggregateRenderFeature>();
			Frames( 8 );
			var group = aggregates.ResidentFor( system.MainPass )?.MeshGroups.SingleOrDefault();
			Assert.IsNotNull( group, "the boxes are a resident group" );
			var tables = backend.GpuDrivenTables;
			var released = aggregates.ResidentOwnersReleased;
			var visibilityReleased = tables.VisibilityRunsReleased;
			var bits = tables.VisibilityUse.Bits;
			Assert.IsTrue( bits > 0, "the group has vertex bits" );

			// Another model's box: the world's structure moves, so every list is made again
			world.Add( new MeshObject( BoxMesh( backend, material ), new Transform( Centres[3] ) ) { IsStatic = true } );
			Frames( 8 );
			var list = aggregates.ResidentFor( system.MainPass );
			Assert.AreEqual( 2, list?.MeshGroups.Count ?? 0, "the new box is a group of its own" );
			Assert.IsTrue( list.MeshGroups.Any( g => ReferenceEquals( g.Owner, group.Owner ) ), "the unchanged group kept its owner" );
			Assert.AreEqual( released, aggregates.ResidentOwnersReleased, "no owner was let go" );
			Assert.AreEqual( visibilityReleased, tables.VisibilityRunsReleased, "no vertex bits were released" );
			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A resident mesh whose draws change every frame (game code setting its state each frame) goes the views' own way until it
	/// keeps them, and the lists stay as they are: making every list again each frame drew Construct's whole world the views'
	/// own way while a first-person body's held weapon copy changed its tags twice a frame.
	/// </summary>
	[TestMethod]
	public void ResidentListsOutlastAMeshDrawnAnotherWayEachFrame()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true, GpuDrivenResidentMeshes = true };
		GpuDrivenTests.RequireGpuDrivenDepth( backend );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Resident restless draws test" );
			var material = backend.CreateMaterial( "Resident restless draws boxes", new Color( 0.8f, 0.8f, 0.8f ) );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 1.0f, 0.9f, 0.8f );
			world.Lighting.AmbientColor = new Color( 0.1f, 0.1f, 0.1f, 1 );
			world.Lighting.SunShadowCascades = 0;
			var box = BoxMesh( backend, material );
			var boxes = new List<MeshObject>();
			for ( int b = 0; b < 3; b++ )
			{
				var obj = new MeshObject( box, new Transform( Centres[b] ) ) { IsStatic = true };
				world.Add( obj );
				boxes.Add( obj );
			}

			var view = new RenderView { Name = "Resident restless draws test", FieldOfView = 60, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( 0, -700, 150 );
			view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 20 ) );

			void Frame()
			{
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
			}

			var aggregates = system.GetFeature<Borealis.Features.AggregateRenderFeature>();
			for ( int i = 0; i < 8; i++ ) Frame();
			Device.WaitIdle();
			Assert.AreEqual( 3, aggregates.ResidentFor( system.MainPass )?.MeshGroups.SingleOrDefault()?.Members.Length ?? 0, "the boxes are one resident group" );

			// One box's draws change every frame: once out of the lists, they're made no more
			var made = aggregates.ResidentListsMade;
			for ( int i = 0; i < 12; i++ )
			{
				boxes[1].LodOverride = i % 2 == 0 ? 0 : -1;
				Frame();
			}

			Device.WaitIdle();
			Assert.IsTrue( aggregates.ResidentListsMade - made <= 2, $"lists made {aggregates.ResidentListsMade - made} times in 12 frames" );
			var list = aggregates.ResidentFor( system.MainPass );
			Assert.AreEqual( 2, list?.MeshGroups.SingleOrDefault()?.Members.Length ?? 0, "the boxes that keep still stay resident" );
			Assert.IsFalse( list.Holds[boxes[1].Index], "the box drawn another way each frame is the views' own" );

			// It keeps its draws: resident again once settled
			var settle = Borealis.Features.AggregateRenderFeature.SettleFrames;
			for ( int i = 0; i < settle + 4; i++ ) Frame();
			Device.WaitIdle();
			Assert.AreEqual( 3, aggregates.ResidentFor( system.MainPass )?.MeshGroups.SingleOrDefault()?.Members.Length ?? 0, "resident again once it kept its draws" );
			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A probe volume that owns part of a shared atlas texture's depth (<c>AtlasZScale</c>, <c>AtlasZOffset</c>) samples only
	/// that part: a box lit from the upper half of a red-then-green atlas draws as one lit from a green texture of its own,
	/// from the lower half as one lit from a red one, both through the views' own batches and GPU-driven lighting records.
	/// </summary>
	[TestMethod]
	public void ProbeVolumeSamplesItsPartOfAnAtlas()
	{
		foreach ( var gpuDriven in new[] { false, true } )
		{
			using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = gpuDriven, GpuDriven = gpuDriven, GpuDrivenVisibility = gpuDriven };
			if ( gpuDriven ) GpuDrivenTests.RequireGpuDrivenDepth( backend );
			var previousHost = RenderHost.Current;
			RenderHost.Current = new BackendRenderHost( backend );
			try
			{
				using var system = new RenderSystem { ParallelRecording = false };
				using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Probe atlas test" );
				var box = BoxMesh( backend, backend.CreateMaterial( "Probe atlas box", new Color( 0.8f, 0.8f, 0.8f ) ) );

				Color red = new( 4, 0, 0 ), green = new( 0, 4, 0 );
				var atlas = Texture3D( backend, 24, z => z < 12 ? red : green );
				var ownRed = Texture3D( backend, red );
				var ownGreen = Texture3D( backend, green );
				var centre = Centres[1];

				var view = new RenderView { Name = "Probe atlas test", FieldOfView = 60, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.3f, 0.4f, 0.5f ), Viewport = new Rect( 0, 0, Width, Height ) };
				view.Position = new Vector3( 0, -700, 150 );
				view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 20 ) );

				byte[] Frame( GpuTexture probe, float scale, float offset )
				{
					var world = new RenderWorld();
					world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
					world.Lighting.SunColor = new Color( 1.0f, 0.9f, 0.8f );
					world.Lighting.AmbientColor = new Color( 0.1f, 0.1f, 0.1f, 1 );
					world.Lighting.SunShadowCascades = 0;
					var attributes = backend.LightProbeVolumeParameters( probe, null, null, new Transform( Vector3.Zero ), centre - 60, centre + 60, scale, offset );
					world.LightProbeVolumes.Add( new LightProbeVolume { BoxMins = centre - 60, BoxMaxs = centre + 60, Attributes = attributes, LightGroups = [0] } );
					world.Add( new MeshObject( box, new Transform( centre ) ) { NeedsLightProbe = true } );

					byte[] pixels = null;
					for ( int i = 0; i < 2; i++ )
					{
						backend.BeginFrame();
						RenderHost.Current.NextFrame();
						system.Render( world, view, target );
						Device.WaitIdle();
						var (texture, layout) = BackendDevice.Unwrap( target.Color );
						pixels = ReadBack( texture, layout );
						BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
					}

					return pixels;
				}

				var redOwn = Frame( ownRed, 1, 0 );
				var greenOwn = Frame( ownGreen, 1, 0 );
				var lower = Frame( atlas, 0.5f, 0 );
				var upper = Frame( atlas, 0.5f, 0.5f );

				var boxPixels = Count( Mask( redOwn, greenOwn ), Mask( redOwn, greenOwn ) );
				Assert.IsTrue( boxPixels > 500, $"{(gpuDriven ? "GPU-driven" : "batches")}: red and green probes light the box apart ({boxPixels} pixels)" );
				var upperOff = Count( Mask( upper, greenOwn ), Mask( upper, greenOwn ) );
				var lowerOff = Count( Mask( lower, redOwn ), Mask( lower, redOwn ) );
				Assert.IsTrue( upperOff <= boxPixels / 100, $"{(gpuDriven ? "GPU-driven" : "batches")}: the atlas's upper half lights as green ({upperOff} of {boxPixels} pixels differ)" );
				Assert.IsTrue( lowerOff <= boxPixels / 100, $"{(gpuDriven ? "GPU-driven" : "batches")}: the atlas's lower half lights as red ({lowerOff} of {boxPixels} pixels differ)" );
				Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
			}
			finally
			{
				Device.WaitIdle();
				RenderHost.Current = previousHost;
			}
		}
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
	GpuTexture Texture3D( BackendDevice backend, Color colour ) => Texture3D( backend, 12, _ => colour );

	/// <summary>A 2x2x<paramref name="depth"/> 3D texture, each z slice the colour <paramref name="colour"/> gives it, ready to sample.</summary>
	GpuTexture Texture3D( BackendDevice backend, int depth, Func<int, Color> colour )
	{
		var texture = (BackendTexture)backend.CreateTexture( new TextureDesc { Width = 2, Height = 2, Depth = depth, Mips = 1, Samples = 1, Format = TextureFormat.RGBA8888 }, "Probe" );
		var data = new byte[2 * 2 * depth * 4];
		for ( int z = 0; z < depth; z++ )
		{
			var c = colour( z );
			var texel = new byte[] { Byte( c.r ), Byte( c.g ), Byte( c.b ), Byte( c.a ) };
			for ( int i = z * 16; i < z * 16 + 16; i += 4 ) texel.CopyTo( data, i );
		}

		Device.WriteTexture( texture.Handle, data );
		texture.Layout = ImageLayout.ShaderRead;
		return texture;
	}

	static byte Byte( float value ) => (byte)Math.Clamp( value * 63.75f, 0, 255 );

	internal static RenderMesh BoxMesh( BackendDevice backend, GpuMaterial material )
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
