namespace Sandbox;

/// <summary>
/// How a <see cref="Scene"/> is lit. For use with <see cref="Scene.LightingMode"/>.
/// </summary>
[Expose]
public enum SceneLightingMode
{
	/// <summary>
	/// By its lights, sky and environment probes as it runs. Nothing is baked. This is the default.
	/// </summary>
	[Title( "IBL" ), Icon( "panorama" )]
	IBL,

	/// <summary>
	/// From a baked lightmap and light probes, <see cref="Scene.Lightmap"/>.
	/// </summary>
	[Icon( "grid_on" )]
	Lightmap
}

public partial class Scene
{
	/// <summary>
	/// How this scene is lit. <see cref="Lightmap"/> exists only while this is <see cref="SceneLightingMode.Lightmap"/>,
	/// and leaving it clears the bake.
	/// </summary>
	public SceneLightingMode LightingMode
	{
		get => Lightmap is null ? SceneLightingMode.IBL : SceneLightingMode.Lightmap;
		set
		{
			if ( value == LightingMode ) return;

			Lightmap?.Clear();
			Lightmap = value == SceneLightingMode.Lightmap ? new BakedLightmap( this ) : null;
		}
	}

	/// <summary>
	/// This scene's baked lightmap: how it's baked and what the last bake made. Saved with the scene. Null unless
	/// <see cref="LightingMode"/> is <see cref="SceneLightingMode.Lightmap"/>.
	/// </summary>
	// One bake a scene, and a scene loaded through a MapInstance leaves its own behind. Have MapInstance hold the map's and its objects ask it first
	public BakedLightmap Lightmap
	{
		get;
		// A bake belongs to the lightmap it started on: it stops with it, and leaves the scene alone from then on
		private set
		{
			if ( field == value ) return;

			var old = field;
			field = value;
			old?.Stop();
		}
	}
}
