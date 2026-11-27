namespace Sandbox;

/// <summary>
/// A scene's baked lightmap, <see cref="Scene.Lightmap"/>: how it's baked and what the last bake made, saved with the
/// scene. One lightmap for every mesh and static model in the scene, traced on the GPU with a compute BVH. A model
/// whose materials can't sample one casts shadows into it and is lit from the light probes it fills.
/// Directional lights and lights on static objects become baked lights: the lightmap holds how much of each
/// reaches a luxel, and the light they and the skybox bounce around the scene.
/// </summary>
[Expose]
public sealed partial class BakedLightmap
{
	Scene Scene { get; }

	internal BakedLightmap( Scene scene ) => Scene = scene;

	public enum LightmapSize
	{
		[Title( "512" )] Size512 = 512,
		[Title( "1024" )] Size1024 = 1024,
		[Title( "2048" )] Size2048 = 2048,
		[Title( "4096" )] Size4096 = 4096,
		//[Title( "8192" )] Size8192 = 8192,
	}

	/// <summary>
	/// Rays traced from each luxel.
	/// </summary>
	public enum BakeQuality
	{
		[Icon( "star_border" )] Low = 256,
		[Icon( "star_half" )] Medium = 1024,
		[Icon( "star" )] High = 4096,
	}

	/// <summary>
	/// The widest and tallest the lightmap can be. Luxels are as small as still lets the scene fit.
	/// </summary>
	[Property, EnumButtonGroup]
	public LightmapSize Size { get; set; } = LightmapSize.Size4096;

	/// <summary>
	/// How many rays are traced from each luxel, for soft shadows and for indirect light. Higher takes longer to bake.
	/// </summary>
	[Property, EnumButtonGroup]
	public BakeQuality Quality { get; set; } = BakeQuality.Medium;

	/// <summary>
	/// Smooth the noise out of indirect light and soft shadows, so a lower <see cref="Quality"/> does.
	/// </summary>
	[Property]
	public bool Denoise { get; set; } = true;

	/// <summary>
	/// Directional lights' cascaded shadow maps take in static geometry too, so its shadows are sharp up close, over the
	/// ones baked into the lightmap, which carry on past the cascades. Off, a baked sun's shadow maps leave static
	/// geometry out. Baking sets each directional light's Include Static Shadow Casters to this, and turns its Mixed
	/// Shadows on with it.
	/// </summary>
	[Property, Title( "Realtime Sun Shadows" )]
	public bool RealtimeSunShadows { get; set; } = true;

	/// <summary>
	/// How many times light bounces off surfaces. With none, only the sky and glowing surfaces light a luxel indirectly.
	/// </summary>
	[Property, Range( 0, 4 )]
	public int Bounces { get; set; } = 2;

	/// <summary>
	/// Darken indirect light where other surfaces are nearer than this, in units. 0 bakes no ambient occlusion.
	/// </summary>
	[Property, Range( 0, 256 )]
	public float AmbientOcclusion { get; set; } = 128;

	/// <summary>
	/// Bake the light volumetric fog scatters inside the scene's <see cref="VolumetricFogVolume"/>s: indirect light, and
	/// the baked lights' own, the sun's too, which then light no fog while the game runs.
	/// </summary>
	[Property, Title( "Volumetric Fog" )]
	public bool BakeFog { get; set; }

	/// <summary>
	/// Minimum distance in world units between the light probes that light what isn't lightmapped. They're baked into the scene's
	/// <see cref="LightProbeVolume"/>s. One is made around the scene if there are none.
	/// </summary>
	[Property, Range( 16, 256 ), Title( "Probe Spacing" )]
	public int ProbeSpacing { get; set => field = Math.Max( 16, value ); } = 64;

	/// <summary>
	/// Size of a luxel in units in the last bake.
	/// </summary>
	[Property, Hide]
	public float LuxelSize { get; set { field = value; Refresh(); } }

	/// <summary>
	/// The textures the last bake made. Every mesh and model with a place in the lightmap is lit from them.
	/// </summary>
	[Property, Hide]
	public LightmapTextures Textures { get; set { field = value; Refresh(); } }

	/// <summary>
	/// Show this bake in the scene preview. Switching it off uses realtime lighting and preserves the bake.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public bool PreviewEnabled
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			Refresh();
			foreach ( var light in Scene.GetAll<Light>() )
			{
				if ( light.BakeLightIndex >= 0 )
					light.UpdateBakedLightIndex();
			}

			foreach ( var volume in Scene.GetAll<LightProbeVolume>() )
				volume.Rebuild();
		}
	} = true;

	internal LightmapTextures VisibleTextures => PreviewEnabled ? Textures : null;

	/// <summary>
	/// The probe volume a bake made around the scene, which every bake fits to it again.
	/// </summary>
	[Property, Hide]
	public Guid FittedProbes { get; set; }

	void Refresh()
	{
		foreach ( var mesh in Scene.GetAll<MeshComponent>() ) mesh.UpdateLightmap();
		foreach ( var model in Scene.GetAll<ModelRenderer>() ) model.UpdateLightmap();
		foreach ( var aggregate in Scene.GetAll<AggregateRenderer>() ) aggregate.UpdateLightmap();
	}
}
