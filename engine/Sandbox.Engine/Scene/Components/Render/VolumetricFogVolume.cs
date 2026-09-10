namespace Sandbox;

/// <summary>
/// Adds a volumetric fog volume to the scene.
/// </summary>
[Title( "VolumetricFogVolume" )]
[Category( "Rendering" )]
[Icon( "visibility" )]
[EditorHandle( "materials/gizmo/VolumetricFogVolume.png" )]
public class VolumetricFogVolume : Component, Component.ExecuteInEditor
{
	SceneFogVolume sceneObject;

	[Property] public BBox Bounds { get; set; } = BBox.FromPositionAndSize( 0, 300 );
	[Property, Range( 0, 1 )] public float Strength { get; set; } = 1.0f;
	[Property, Range( 0, 1 )] public float FalloffExponent { get; set; } = 1.0f;
	[Property] public Color Color { get; set; } = Color.White;

	bool isFromMap;

	protected override void DrawGizmos()
	{
		using var scope = Gizmo.Scope( $"volumetricfogvolume-{GetHashCode()}" );

		if ( !Gizmo.IsSelected && !Gizmo.IsHovered )
			return;

		Gizmo.Draw.Color = Color.White.WithAlpha( Gizmo.IsSelected ? 0.9f : 0.4f );
		Gizmo.Draw.LineBBox( Bounds );
	}

	protected override void OnEnabled()
	{
		Assert.True( !sceneObject.IsValid() );
		Assert.NotNull( Scene );

		sceneObject = new SceneFogVolume( Scene.SceneWorld, WorldTransform, Bounds, Strength, FalloffExponent );
	}

	protected override void OnDisabled()
	{
		sceneObject?.Delete();
		sceneObject = null;
	}

	protected override void OnPreRender()
	{
		if ( !sceneObject.IsValid() )
			return;

		var strength = Strength;

		if ( isFromMap )
		{
			// this is a legacy thing, not too much of a big deal yet but we shouldn't really be looking
			// up every frame. Should really have VolumetricFogController
			strength *= Scene.GetAll<VolumetricFogController>().FirstOrDefault()?.GlobalScale ?? 1.0f;
		}

		sceneObject.Transform = WorldTransform;
		sceneObject.BoundingBox = Bounds;
		sceneObject.FogStrength = strength;
		sceneObject.FalloffExponent = FalloffExponent;
		sceneObject.Color = Color;
	}

	internal static void InitializeFromLegacy( GameObject go, Sandbox.MapLoader.ObjectEntry kv )
	{
		var component = go.Components.Create<VolumetricFogVolume>();

		var boundsMin = kv.GetValue( "box_mins", new Vector3( -64.0f, -64.0f, -64.0f ) );
		var boundsMax = kv.GetValue( "box_maxs", new Vector3( 64.0f, 64.0f, 64.0f ) );
		var fogStrength = kv.GetValue( "FogStrength", 1.0f );
		var falloffExponent = kv.GetValue( "FalloffExponent", 1.0f );

		component.Bounds = new BBox( boundsMin, boundsMax );
		component.Strength = fogStrength;
		component.FalloffExponent = falloffExponent;
		component.isFromMap = true;
	}

}
