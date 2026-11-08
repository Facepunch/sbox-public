using Sandbox.SceneRenderer;

namespace Sandbox.SceneLab;

/// <summary>
/// Lab presets that place their objects by hand (<see cref="SceneLabScene.Build"/>), each one renderer case as small as it
/// can be: texture formats and filtering, mirrored and non-uniform scale, an extreme depth range, many materials on one
/// object, and shadow casters near contact.
/// </summary>
internal static class CoreScenes
{
	/// <summary>
	/// A quad per texture format, head on: uncompressed sRGB and linear, BGRA, half float, BC1, BC3, BC4 (red only), BC5
	/// (red and green), BC6H, BC7, and two sizes that aren't powers of two. Each samples the same picture through complex's
	/// colour texture, so they should look alike but for what the format can hold.
	/// </summary>
	public static void TextureFormats( LabLayout layout )
	{
		(ImageFormat Format, int Width, int Height)[] textures =
		[
			(ImageFormat.RGBA8888, 64, 64), (ImageFormat.RGBA8888_LINEAR, 64, 64), (ImageFormat.BGRA8888, 64, 64), (ImageFormat.RGBA16161616F, 64, 64),
			(ImageFormat.DXT1, 64, 64), (ImageFormat.DXT5, 64, 64), (ImageFormat.ATI1N, 64, 64), (ImageFormat.ATI2N, 64, 64),
			(ImageFormat.BC6H, 64, 64), (ImageFormat.BC7, 64, 64), (ImageFormat.RGBA8888, 37, 23), (ImageFormat.BC7, 48, 80),
		];

		const float size = 40, spacing = 48;
		var quad = layout.Mesh( "procedural/quad", () => Quad( size, 1 ) );

		for ( int i = 0; i < textures.Length; i++ )
		{
			var (format, width, height) = textures[i];
			var material = Material.Load( "materials/dev/primary_white.vmat" ).CreateCopy();
			material.Set( "g_tColor", TestTextures.Create( format, width, height, $"scenelab_{format}_{width}x{height}".ToLowerInvariant() ) );

			// Four across, three down, facing the camera (which looks along +x, with +y to its left)
			int column = i % 4, row = i / 4;
			var position = new Vector3( 0, (1.5f - column) * spacing, (1 - row) * spacing );
			layout.Add( quad, new Transform( position, Rotation.FromYaw( 180 ) ), material );
		}

		layout.Yaw = 0;
		layout.Pitch = 0;
		// Close enough that each quad covers more pixels than it has texels, so the top mip is what's sampled
		layout.Distance = 160;
	}

	/// <summary>
	/// Strips of the same texture running away from a low camera, sampled point, nearest, bilinear, trilinear and
	/// anisotropic (bark's <c>F_TEXTURE_FILTERING</c>). Each mip is its own colour, so the mip chosen at the grazing angle,
	/// and the blend between mips, show as bands.
	/// </summary>
	public static void TextureFiltering( LabLayout layout )
	{
		const float width = 32, length = 1600, gap = 6;
		var strip = layout.Mesh( "procedural/filter_strip", () => Strip( width, length, length / width ) );
		var texture = TestTextures.MipColors( 64 );
		var normal = TestTextures.Solid( ImageFormat.RGBA8888_LINEAR, new Color32( 128, 128, 255 ), "scenelab_flat_normal" );
		var rma = TestTextures.Solid( ImageFormat.RGBA8888_LINEAR, new Color32( 255, 0, 255 ), "scenelab_rough_rma" );

		// Point, nearest, bilinear, trilinear, anisotropic, left to right
		int[] filters = [3, 4, 1, 2, 0];
		for ( int i = 0; i < filters.Length; i++ )
		{
			var material = Material.Create( $"scenelab_filter_{filters[i]}", "shaders/bark.shader" );
			material.SetFeature( "F_TEXTURE_FILTERING", filters[i] );
			material.Set( "g_tColor", texture );
			material.Set( "g_tNormal", normal );
			material.Set( "g_tRma", rma );
			material.Set( "g_flTintColor", Vector3.One );

			var y = ((filters.Length - 1) * 0.5f - i) * (width + gap);
			layout.Add( strip, new Transform( new Vector3( 0, y, 0 ) ), material );
		}

		layout.Focus = new Vector3( 160, 0, 0 );
		layout.Yaw = 0;
		layout.Pitch = 14;
		layout.Distance = 200;
	}

	/// <summary>
	/// Meshes stretched and squashed under a shadowed spot and the sun, some turned as well, so their normals only shade
	/// right if they're transformed by the inverse transpose: an F, boxes and spheres.
	/// </summary>
	public static void NonUniformScale( LabLayout layout )
	{
		var letter = LetterF( layout );
		var box = layout.Mesh( "models/dev/box.vmdl" );
		var sphere = layout.Mesh( "models/dev/sphere.vmdl" );
		var turned = Rotation.FromYaw( 30 );

		layout.Stand( letter, new Vector3( -70, 40, 0 ), Rotation.Identity, 1 );
		layout.Stand( letter, new Vector3( -70, -40, 0 ), turned, new Vector3( 1.5f, 0.6f, 1.2f ) );
		layout.Stand( box, new Vector3( 0, 40, 0 ), Rotation.Identity, new Vector3( 1.6f, 0.4f, 1 ) );
		layout.Stand( box, new Vector3( 0, -40, 0 ), turned, new Vector3( 0.5f, 0.5f, 1.8f ) );
		layout.Stand( sphere, new Vector3( 70, 40, 0 ), Rotation.Identity, new Vector3( 1, 1, 0.35f ) );
		layout.Stand( sphere, new Vector3( 70, -40, 0 ), turned, new Vector3( 0.5f, 1.4f, 1 ) );
		layout.Distance = 320;
	}

	/// <summary>
	/// Mirrored meshes under a shadowed spot and the sun: a mirror's winding flips, so it shows its back faces, or loses
	/// its front ones, if culling doesn't follow (native flips it per draw, <c>BATCH_FLAGS_FLIP_WINDING</c>). An F, whose
	/// handedness shows, as it is, mirrored in x, in y and turned, and upside down in z; a box and a sphere mirrored in
	/// all three axes.
	/// </summary>
	public static void Mirrored( LabLayout layout )
	{
		var letter = LetterF( layout );
		var box = layout.Mesh( "models/dev/box.vmdl" );
		var sphere = layout.Mesh( "models/dev/sphere.vmdl" );
		var turned = Rotation.FromYaw( 30 );

		layout.Stand( letter, new Vector3( -50, 60, 0 ), Rotation.Identity, 1 );
		layout.Stand( letter, new Vector3( -50, 0, 0 ), Rotation.Identity, new Vector3( -1, 1, 1 ) );
		layout.Stand( letter, new Vector3( -50, -60, 0 ), turned, new Vector3( 1, -1, 1 ) );
		layout.Stand( letter, new Vector3( 30, 60, 0 ), Rotation.Identity, new Vector3( 1, 1, -1 ) );
		layout.Stand( box, new Vector3( 30, 0, 0 ), turned, -1 );
		layout.Stand( sphere, new Vector3( 30, -60, 0 ), turned, new Vector3( -0.8f, -0.8f, -1.2f ) );
		layout.Distance = 320;
	}

	/// <summary>
	/// An F in boxes: its handedness shows, so a mirror can't pass for the original.
	/// </summary>
	static RenderMesh LetterF( LabLayout layout )
	{
		const string white = "materials/dev/primary_white.vmat";
		return layout.Mesh( "procedural/letter_f", () => Model.Builder
			.WithName( "letter_f" )
			.AddMesh( Box( white, new Vector3( -5, -15, 0 ), new Vector3( 5, -5, 60 ) ) )
			.AddMesh( Box( white, new Vector3( -5, -5, 50 ), new Vector3( 5, 20, 60 ) ) )
			.AddMesh( Box( white, new Vector3( -5, -5, 28 ), new Vector3( 5, 12, 38 ) ) )
			.AddMesh( Box( white, new Vector3( -20, -15, 0 ), new Vector3( -5, -5, 6 ) ) )
			.Create() );
	}

	/// <summary>
	/// A near plane of 0.05 and a far one of two million: a small box a couple of units from the camera, boxes at a
	/// normal distance, and giant ones hundreds of thousands of units away - two of them nearly touching, so a depth
	/// buffer without the precision fights over them.
	/// </summary>
	public static void DepthRange( LabLayout layout )
	{
		var box = layout.Mesh( "models/dev/box.vmdl" );
		var size = box.Bounds.Size.x;

		// The middle distance, around the focus
		layout.Add( box, new Transform( new Vector3( 0, 40, 0 ) ) ).Tint = new Color( 0.9f, 0.5f, 0.4f );
		layout.Add( box, new Transform( new Vector3( 60, -30, 0 ) ) ).Tint = new Color( 0.4f, 0.8f, 0.5f );
		var bounds = layout.Bounds;

		// Giants, a box width apart in depth and overlapping on screen, up where the sky would be
		var giant = 4000.0f;
		layout.Add( box, new Transform( new Vector3( 600_000, 40_000, 180_000 ), Rotation.Identity, giant ) ).Tint = new Color( 0.5f, 0.6f, 1.0f );
		layout.Add( box, new Transform( new Vector3( 600_000 + size * giant * 1.002f, -60_000, 200_000 ), Rotation.Identity, giant ) ).Tint = new Color( 1.0f, 0.85f, 0.4f );
		layout.Add( box, new Transform( new Vector3( 1_500_000, -500_000, 300_000 ), Rotation.FromYaw( 20 ), giant * 2 ) ).Tint = new Color( 0.8f, 0.5f, 0.9f );

		// Close enough to the camera that only a tiny near plane keeps it, low on the right
		var view = Rotation.From( 6, 0, 0 );
		var camera = bounds.Center - view.Forward * 240;
		layout.Add( box, new Transform( camera + view.Forward * 3 + view.Right * 1.6f + view.Down * 0.8f, Rotation.FromYaw( 30 ), 0.01f ) ).Tint = new Color( 1.0f, 0.95f, 0.3f );

		layout.Bounds = bounds;
		layout.Yaw = 0;
		layout.Pitch = 6;
		layout.Distance = 240;
		layout.ZNear = 0.05f;
		layout.ZFar = 2_000_000;
	}

	/// <summary>
	/// One model whose meshes each have their own material - opaque, metal, alpha tested, unlit, self-illuminated,
	/// additive, and a translucent shell around the self-illuminated one - so its draws go to every pass and sort among themselves.
	/// </summary>
	public static void MaterialMix( LabLayout layout )
	{
		var model = layout.Mesh( "procedural/material_mix", () => Model.Builder
			.WithName( "material_mix" )
			.AddMesh( Box( "materials/dev/primary_white.vmat", new Vector3( -90, -30, 0 ), new Vector3( 90, 30, 8 ) ) )
			.AddMesh( Box( "materials/dev/dev_metal_rough10.vmat", new Vector3( -80, -20, 8 ), new Vector3( -50, 10, 38 ) ) )
			.AddMesh( Box( "materials/dev/dev_grating.vmat", new Vector3( -40, -20, 8 ), new Vector3( -10, 10, 38 ) ) )
			.AddMesh( Box( "materials/dev/black_unlit.vmat", new Vector3( 0, -20, 8 ), new Vector3( 30, 10, 38 ) ) )
			.AddMesh( Box( "materials/dev/bright_10.vmat", new Vector3( 40, -20, 8 ), new Vector3( 70, 10, 38 ) ) )
			.AddMesh( Box( "materials/dev/primary_red_additive.vmat", new Vector3( -75, -36, 8 ), new Vector3( -30, -28, 50 ) ) )
			.AddMesh( Box( "materials/dev/primary_white_trans.vmat", new Vector3( 35, -25, 8 ), new Vector3( 75, 15, 50 ) ) )
			.Create() );

		// Its front (-y), with the translucent boxes, toward the camera
		layout.Add( model, new Transform( Vector3.Zero, Rotation.FromYaw( -55 ) ) );
		layout.Distance = 200;
	}

	/// <summary>
	/// Shadow casters on a floor, under a low sun and a shadowed spot: a box resting on it, others raised half a unit and
	/// four, a thin plate one unit up, a sphere touching it at a point, a box sunk into it, and a tall pillar that doesn't
	/// cast, in the way of the light.
	/// </summary>
	public static void ShadowCasters( LabLayout layout )
	{
		var box = layout.Mesh( "models/dev/box.vmdl" );
		var sphere = layout.Mesh( "models/dev/sphere.vmdl" );

		layout.Stand( box, new Vector3( -60, 60, 0 ), Rotation.Identity, 1 );
		layout.Stand( box, new Vector3( 0, 60, 0 ), Rotation.Identity, 1, 0.5f );
		layout.Stand( box, new Vector3( 60, 60, 0 ), Rotation.Identity, 1, 4 );
		layout.Stand( box, new Vector3( -60, -10, 0 ), Rotation.FromYaw( 20 ), new Vector3( 1.2f, 1.2f, 0.04f ), 1 );
		layout.Stand( sphere, new Vector3( 0, -10, 0 ), Rotation.Identity, 1 );
		layout.Stand( box, new Vector3( 60, -10, 0 ), Rotation.From( 30, 10, 20 ), 1, -12 );

		layout.Stand( box, new Vector3( 10, -70, 0 ), Rotation.Identity, new Vector3( 0.3f, 0.3f, 2.5f ) ).CastShadows = false;
		layout.Distance = 280;

		// The floor under what rests on it, not the sunk box's bottom
		var bounds = layout.Bounds;
		layout.Bounds = new BBox( bounds.Mins.WithZ( 0 ), bounds.Maxs );
	}

	/// <summary>
	/// <see cref="ShadowCasters"/> with a large sun shadow bias: shadows near contact (under the plate, between the
	/// boxes, around the sphere) are gone, the long ones stay.
	/// </summary>
	public static void ShadowBias( LabLayout layout )
	{
		ShadowCasters( layout );
		layout.World.Lighting.SunShadowBias = 0.002f;
	}

	static Vertex MakeVertex( Vector3 position, Vector3 normal, Vector3 tangent, Vector2 uv ) => new( position, normal, tangent, new Vector4( uv.x, uv.y, 0, 0 ) ) { Color = Color32.White };

	static Mesh Build( Material material, List<Vertex> vertices, List<int> indices )
	{
		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer( vertices.Count, vertices );
		mesh.CreateIndexBuffer( indices.Count, indices );
		mesh.Bounds = BBox.FromPoints( vertices.Select( x => x.Position ) );
		return mesh;
	}

	static Model Single( string name, Mesh mesh ) => Model.Builder.WithName( name ).AddMesh( mesh ).Create();

	/// <summary>
	/// A square facing +x, its texture upright and the right way round seen from in front, tiled <paramref name="tiles"/> times.
	/// </summary>
	static Model Quad( float size, float tiles )
	{
		var h = size / 2;
		List<Vertex> vertices =
		[
			MakeVertex( new Vector3( 0, -h, h ), Vector3.Forward, Vector3.Left, new Vector2( 0, 0 ) ),
			MakeVertex( new Vector3( 0, h, h ), Vector3.Forward, Vector3.Left, new Vector2( tiles, 0 ) ),
			MakeVertex( new Vector3( 0, h, -h ), Vector3.Forward, Vector3.Left, new Vector2( tiles, tiles ) ),
			MakeVertex( new Vector3( 0, -h, -h ), Vector3.Forward, Vector3.Left, new Vector2( 0, tiles ) ),
		];

		return Single( "quad", Build( Material.Load( "materials/dev/primary_white.vmat" ), vertices, [0, 2, 1, 0, 3, 2] ) );
	}

	/// <summary>
	/// A strip on the ground running along +x from the origin, <paramref name="tiles"/> texture repeats long.
	/// </summary>
	static Model Strip( float width, float length, float tiles )
	{
		var h = width / 2;
		List<Vertex> vertices =
		[
			MakeVertex( new Vector3( 0, h, 0 ), Vector3.Up, Vector3.Right, new Vector2( 0, 0 ) ),
			MakeVertex( new Vector3( 0, -h, 0 ), Vector3.Up, Vector3.Right, new Vector2( 1, 0 ) ),
			MakeVertex( new Vector3( length, -h, 0 ), Vector3.Up, Vector3.Right, new Vector2( 1, tiles ) ),
			MakeVertex( new Vector3( length, h, 0 ), Vector3.Up, Vector3.Right, new Vector2( 0, tiles ) ),
		];

		return Single( "strip", Build( Material.Load( "materials/dev/primary_white.vmat" ), vertices, [0, 1, 2, 0, 2, 3] ) );
	}

	/// <summary>
	/// A box from <paramref name="mins"/> to <paramref name="maxs"/> in one material, its texture one unit per 32.
	/// </summary>
	static Mesh Box( string material, Vector3 mins, Vector3 maxs )
	{
		var vertices = new List<Vertex>();
		var indices = new List<int>();
		var center = (mins + maxs) / 2;
		var half = (maxs - mins) / 2;
		Vector3[] normals = [Vector3.Forward, Vector3.Backward, Vector3.Left, Vector3.Right, Vector3.Up, Vector3.Down];

		foreach ( var normal in normals )
		{
			// Two axes across the face, ordered so the winding faces out
			var u = MathF.Abs( normal.z ) > 0.5f ? Vector3.Forward : Vector3.Up;
			var v = Vector3.Cross( normal, u );
			var start = vertices.Count;

			foreach ( var (a, b) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) } )
			{
				var position = center + (normal + u * a + v * b) * half;
				vertices.Add( MakeVertex( position, normal, v, new Vector2( Vector3.Dot( position, v ), Vector3.Dot( position, u ) ) / 32 ) );
			}

			indices.AddRange( [start, start + 1, start + 2, start, start + 2, start + 3] );
		}

		return Build( Material.Load( material ), vertices, indices );
	}
}
