using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2;
using Borealis.Source2.World;
using NumericsVector3 = System.Numerics.Vector3;

namespace Sandbox.BorealisLab.Maps;

/// <summary>
/// What the map's entity lump gives a <see cref="MapWorld"/>, read as the engine's map loaders read it
/// (<c>SceneMapLoader</c>, <c>MapInstance</c>):
/// <list type="bullet">
/// <item><c>light_spot</c>, <c>light_omni</c>: <see cref="LightObject"/>s. Baked ones (<c>directlight</c> 1, or 3 with
/// mixed shadows) carry their <c>bakelightindex</c>, which the lightmaps' and probes' direct light indices name, and their
/// colour scaled by <c>bakelightindexscale</c>, as native packs a baked light.</item>
/// <item><c>light_environment</c>: the sun, its colour and direction, and its baked entry (<see cref="SceneLighting.SunBakeIndex"/>).</item>
/// <item><c>env_gradient_fog</c>, <c>env_cubemap_fog</c>: the world's fog.</item>
/// <item><c>env_sky</c>: the 2D sky and its whole-world probe (<c>SkyBox2D</c>).</item>
/// <item><c>env_combined_light_probe_volume</c>, <c>env_light_probe_volume</c>: the probe volumes, with their own boxes.</item>
/// <item><c>skybox_reference</c> and the sky package's <c>sky_camera</c>: the 3D skybox.</item>
/// </list>
/// Cubemaps aren't loaded: the game renders the combined volumes' cubemaps when it starts, and the map has none
/// compiled. Sounds, players, cables, doors and physics props aren't drawn.
/// </summary>
internal sealed partial class MapWorld
{
	/// <summary>What each entity class gave the world, for reports.</summary>
	public SortedDictionary<string, int> EntityClasses { get; } = new();
	public int Lights { get; private set; }
	public int BakedLights { get; private set; }

	static IReadOnlyList<MapEntity> LoadEntities( ContentFiles content, WorldFile world )
	{
		var entities = new List<MapEntity>();
		var pending = new Queue<string>( world.EntityLumps );
		while ( pending.Count > 0 )
		{
			var lump = EntityLump.Load( content, pending.Dequeue() );
			entities.AddRange( lump.Entities );
			foreach ( var child in lump.ChildLumps ) pending.Enqueue( child );
		}

		return entities;
	}

	void ApplyEntities( RenderWorld target, Level level )
	{
		var lighting = target.Lighting;
		lighting.AmbientColor = Color.Transparent;

		foreach ( var e in level.Entities )
		{
			var used = e.ClassName switch
			{
				"light_spot" => AddLight( target, e, LightObject.LightKind.Spot ),
				"light_omni" => AddLight( target, e, LightObject.LightKind.Point ),
				"light_environment" or "light_directional" => SetSun( lighting, e, target == World ),
				"env_gradient_fog" => SetGradientFog( lighting, e ),
				"env_cubemap_fog" => SetCubemapFog( lighting, e ),
				"env_sky" => SetSky( target, e ),
				_ => false,
			};

			if ( used ) EntityClasses[e.ClassName] = EntityClasses.GetValueOrDefault( e.ClassName ) + 1;
		}
	}

	static Rotation RotationOf( MapEntity e )
	{
		var angles = e.Angles;
		return Rotation.From( angles.X, angles.Y, angles.Z );
	}

	static Vector3 V( NumericsVector3 v ) => new( v.X, v.Y, v.Z );

	/// <summary>A colour key, 0-255, as linear 0-1 (Hammer's light colours go to the scene lights unconverted).</summary>
	static Color ColorOf( MapEntity e, string key )
	{
		var c = e.Color( key, new System.Numerics.Vector4( 255 ) );
		return new Color( c.X / 255, c.Y / 255, c.Z / 255, c.W / 255 );
	}

	bool AddLight( RenderWorld target, MapEntity e, LightObject.LightKind kind )
	{
		if ( !e.Bool( "enabled", true ) ) return false;

		// SceneMapLoader.LightData: the colour times brightness times the attenuation's magnitude
		var attenuation0 = e.Float( "attenuation0" );
		var attenuation1 = e.Float( "attenuation1" );
		var attenuation2 = e.Float( "attenuation2", 1 );
		var scale = attenuation2 * 10000 + attenuation1 * 100 + attenuation0;
		var color = ColorOf( e, "color" ) * e.Float( "brightness", 1 ) * (scale > 0 ? scale : 1);

		// Hammer's direct light: 1 baked, 3 stationary (baked with runtime shadows), 2 dynamic
		var direct = (int)e.Float( "directlight", 2 );
		var baked = direct is 1 or 3;
		var bakeIndex = (int)e.Float( "bakelightindex", -1 );
		if ( baked ) color *= e.Float( "bakelightindexscale", 1 );

		var range = e.Float( "precomputedmaxrange" ) is var precomputed and > 0 ? precomputed : e.Float( "range", 1024 );
		var light = new LightObject( kind, new Transform( V( e.Origin ), RotationOf( e ) ) )
		{
			Color = new Color( color.r, color.g, color.b ),
			Radius = range,
			Attenuation = attenuation2 * 10000,
			CastShadows = e.Bool( "castshadows", true ),
			Baked = baked,
			MixedShadows = direct == 3,
			BakeIndex = baked ? bakeIndex : -1,
		};

		if ( kind == LightObject.LightKind.Spot )
		{
			light.ConeInner = e.Float( "innerconeangle", 45 );
			light.ConeOuter = e.Float( "outerconeangle", 60 );
		}

		target.Add( light );
		Lights++;
		if ( baked ) BakedLights++;
		return true;
	}

	/// <summary>
	/// The sun, as the bridge mirrors a map's directional light: its colour and direction, and, baked, its entry for the
	/// lightmaps' direct light indices packed as <c>PackBakedDirectionalGPULight</c> packs it (pointing from the receiver
	/// to the sun, placed 2e6 units out that way, colour scaled by <c>bakelightindexscale</c>).
	/// </summary>
	static bool SetSun( SceneLighting lighting, MapEntity e, bool scene )
	{
		if ( !e.Bool( "enabled", true ) ) return false;

		var rotation = RotationOf( e );
		var color = ColorOf( e, "color" ) * e.Float( "brightness", 1 );
		lighting.SunColor = new Color( color.r, color.g, color.b );
		lighting.SunDirection = rotation.Forward;

		// As MapInstance makes the sun into a DirectionalLight: shadow hardness 0.5, and contact shadows, on by default, in the scene
		lighting.SunShadowHardness = 0.5f;
		lighting.SunContactShadows = scene;

		// The scene's ambient light: the directional light's sky colour times its intensity, alpha from the colour
		// (MapInstance, then Scene's ambient sum). A skybox world is loaded into a scene world of its own, without it
		if ( scene )
		{
			var sky = ColorOf( e, "SkyColor" ) * e.Float( "SkyIntensity", 1 );
			lighting.AmbientColor = sky.WithAlpha( ColorOf( e, "SkyColor" ).a );
		}

		var direct = (int)e.Float( "directlight", 2 );
		var bakeIndex = (int)e.Float( "bakelightindex", -1 );
		var baked = direct is 1 or 3;
		lighting.SunShadowsBaked = baked;
		if ( !baked || bakeIndex < 0 )
		{
			lighting.SunBakeIndex = -1;
			lighting.SunBaked = null;
			return true;
		}

		// The translation is the light's position, which the indexed baked path lights from (towards each receiver, with
		// no falloff): LightDesc_t::RecalculateDerivedValues puts a directional light's 2e6 units out towards the sun
		var scaled = color * e.Float( "bakelightindexscale", 1 );
		var forward = -rotation.Forward;
		var up = rotation.Up;
		var left = Vector3.Cross( up, forward ).Normal;
		var position = forward * 2.0e6f;
		lighting.SunBakeIndex = bakeIndex;
		lighting.SunBaked = new global::Borealis.Features.LightBinnerFeature.GpuLight
		{
			Type = 2, // directional
			Flags = 0x2 | 0x4 | 0x8, // diffuse, specular, transmissive
			Color = new Vector3( scaled.r, scaled.g, scaled.b ),
			Radius = float.MaxValue,
			RadiusSquared = float.MaxValue,
			SpotLightInnerOuterConeCosines = new Vector4( 0, -1, 1, 0 ),
			Row0 = new Vector4( forward.x, left.x, up.x, position.x ),
			Row1 = new Vector4( forward.y, left.y, up.y, position.y ),
			Row2 = new Vector4( forward.z, left.z, up.z, position.z ),
			Row3 = new Vector4( 0, 0, 0, 1 ),
			ShadowMapIndex = 0xFFFFFFFF,
		};

		return true;
	}

	/// <summary><c>MapInstance.CreateGradientFog</c>, then the <c>GradientFog</c> component's settings.</summary>
	static bool SetGradientFog( SceneLighting lighting, MapEntity e )
	{
		var height = e.Origin.Z;
		var color = ColorOf( e, "fogcolor" );
		lighting.GradientFog = new GradientFogSettings
		{
			Enabled = e.Bool( "fogenabled" ),
			StartDistance = e.Float( "fogstart" ),
			EndDistance = e.Float( "fogend" ),
			StartHeight = height,
			EndHeight = height + MathF.Max( 1, e.Float( "fogendheight" ) ),
			MaximumOpacity = e.Float( "fogmaxopacity" ),
			Color = color.WithAlpha( 1 ),
			DistanceFalloffExponent = e.Float( "fogfalloffexponent" ),
			VerticalFalloffExponent = e.Float( "fogverticalexponent" ),
		};

		return true;
	}

	/// <summary><c>MapInstance.CreateCubemapFog</c>, then the <c>CubemapFog</c> component's settings: its sky material's cubemap.</summary>
	bool SetCubemapFog( SceneLighting lighting, MapEntity e )
	{
		if ( SkyTexture( e.String( "cubemapfogmaterial" ) ) is not { } texture ) return false;

		var fog = lighting.CubemapFog;
		fog.Enabled = true;
		fog.Texture = texture;
		fog.LodBias = 1 - e.Float( "cubemapfoglodbiase", 0.5f );
		fog.StartDistance = e.Float( "cubemapfogstartdistance" );
		fog.EndDistance = e.Float( "cubemapfogenddistance" );
		fog.FalloffExponent = e.Float( "cubemapfogfalloffexponent" );
		fog.HeightExponent = e.Float( "cubemapfogheightexponent" );
		fog.HeightStart = e.Float( "cubemapfogheightstart" );
		fog.HeightWidth = e.Float( "cubemapfogheightwidth" );
		fog.Transform = new Transform( V( e.Origin ), RotationOf( e ).Conjugate );
		fog.Tint = Color.White;
		return true;
	}

	/// <summary>A sky material's cubemap (<c>g_tSkyTexture</c>), or null.</summary>
	GpuTexture SkyTexture( string material )
	{
		if ( string.IsNullOrEmpty( material ) || Backend.LoadMaterial( material ) is not BackendMaterial m ) return null;
		return m.Textures.TryGetValue( "g_tSkyTexture", out var texture ) ? texture : null;
	}

	/// <summary>
	/// <c>SkyBox2D.InitializeFromLegacy</c>: the sky material, its tint and rotation, and with image-based lighting
	/// (<c>ibl</c>) a whole-world probe of its cubemap at priority -5 and feathering 0.01.
	/// </summary>
	bool SetSky( RenderWorld target, MapEntity e )
	{
		var name = e.String( "skyname" );
		if ( string.IsNullOrEmpty( name ) || Backend.LoadMaterial( name ) is not { } material ) return false;

		var lighting = target.Lighting;
		lighting.SkyMaterial = material;
		lighting.SkyTint = ColorOf( e, "tint_color" );
		lighting.SkyRotation = RotationOf( e );

		if ( e.Bool( "ibl", true ) && SkyTexture( name ) is { } texture )
		{
			const float Extent = 1e7f;
			target.Add( new EnvMapObject( texture, new BBox( new Vector3( -Extent ), new Vector3( Extent ) ), Transform.Zero ) { Priority = -5, Feathering = 0.01f } );
		}

		return true;
	}

	/// <summary>
	/// The probe volumes from the entity lump (<c>SceneMapLoader.CreateLightProbeVolume</c>): each with a probe texture, its
	/// own box and transform, its constants as <c>CSceneLightProbeVolumeObject::CreateConstants</c> makes them. Objects the
	/// compiler gave a volume need a probe. False when the lump has none.
	/// </summary>
	bool LoadProbeVolumes( RenderWorld target, Level level, List<(MeshObject Object, int Volume, NumericsVector3 Mins, NumericsVector3 Maxs)> probed )
	{
		var volumes = 0;
		foreach ( var e in level.Entities )
		{
			if ( e.ClassName is not ("env_light_probe_volume" or "env_combined_light_probe_volume") ) continue;
			if ( e.String( "lightprobetexture" ) is not { Length: > 0 } path ) continue;
			if ( Backend.LoadTexture( path ) is not BackendTexture { Dimension: TextureDimension.Texture3D } probe )
			{
				Problems.Add( $"probe volume {path} isn't readable" );
				continue;
			}

			var indices = Backend.LoadTexture( e.String( "lightprobetexture_dli" ) ?? "" ) is BackendTexture { Dimension: TextureDimension.Texture3D } dli ? dli : probe;
			var scalars = Backend.LoadTexture( e.String( "lightprobetexture_dls" ) ?? "" ) is BackendTexture { Dimension: TextureDimension.Texture3D } dls ? dls : probe;
			var mins = e.Vector( "box_mins", new NumericsVector3( -72 ) );
			var maxs = e.Vector( "box_maxs", new NumericsVector3( 72 ) );
			var transform = new Transform( V( e.Origin ), RotationOf( e ) );
			target.LightProbeVolumes.Add( new global::Borealis.LightProbeVolume
			{
				Transform = transform,
				BoxMins = V( mins ),
				BoxMaxs = V( maxs ),
				LightGroups = [0],
				Attributes = ProbeAttributes( probe, indices, scalars, transform, mins, maxs ),
			} );

			volumes++;
			ProbeVolumes++;
			EntityClasses[e.ClassName] = EntityClasses.GetValueOrDefault( e.ClassName ) + 1;
		}

		if ( volumes == 0 ) return false;
		foreach ( var (obj, _, _, _) in probed )
		{
			obj.NeedsLightProbe = true;
			ProbeObjects++;
		}

		return true;
	}

	/// <summary>A probe volume's attributes: <c>PerBatchLightProbeVolumeConstantBuffer_t</c> over its box and transform, and the flags.</summary>
	ShaderParameters ProbeAttributes( BackendTexture probe, GpuTexture indices, GpuTexture scalars, Transform transform, NumericsVector3 mins, NumericsVector3 maxs )
		=> Backend.LightProbeVolumeParameters( probe, indices, scalars, transform, V( mins ), V( maxs ) );

	/// <summary>
	/// The 3D skybox (<c>MapSkybox3D</c>): the sky world's <c>sky_camera</c> and scale, placed so the main world's
	/// <c>skybox_reference</c> sees it from its camera.
	/// </summary>
	void LoadSkybox( MapEntity reference, Level sky )
	{
		if ( sky.Entities.FirstOrDefault( e => e.ClassName == "sky_camera" ) is not { } camera ) return;
		var scale = camera.Float( "scale", 16 );
		if ( scale <= 0 ) return;

		World.Skybox3D = new global::Borealis.Skybox3D( SkyWorld ) { Scale = scale, Origin = V( camera.Origin ) - V( reference.Origin ) / scale };
		EntityClasses["sky_camera"] = 1;
		EntityClasses["skybox_reference"] = 1;
	}
}
