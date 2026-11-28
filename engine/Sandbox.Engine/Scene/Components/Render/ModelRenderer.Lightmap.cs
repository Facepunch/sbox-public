using Sandbox.Rendering;

namespace Sandbox;

public partial class ModelRenderer
{
	/// <summary>
	/// Corner of this model's charts in the scene's <see cref="Scene.Lightmap"/>, in luxels. Null when it isn't in it.
	/// </summary>
	[Property, Hide]
	public Vector2? LightmapOffset { get; set { if ( field == value ) return; field = value; UpdateObject(); } }

	/// <summary>
	/// The scene's bake when this model has a place in it and can still draw from it.
	/// </summary>
	internal BakedLightmap Lightmap => LightmapOffset is not null && CanLightmap && Scene?.Lightmap is { LuxelSize: > 0 } baked ? baked : null;

	internal void UpdateLightmap()
	{
		if ( LightmapOffset is not null ) UpdateObject();
	}

	/// <summary>
	/// Only a model that stays put and keeps its shape is in a bake.
	/// </summary>
	internal bool IsBaked => this is not SkinnedModelRenderer && GameObject.IsStatic && !_hasActiveDeformations && Model is not null && Model.HasRenderMeshes();

	/// <summary>
	/// A baked model whose materials can all sample a lightmap. Any other only casts shadows into it.
	/// </summary>
	internal bool CanLightmap => IsBaked && (MaterialOverride is not null ? LightmapGeometry.ReadsLightmaps( MaterialOverride ) : Model.Materials.All( LightmapGeometry.ReadsLightmaps ));

	/// <summary>
	/// The LOD a lightmapped copy is made from. It has no others.
	/// </summary>
	// A lightmapped model draws one LOD at any distance. Unwrap each LOD when that costs too much
	internal int LightmapLod => Math.Clamp( LodOverride ?? 0, 0, Math.Max( 0, Model.MaxLodLevel ) );

	Model _lightmapModel;
	(LightmapGeometry.Draw[], Vector2, string) _lightmapKey;

	/// <summary>
	/// The copy of the model with this renderer's lightmap coordinates, null when it isn't lightmapped.
	/// </summary>
	// Read back, unwrapped and built on the main thread each time the scene loads, trusting the unwrap to place charts as the bake did. Compile Scene stores them. Save each copy with the bake if uncompiled scenes ship
	Model LightmapModel()
	{
		if ( Lightmap is not { } baked )
			return _lightmapModel = null;

		// The same unwrap until the model, its body, scale or the luxel size changes, or the model is reloaded
		var geometry = LightmapGeometry.Unwrap( Model, BodyGroups, LightmapLod, WorldScale, baked.LuxelSize );
		var key = (geometry, LightmapOffset.Value, MaterialGroup);
		if ( _lightmapModel is not null && key.Equals( _lightmapKey ) )
			return _lightmapModel;

		_lightmapKey = key;
		return _lightmapModel = geometry is null ? null : LightmapGeometry.Build( geometry, LightmapOffset.Value, LightmapGeometry.MaterialSwap( Model, MaterialGroup ) );
	}
}
