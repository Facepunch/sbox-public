namespace Sandbox;

public sealed partial class MeshComponent
{
	/// <summary>
	/// Corner of this mesh's charts in the scene's <see cref="Scene.Lightmap"/>, in luxels. Null when it isn't in it.
	/// </summary>
	[Property, Hide]
	public Vector2? LightmapOffset { get; set { field = value; UpdateLightmap(); } }

	/// <summary>
	/// The scene's bake when this mesh has a place in it.
	/// </summary>
	internal BakedLightmap Lightmap => LightmapOffset is not null && Scene?.Lightmap is { LuxelSize: > 0 } baked ? baked : null;

	// The mesh is rebuilt for it on its next update
	internal void UpdateLightmap()
	{
		Mesh?.SetLightmap( Lightmap?.LuxelSize ?? 0, LightmapOffset ?? default, WorldScale );

		if ( !_sceneObject.IsValid() ) return;

		var textures = Lightmap?.VisibleTextures;

		// Its shadows from baked lights are in the lightmap, so their shadow maps leave it out
		_sceneObject.Flags.IsStatic = GameObject.IsStatic || textures is not null;
		_sceneObject.SetLightmap( textures );
	}
}
