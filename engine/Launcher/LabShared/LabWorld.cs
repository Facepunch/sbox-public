using Borealis;

namespace Sandbox.Lab;

/// <summary>
/// Where a <see cref="LabWorld"/> gets its meshes, materials and textures, so the world building needs nothing but
/// Borealis. BorealisLab's <c>BackendLabContent</c> reads them on a Borealis.Gpu device.
/// </summary>
internal interface ILabContent
{
	RenderMesh Mesh( string path );

	/// <summary>
	/// The material, or null for a null path.
	/// </summary>
	GpuMaterial Material( string path );

	GpuTexture Texture( string path );

	/// <summary>
	/// A 2D sky material, and the cubemap it draws.
	/// </summary>
	(GpuMaterial Material, GpuTexture Cubemap) Sky( string path );

	/// <summary>
	/// A sky cubemap, for cubemap fog.
	/// </summary>
	GpuTexture SkyCube();

	/// <summary>
	/// Pose a skinned object from a sequence, frozen at <paramref name="time"/> (0 to 1), and fit its bounds to the pose.
	/// </summary>
	void Pose( MeshObject obj, string sequence, float time );
}

/// <summary>
/// A <see cref="LabPreset"/> built into a <see cref="RenderWorld"/>: the grid, probes, sky and fog, the floor and lights,
/// and where the camera looks from. BorealisLab builds it.
/// </summary>
internal sealed class LabWorld
{
	public LabPreset Preset { get; }
	public RenderWorld World { get; } = new();

	/// <summary>
	/// What the camera orbits.
	/// </summary>
	public Vector3 Focus { get; private set; }

	/// <summary>
	/// How far the camera starts from <see cref="Focus"/>.
	/// </summary>
	public float DefaultDistance { get; private set; }

	/// <summary>
	/// The camera's starting angles.
	/// </summary>
	public const float DefaultPitch = 25, DefaultYaw = 35;

	readonly List<LightObject> orbitingLights = new();
	float lightRingRadius;
	float lightHeight;

	LabWorld( LabPreset preset ) => Preset = preset;

	public static LabWorld Build( LabPreset preset, ILabContent content )
	{
		var lab = new LabWorld( preset );
		lab.Build( content );
		return lab;
	}

	void Build( ILabContent content )
	{
		var scene = Preset;
		var world = World;

		var models = scene.Models.Select( content.Mesh ).ToArray();
		var first = models[0];
		var spacing = models.Max( m => MathF.Max( m.Bounds.Size.x, m.Bounds.Size.y ) ) * 1.5f;
		var offset = (scene.Grid - 1) * spacing * 0.5f;

		var material = content.Material( scene.Material );
		var checker = scene.Checker.Select( x => (Material: content.Material( x.Material ), x.Alpha) ).ToArray();

		for ( int x = 0; x < scene.Grid; x++ )
		{
			for ( int y = 0; y < scene.Grid; y++ )
			{
				var position = new Vector3( x * spacing - offset, y * spacing - offset, 0 );
				var obj = new MeshObject( models[(x + y) % models.Length], new Transform( position ) ) { IsStatic = scene.Static, MaterialOverride = material };

				var cell = (x + y) % (checker.Length + 1) - 1;
				if ( cell >= 0 )
				{
					obj.MaterialOverride = checker[cell].Material ?? material;
					obj.Tint = Color.White.WithAlpha( checker[cell].Alpha );
				}

				if ( scene.Sequences.Length > 0 && obj.Mesh.IsSkinned )
					content.Pose( obj, scene.Sequences[(x * scene.Grid + y) % scene.Sequences.Length], Args.Float( "-sequence-time", scene.SequenceTime ) );

				world.Add( obj );
			}
		}

		if ( scene.EnvMap )
		{
			// A box around the grid, tall as it's wide - a room for the reflections to project onto
			var half = MathF.Max( scene.Grid * spacing, spacing * 2 ) * 0.8f;
			var bounds = new BBox( new Vector3( -half, -half, first.Bounds.Mins.z ), new Vector3( half, half, half ) );
			world.Add( new EnvMapObject( content.Texture( "textures/cubemaps/default.vtex" ), bounds, Transform.Zero ) );
		}

		if ( scene.Sky is not null )
		{
			// What SkyBox2D sets up: the sky, and a probe of its cubemap around everything, at the lowest priority
			var sky = content.Sky( scene.Sky );
			world.Lighting.SkyMaterial = sky.Material;
			world.Add( new EnvMapObject( sky.Cubemap, BBox.FromPositionAndSize( Vector3.Zero, int.MaxValue ), Transform.Zero )
			{
				Priority = -5,
				Feathering = 0.01f,
			} );
		}

		if ( scene.ImageBasedAmbient )
			world.Lighting.AmbientColor = world.Lighting.AmbientColor.WithAlpha( 0 );

		// Fog scaled to the grid: clear close up, thick at its far edge, thinning above the objects
		var size = scene.Grid * spacing;
		if ( scene.GradientFog )
		{
			world.Lighting.GradientFog = new Borealis.GradientFogSettings
			{
				Enabled = true,
				StartDistance = size * 0.05f,
				EndDistance = size * 0.8f,
				StartHeight = 0,
				EndHeight = first.Bounds.Size.z * 4,
				MaximumOpacity = 0.9f,
				DistanceFalloffExponent = 1,
				VerticalFalloffExponent = 1,
				Color = new Color( 0.55f, 0.6f, 0.7f ),
			};
		}

		if ( scene.CubemapFog )
		{
			var fog = world.Lighting.CubemapFog;
			fog.Enabled = true;
			fog.Texture = content.SkyCube();
			fog.StartDistance = size * 0.05f;
			fog.EndDistance = size * 0.8f;
			fog.FalloffExponent = 1;
			fog.LodBias = 0.5f;
			fog.Tint = Color.White;
			fog.Transform = Transform.Zero;
		}

		if ( scene.SunDirection is { } sunDirection )
			world.Lighting.SunDirection = sunDirection;

		if ( scene.SunColor is { } sunColor )
			world.Lighting.SunColor = sunColor;

		if ( scene.SecondSun is { } second )
			world.Lighting.ExtraSuns.Add( new Borealis.ExtraSun { Direction = second.Direction, Color = second.Color, Shadows = true } );

		Focus = first.Bounds.Center;
		DefaultDistance = scene.CameraDistance ?? MathF.Max( first.Bounds.Size.Length * 1.6f, scene.Grid * spacing * 1.2f );

		if ( scene.HasLocalLights || scene.Floor )
			AddLights( content, first, MathF.Max( scene.Grid * spacing, spacing * 2 ) );
	}

	/// <summary>
	/// A floor to catch the light and the shadows, a ring of point lights over it, and a spot
	/// straight down.
	/// </summary>
	void AddLights( ILabContent content, RenderMesh mesh, float extent )
	{
		var scene = Preset;
		var world = World;

		// Let the local lights carry the scene
		if ( scene.HasLocalLights )
		{
			world.Lighting.SunColor = world.Lighting.SunColor * scene.Sun;
			world.Lighting.AmbientColor = new Color( 0.02f, 0.02f, 0.03f );
		}

		var floor = content.Mesh( "models/dev/plane.vmdl" );
		var floorSize = MathF.Max( floor.Bounds.Size.x, 1 );
		var floorZ = mesh.Bounds.Mins.z - floor.Bounds.Maxs.z;
		world.Add( new MeshObject( floor, new Transform( new Vector3( 0, 0, floorZ ), Rotation.Identity, extent * 1.6f / floorSize ) ) { IsStatic = scene.Static } );

		lightRingRadius = extent * 0.35f;
		lightHeight = mesh.Bounds.Maxs.z + mesh.Bounds.Size.z;

		for ( int i = 0; i < scene.Lights; i++ )
		{
			var light = new LightObject( LightObject.LightKind.Point, Transform.Zero )
			{
				Color = new ColorHsv( i * 360.0f / scene.Lights, 0.75f, 1 ).ToColor() * 4,
				Radius = extent * scene.LightRadius,
				CastShadows = scene.LightShadows,
			};

			orbitingLights.Add( light );
			world.Add( light );
		}

		if ( scene.Spot )
		{
			world.Add( new LightObject( LightObject.LightKind.Spot, new Transform( new Vector3( 0, 0, lightHeight * 4 ), Rotation.LookAt( Vector3.Down ) ) )
			{
				Color = new Color( 1.0f, 0.95f, 0.8f ) * 6,
				Radius = lightHeight * 8,
				ConeInner = 15,
				ConeOuter = 25,
				CastShadows = scene.LightShadows,
				IsStatic = scene.Static,
			} );
		}

		MoveLights( 0 );
	}

	/// <summary>
	/// Put the point lights where they are at <paramref name="time"/> seconds round their ring. Captures keep them at 0,
	/// so they're repeatable.
	/// </summary>
	public void MoveLights( float time )
	{
		for ( int i = 0; i < orbitingLights.Count; i++ )
		{
			var angle = time * 0.5f + i * MathF.Tau / orbitingLights.Count;
			var position = new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0 ) * lightRingRadius + Vector3.Up * lightHeight;
			orbitingLights[i].Transform = new Transform( position );
		}
	}

	/// <summary>
	/// Look at <see cref="Focus"/> from <paramref name="distance"/> away at these angles, with depth range to suit.
	/// </summary>
	public void Aim( RenderView view, float pitch, float yaw, float distance )
	{
		view.Rotation = Rotation.From( pitch, yaw, 0 );
		view.Position = Focus - view.Rotation.Forward * distance;
		view.ZNear = MathF.Max( 0.1f, distance * 0.01f );
		view.ZFar = distance * 10.0f;
	}
}
