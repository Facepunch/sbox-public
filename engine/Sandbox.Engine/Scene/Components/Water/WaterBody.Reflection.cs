using Sandbox.Rendering;

namespace Sandbox;

public sealed partial class WaterBody
{
	readonly List<SkyBox2D> _reflectionSkies = [];

	/// <summary>
	/// How the water reflects the scene. Either way the environment fills whatever the reflection can't see.
	/// </summary>
	[Property, Feature( "Reflection", Icon = "flip" ), Title( "Type" ), Order( -100 )]
	public ReflectionType Reflections { get; set; } = ReflectionType.ScreenSpace;

	/// <summary>
	/// Water uses the camera's screen-space reflections unless it asks for planar reflections and the water quality
	/// level allows them. Planar water whose view rendered no reflection for its plane falls back to screen-space
	/// reflections in the shader. With screen-space reflections turned off in the settings the environment fills everything.
	/// </summary>
	internal ReflectionMode ReflectionLevel => Reflections == ReflectionType.Planar && PlanarDownsampleFactor > 0 ? ReflectionMode.Planar : ReflectionMode.ScreenSpace;

	/// <summary>
	/// What the water reflects where the screen-space or planar reflection has nothing, and everywhere when
	/// screen-space reflections are off in the settings.
	/// </summary>
	[Property, Feature( "Reflection" ), Title( "Environment" ), Order( -99 ), EnumDropdown]
	public EnvironmentMode ReflectionEnvironment { get; set; } = EnvironmentMode.Sky;

	/// <summary>
	/// Cubemap reflected by the Custom Cubemap environment. Use a filtered HDR cubemap with roughness mipmaps.
	/// Missing or invalid textures fall back to scene probes.
	/// </summary>
	[Property, Feature( "Reflection" ), Title( "Cubemap" ), Order( -98 )]
	[ShowIf( nameof( ReflectionEnvironment ), EnvironmentMode.Custom )]
	public Texture ReflectionEnvmap { get; set; }

	/// <summary>
	/// What the surface reflects where its reflection has nothing: the cubemap (null for the scene's probes), its encoding and
	/// orientation, and the reflection mode. The game side finds it each frame (enabling, replacing or rotating the sky updates
	/// the water too); the capture commands that bind it are recorded again only when it changes (<see cref="BindEnvironment"/>).
	/// </summary>
	readonly struct EnvironmentBinding
	{
		public readonly Texture Texture;
		public readonly int Format;
		public readonly Rotation Rotation;
		public readonly Color Tint;
		public readonly ReflectionMode Mode;

		public EnvironmentBinding( Texture texture, int format, Rotation rotation, Color tint, ReflectionMode mode )
		{
			Texture = texture;
			Format = format;
			Rotation = rotation;
			Tint = tint;
			Mode = mode;
		}

		/// <summary>Exactly the same binding: a sky turning slowly still records again.</summary>
		public bool Same( in EnvironmentBinding other ) => ReferenceEquals( Texture, other.Texture ) && Format == other.Format
			&& Rotation.x == other.Rotation.x && Rotation.y == other.Rotation.y && Rotation.z == other.Rotation.z && Rotation.w == other.Rotation.w
			&& Tint.r == other.Tint.r && Tint.g == other.Tint.g && Tint.b == other.Tint.b && Tint.a == other.Tint.a && Mode == other.Mode;
	}

	/// <summary>
	/// The body-specific environment, without changing the scene's probes or other materials: the custom cubemap, or the
	/// scene's active 2D sky with its rotation and tint. Game state only: nothing here asks the renderer for anything.
	/// </summary>
	EnvironmentBinding CurrentEnvironment()
	{
		Texture texture = null;
		var rotation = Rotation.Identity;
		var tint = Color.White;
		var environment = ReflectionEnvironment;
		int format = 0;

		if ( environment == EnvironmentMode.Custom )
		{
			texture = ReflectionEnvmap;
		}
		else if ( environment == EnvironmentMode.Sky )
		{
			SkyBox2D sky = null;
			_reflectionSkies.Clear();
			Scene.GetAll( _reflectionSkies );
			foreach ( var candidate in _reflectionSkies )
			{
				if ( !candidate.Active )
					continue;

				sky = candidate;
				break;
			}

			_reflectionSkies.Clear();

			if ( sky.IsValid() )
			{
				var material = sky.SkyMaterial;
				texture = material?.GetTexture( "g_tSkyTexture" );
				format = material?.GetFeature( "F_TEXTURE_FORMAT2" ) ?? 0;
				rotation = sky.WorldRotation;
				tint = sky.Tint;
			}
		}

		bool hasEnvironment = texture is { IsValid: true, IsError: false } && texture.Desc.IsCube && !texture.Desc.IsArray;
		return new EnvironmentBinding( hasEnvironment ? texture : null, format, rotation, tint, ReflectionLevel );
	}

	/// <summary>
	/// Bind the environment for the surface's draws as the capture commands play back, where the renderer draws: the
	/// cubemap's bindless index is looked up and its streaming touched there, not in game code.
	/// </summary>
	static void BindEnvironment( CommandList commands, in EnvironmentBinding environment )
	{
		var attributes = commands.Attributes;
		attributes.Set( "HasEnvironment", environment.Texture is not null );
		if ( environment.Texture is { } texture )
			attributes.SetIndex( "EnvironmentTextureIndex", texture );
		attributes.Set( "EnvironmentTextureFormat", environment.Format );

		var tint = environment.Tint;
		var rotation = environment.Rotation;
		attributes.Set( "ReflectionMode", (int)environment.Mode );
		attributes.Set( "EnvironmentTint", new Vector4( tint.r, tint.g, tint.b, tint.a ) );
		attributes.Set( "EnvironmentAxisX", rotation.Forward );
		attributes.Set( "EnvironmentAxisY", rotation.Left );
		attributes.Set( "EnvironmentAxisZ", rotation.Up );
	}

	/// <summary>
	/// Divisor of both planar capture dimensions, set by the water quality level. 1 is full resolution.
	/// Zero turns planar reflections off; bodies that asked for them use screen-space reflections instead.
	/// </summary>
	[ConVar( "r.water.planar.downsample", Min = 0, Max = 8, Help = "Planar water reflection resolution divisor (0: off, 1: full, 2: half, 4: quarter)." )]
	internal static int PlanarDownsampleFactor { get; set; } = 1;

	SceneReflectionPlane _reflectionPlane;

	/// <summary>
	/// Ask views for a planar reflection of the surface while planar reflections are on. Views decide whether to
	/// render one and publish it; the surface's own footprint hides reflected objects poking through it.
	/// </summary>
	void UpdateReflectionPlane( SurfaceAttributes attributes )
	{
		var size = new Vector2( MathF.Abs( Size.x * WorldScale.x ), MathF.Abs( Size.y * WorldScale.y ) );
		attributes.Set( "ReflectionWaterHalfSize", size * 0.5f );
		attributes.Set( "ReflectionWaterInfinite", Ocean );

		if ( ReflectionLevel != ReflectionMode.Planar || !_surface.Enabled || Scene.SceneWorld is not { } world )
		{
			DestroyReflectionPlane();
			return;
		}

		if ( _reflectionPlane?.World != world )
		{
			DestroyReflectionPlane();
			_reflectionPlane = new SceneReflectionPlane( world );
		}

		_reflectionPlane.Update( new Transform( SurfaceOrigin, WorldRotation ), size, Ocean, CaptureDisplacement, PlanarDownsampleFactor, _surfaceTag );
	}

	void DestroyReflectionPlane()
	{
		_reflectionPlane?.Delete();
		_reflectionPlane = null;
	}

	/// <summary>
	/// Reflection sources supported by the water surface.
	/// Values match the REFLECTION_MODE constants in the water shader.
	/// </summary>
	internal enum ReflectionMode
	{
		/// <summary>
		/// A mirrored camera clipped to the surface, including objects outside the main view.
		/// </summary>
		Planar = 1,

		/// <summary>
		/// The camera's ScreenSpaceReflections output, with the selected environment filling misses.
		/// </summary>
		ScreenSpace = 2
	}

	/// <summary>
	/// How water reflects the scene.
	/// </summary>
	[Expose]
	public enum ReflectionType
	{
		/// <summary>
		/// Reflects what's already on screen, with the environment filling the rest. Cheap.
		/// </summary>
		[Title( "Screen Space" )]
		ScreenSpace,

		/// <summary>
		/// Renders the scene again, mirrored, for sharp reflections of everything above the water. Costs a second
		/// view: water on the same plane shares one, and each view renders at most one, for the plane covering the
		/// most of the screen. Other water, and all water at Low and Medium water quality, uses screen space.
		/// </summary>
		Planar
	}

	/// <summary>
	/// What water reflects where its reflection has nothing.
	/// </summary>
	[Expose]
	public enum EnvironmentMode
	{
		/// <summary>
		/// The scene's environment probes, blended by the engine.
		/// </summary>
		[Title( "Scene Probes" )]
		SceneProbes,

		/// <summary>
		/// The scene's active 2D skybox, with its rotation and tint, ignoring local probes.
		/// </summary>
		[Title( "Scene Sky" )]
		Sky,

		/// <summary>
		/// A cubemap you assign.
		/// </summary>
		[Title( "Custom Cubemap" )]
		Custom
	}
}
