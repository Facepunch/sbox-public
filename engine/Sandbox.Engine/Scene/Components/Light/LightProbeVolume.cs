namespace Sandbox;

/// <summary>
/// Provides baked ambient lighting using the light probe volume textures produced by Hammer.
/// Mount loaders can supply textures in the same format. This component does not bake probes
/// and is hidden from the editor's component picker.
/// </summary>
[Expose]
[Hide]
[Title( "Light Probe Volume" )]
[Category( "Light" )]
[Icon( "all_out" )]
[EditorHandle( "materials/gizmo/lpv.png" )]
public sealed class LightProbeVolume : Component, Component.ExecuteInEditor, Component.DontExecuteOnServer
{
	/// <summary>
	/// Baked ambient cubes stored as six layers in a volume texture.
	/// No lighting volume is created until a texture is assigned.
	/// </summary>
	[Property]
	public Texture Texture
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			if ( Active ) Rebuild();
		}
	}

	/// <summary>
	/// Optional baked direct-light indices, matching the ambient probe grid.
	/// </summary>
	[Property]
	public Texture IndicesTexture
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			if ( Active ) Rebuild();
		}
	}

	/// <summary>
	/// Optional baked direct-light weights, matching the ambient probe grid.
	/// </summary>
	[Property]
	public Texture ScalarsTexture
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			if ( Active ) Rebuild();
		}
	}

	/// <summary>
	/// Probe bounds in local space. Native probe sampling uses position and rotation, not scale.
	/// </summary>
	[Property]
	public BBox Bounds
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			if ( Active ) Rebuild();
		}
	} = new BBox( new Vector3( -72 ), new Vector3( 72 ) );

	/// <summary>
	/// Higher priority takes precedence when multiple volumes contain an object's lighting origin.
	/// </summary>
	[Property]
	public int Priority
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			if ( Active ) Rebuild();
		}
	}

	/// <summary>
	/// Baked identifier used by static map objects to select their light probe volume.
	/// </summary>
	[Property, Hide]
	public int Handshake
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			if ( Active ) Rebuild();
		}
	}

	SceneLightProbe sceneObject;

	protected override void OnEnabled()
	{
		Transform.OnTransformChanged += Rebuild;
		Rebuild();
	}

	protected override void OnDisabled()
	{
		Transform.OnTransformChanged -= Rebuild;
		sceneObject?.Delete();
		sceneObject = null;
	}

	void Rebuild()
	{
		// Probe constants include world-to-texture placement. Recreating also invalidates the
		// managed renderer's cached attributes and volume selections.
		sceneObject?.Delete();
		sceneObject = null;

		if ( Texture is null ) return;

		var size = Bounds.Size;
		if ( !Bounds.Mins.IsFinite || !Bounds.Maxs.IsFinite || size.x <= 0 || size.y <= 0 || size.z <= 0 )
		{
			Log.Warning( $"{GameObject.Name}: light probe bounds must be finite and have positive size." );
			return;
		}

		sceneObject = new SceneLightProbe( Scene.SceneWorld, Texture, IndicesTexture, ScalarsTexture,
			Bounds, WorldTransform.WithScale( 1 ), Handshake, Priority );
		sceneObject.Component = this;
		sceneObject.Tags.SetFrom( Tags );
	}

	protected override void OnTagsChanged()
	{
		if ( !sceneObject.IsValid() ) return;
		sceneObject.Tags.SetFrom( Tags );
	}

	protected override void DrawGizmos()
	{
		Gizmo.Hitbox.BBox( Bounds );
		if ( !Gizmo.IsSelected && !Gizmo.IsHovered ) return;
		Gizmo.Draw.Color = Color.Yellow;
		Gizmo.Draw.LineBBox( Bounds );
	}

	internal static void InitializeFromLegacy( GameObject go, MapLoader.ObjectEntry kv )
	{
		var component = go.Components.Create<LightProbeVolume>();
		component.Texture = kv.GetResource<Texture>( "lightprobetexture" );
		component.IndicesTexture = kv.GetResource<Texture>( "lightprobetexture_dli" );
		component.ScalarsTexture = kv.GetResource<Texture>( "lightprobetexture_dls" );
		component.Bounds = new BBox(
			kv.GetValue( "box_mins", new Vector3( -72 ) ),
			kv.GetValue( "box_maxs", new Vector3( 72 ) ) );
		component.Handshake = kv.GetValue<int>( "handshake" );
		component.Priority = kv.GetValue<int>( "indoor_outdoor_level" );
	}
}
