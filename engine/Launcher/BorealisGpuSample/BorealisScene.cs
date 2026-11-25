using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;

namespace BorealisGpuSample;

/// <summary>
/// Borealis's own renderer on Borealis.Gpu: a <see cref="RenderWorld"/> of meshes made from raw vertex and index data - a
/// ground, a ring of boxes, spheres and a spinning tower - lit by a sun with cascaded shadows, rendered by an unmodified
/// <see cref="RenderSystem"/> (depth prepass, shadow maps, forward) through <see cref="BackendDevice"/>. The tests
/// render it offscreen too.
/// </summary>
internal sealed class BorealisScene : IDisposable
{
	readonly BackendDevice backend;
	readonly RenderHost previousHost;
	readonly RenderSystem system;
	readonly RenderWorld world = new();
	readonly RenderView view = new() { Name = "Borealis.Gpu sample", FieldOfView = 70, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.32f, 0.42f, 0.55f ) };
	readonly List<(MeshObject Object, Vector3 Position, float Speed)> spinning = new();
	ViewTarget target;

	/// <summary>The renderer.</summary>
	public RenderSystem System => system;

	/// <summary>The device it renders through.</summary>
	public BackendDevice Backend => backend;

	/// <summary>Whether it draws GPU-driven (<see cref="BackendDevice.GpuDriven"/>, on by default).</summary>
	public bool GpuDriven
	{
		get => backend.GpuDriven;
		set => backend.GpuDriven = value;
	}

	/// <summary>The world's sun, ambient and sky.</summary>
	public SceneLighting Lighting => world.Lighting;

	/// <summary>Objects in the world.</summary>
	public int ObjectCount => world.Count;

	/// <summary>Of those, compiled s&amp;box models read from the content.</summary>
	public int ModelCount { get; }

	/// <summary>What the last frame drew.</summary>
	public RenderStats Stats => system.Stats;

	public BorealisScene( Device gpu )
	{
		backend = new BackendDevice( gpu );
		previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );

		// One context, recorded in order: the backend tracks layouts as it records (see BackendContext)
		system = new RenderSystem { ParallelRecording = false };

		world.Lighting.SunDirection = new Vector3( 0.5f, 0.35f, -0.8f ).Normal;
		world.Lighting.SunColor = new Color( 1.25f, 1.15f, 1.0f );
		world.Lighting.AmbientColor = new Color( 0.12f, 0.15f, 0.2f, 1 );
		world.Lighting.SunShadowCascades = 3;

		// s&box's own materials where they're built, plain colours (white.vmat tinted) where they aren't
		var ground = Material( "materials/dev/gray_grid_8.vmat", "Ground", new Color( 0.55f, 0.55f, 0.5f ) );
		var stone = Material( "materials/dev/dev_measuregeneric01.vmat", "Stone", new Color( 0.8f, 0.75f, 0.68f ) );
		var red = backend.CreateMaterial( "Red", new Color( 0.85f, 0.2f, 0.15f ) );
		var blue = Material( "materials/dev/dev_nonmetal_rough30.vmat", "Blue", new Color( 0.2f, 0.4f, 0.9f ), tint: true );

		world.Add( new MeshObject( Box( backend, new Vector3( 2400, 2400, 8 ), ground, "Ground", uvScale: 1 / 64f ), new Transform( new Vector3( 0, 0, -4 ) ) ) );

		var box = Box( backend, new Vector3( 40, 40, 40 ), stone, "Box" );
		var sphere = Sphere( backend, 24, red, "Sphere" );
		var tower = Box( backend, new Vector3( 30, 30, 160 ), blue, "Tower" );

		for ( int i = 0; i < 12; i++ )
		{
			var angle = i / 12f * MathF.Tau;
			var position = new Vector3( MathF.Cos( angle ) * 260, MathF.Sin( angle ) * 260, 20 );
			var obj = new MeshObject( i % 2 == 0 ? box : sphere, new Transform( position + (i % 2 == 0 ? Vector3.Zero : new Vector3( 0, 0, 4 )) ) );
			world.Add( obj );
			if ( i % 2 == 0 ) spinning.Add( (obj, position, 0.6f + i * 0.05f) );
		}

		for ( int x = -2; x <= 2; x++ )
		{
			for ( int y = -2; y <= 2; y++ )
			{
				var position = new Vector3( x * 70 - 520, y * 70, 20 );
				world.Add( new MeshObject( (x + y) % 2 == 0 ? box : sphere, new Transform( position, Rotation.FromYaw( x * 17 + y * 31 ) ) ) );
			}
		}

		// Coloured point lights around the ring and a spot light down on the grid; no shadows of their own
		Color[] colors = [new Color( 1, 0.3f, 0.1f ), new Color( 0.1f, 0.6f, 1 ), new Color( 0.3f, 1, 0.3f ), new Color( 1, 0.2f, 0.8f )];
		for ( int i = 0; i < colors.Length; i++ )
		{
			var angle = (i + 0.5f) / colors.Length * MathF.Tau;
			world.Add( new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( MathF.Cos( angle ) * 180, MathF.Sin( angle ) * 180, 30 ) ) )
			{
				Color = colors[i] * 4,
				Radius = 220,
				CastShadows = false,
			} );
		}

		world.Add( new LightObject( LightObject.LightKind.Spot, new Transform( new Vector3( -520, 0, 260 ), Rotation.LookAt( Vector3.Down, Vector3.Forward ) ) )
		{
			Color = new Color( 6, 5.5f, 4 ),
			Radius = 500,
			ConeInner = 20,
			ConeOuter = 30,
			CastShadows = false,
		} );

		// s&box's own compiled models (.vmdl_c), where they're built: a row standing on the ground beside the ring. The
		// citizen is skinned and draws in its bind pose.
		string[] models = ["models/dev/box.vmdl", "models/citizen_props/trashcan01.vmdl", "models/citizen/citizen.vmdl", "models/dev/gordon_at_desk.vmdl", "models/citizen_props/wheel01.vmdl"];
		for ( int i = 0; i < models.Length; i++ )
		{
			if ( backend.LoadModel( models[i] ) is not { } model ) continue;
			world.Add( new MeshObject( model, new Transform( new Vector3( 380, (i - models.Length / 2) * 90, -model.Bounds.Mins.z ), Rotation.FromYaw( 180 ) ) ) );
			ModelCount++;
		}

		var spinner = new MeshObject( tower, new Transform( new Vector3( 0, 0, 80 ) ) );
		world.Add( spinner );
		spinning.Add( (spinner, new Vector3( 0, 0, 80 ), 0.4f) );
	}

	/// <summary>
	/// Render a frame at <paramref name="time"/> into a target of this size, and return its colour and the layout it's left
	/// in. Linear HDR: blit it to an sRGB swap chain image to show it.
	/// </summary>
	public (TextureHandle Texture, ImageLayout Layout, GpuTexture Color) Render( int width, int height, float time )
	{
		if ( target is null || target.Size.x != width || target.Size.y != height )
		{
			target?.Dispose();
			target = ViewTarget.Create( new Vector2Int( width, height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Borealis.Gpu sample" );
		}

		foreach ( var (obj, position, speed) in spinning )
			obj.Transform = new Transform( position, Rotation.FromYaw( time * speed * 57.3f ) * Rotation.FromPitch( time * speed * 20 ) );

		var orbit = time * 0.15f;
		view.Position = new Vector3( MathF.Cos( orbit ) * 700, MathF.Sin( orbit ) * 700, 330 );
		view.Rotation = Rotation.LookAt( -view.Position.WithZ( view.Position.z - 40 ) );
		view.Viewport = new Rect( 0, 0, width, height );

		RenderHost.Current.NextFrame();
		system.Render( world, view, target );

		var (texture, layout) = BackendDevice.Unwrap( target.Color );
		return (texture, layout, target.Color);
	}

	/// <summary>
	/// A compiled material from the game's content, tinted with <paramref name="color"/> if asked; the colour on
	/// white.vmat when it isn't built.
	/// </summary>
	GpuMaterial Material( string path, string name, Color color, bool tint = false )
	{
		if ( backend.Content.Find( path ) is null ) return backend.CreateMaterial( name, color );
		var values = tint ? new Dictionary<string, float[]> { ["g_vColorTint"] = [color.r, color.g, color.b, 0] } : null;
		return backend.LoadMaterial( path, values, name );
	}

	/// <summary>
	/// A box of <paramref name="size"/> centred on its origin, counter-clockwise from outside. Each face's uvs span 0..1,
	/// or its size times <paramref name="uvScale"/> when that's given.
	/// </summary>
	static RenderMesh Box( BackendDevice device, Vector3 size, GpuMaterial material, string name, float uvScale = 0 )
	{
		Vector3[] normals = [Vector3.Forward, Vector3.Backward, Vector3.Left, Vector3.Right, Vector3.Up, Vector3.Down];
		var vertices = new List<MeshVertex>();
		var indices = new List<uint>();

		foreach ( var n in normals )
		{
			// (u, v, n) right-handed, so the quad winds counter-clockwise seen from outside
			var u = MathF.Abs( n.z ) > 0.5f ? Vector3.Forward : Vector3.Cross( Vector3.Up, n );
			var v = Vector3.Cross( n, u );
			var first = (uint)vertices.Count;
			var su = uvScale > 0 ? MathF.Abs( Vector3.Dot( u, size ) ) * uvScale : 1;
			var sv = uvScale > 0 ? MathF.Abs( Vector3.Dot( v, size ) ) * uvScale : 1;

			vertices.Add( new MeshVertex( (n - u - v) * 0.5f * size, n, new Vector2( 0, sv ) ) );
			vertices.Add( new MeshVertex( (n + u - v) * 0.5f * size, n, new Vector2( su, sv ) ) );
			vertices.Add( new MeshVertex( (n + u + v) * 0.5f * size, n, new Vector2( su, 0 ) ) );
			vertices.Add( new MeshVertex( (n - u + v) * 0.5f * size, n, new Vector2( 0, 0 ) ) );
			indices.AddRange( [first, first + 1, first + 2, first, first + 2, first + 3] );
		}

		return BackendMesh.Create( device, vertices.ToArray(), indices.ToArray(), material, name );
	}

	/// <summary>A UV sphere, counter-clockwise from outside.</summary>
	static RenderMesh Sphere( BackendDevice device, float radius, GpuMaterial material, string name )
	{
		const int rings = 16, segments = 24;
		var vertices = new List<MeshVertex>();
		var indices = new List<uint>();

		for ( int r = 0; r <= rings; r++ )
		{
			var phi = r / (float)rings * MathF.PI;
			for ( int s = 0; s <= segments; s++ )
			{
				var theta = s / (float)segments * MathF.Tau;
				var n = new Vector3( MathF.Sin( phi ) * MathF.Cos( theta ), MathF.Sin( phi ) * MathF.Sin( theta ), MathF.Cos( phi ) );
				vertices.Add( new MeshVertex( n * radius, n, new Vector2( s / (float)segments, r / (float)rings ) ) );
			}
		}

		for ( int r = 0; r < rings; r++ )
		{
			for ( int s = 0; s < segments; s++ )
			{
				var a = (uint)(r * (segments + 1) + s);
				var b = a + segments + 1;
				indices.AddRange( [a, b + 1, a + 1, a, b, b + 1] );
			}
		}

		return BackendMesh.Create( device, vertices.ToArray(), indices.ToArray(), material, name );
	}

	public void Dispose()
	{
		backend.Gpu.WaitIdle();
		target?.Dispose();
		system.Dispose();
		backend.Dispose();
		RenderHost.Current = previousHost;
	}
}
