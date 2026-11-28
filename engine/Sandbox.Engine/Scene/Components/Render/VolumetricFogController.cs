namespace Sandbox;

/// <summary>
/// Internal component for storing the baked fog texture
/// We don't need to expose the volumetric fog controller like we did previously with entities,
/// But we need to be fetch the baked fog texture from the map file
/// </summary>
[Title( "VolumetricFogController" )]
[Hide]
public class VolumetricFogController : Component, Component.ExecuteInEditor
{
	[Property, Hide] public Texture BakedFogTexture { get; set; }
	public float GlobalScale { get; set; } = 1.0f;

	internal static void InitializeFromLegacy( GameObject go, Sandbox.MapLoader.ObjectEntry kv )
	{
		// Only one VolumetricFogController per scene
		foreach ( var item in go.Scene.GetAllComponents<VolumetricFogController>() )
			item.Destroy();

		var component = go.Components.Create<VolumetricFogController>();
		component.BakedFogTexture = Texture.Load( kv.GetValue( "fogirradiancevolume", "" ) );

		var fogStrength = kv.GetValue( "FogStrength", 1.0f );
		var fogScale = kv.GetValue( "IndirectStrength", 1.0f );
		component.GlobalScale = fogStrength * fogScale;
	}

}
