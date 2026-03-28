using static Sandbox.Component;

namespace Sandbox;

[Expose]
public abstract class Light : Component, IColorProvider, ExecuteInEditor, ITintable
{
	SceneLight _sceneObject;

	/// <summary>
	/// The main color of the light
	/// </summary>
	[Property, MakeDirty] public Color LightColor { get; set; } = "#E9FAFF";

	[Property, MakeDirty, Category( "Fog Settings" )] public FogInfluence FogMode { get; set; } = FogInfluence.Enabled;
	[Property, MakeDirty, Range( 0, 1 ), Category( "Fog Settings" )] public float FogStrength { get; set; } = 1.0f;

	/// <summary>
	/// Should this light cast shadows?
	/// </summary>
	[Property, MakeDirty, Category( "Shadows" ), Order( -10 )] public bool Shadows { get; set; } = true;

	[Property, MakeDirty, Range( 0, 1 ), Category( "Shadows" ), Advanced] public float ShadowBias { get; set; } = 0.0005f;

	[Property, MakeDirty, Range( 0, 1 ), Category( "Shadows" )] public float ShadowHardness { get; set; } = 0.0f;

	Color IColorProvider.ComponentColor => LightColor;

	Color ITintable.Color { get => LightColor; set => LightColor = value; }

	public enum FogInfluence
	{
		[Icon( "blur_off" )]
		Disabled = SceneLight.FogLightingMode.None,
		[Icon( "blur_linear" )]
		Enabled = SceneLight.FogLightingMode.Dynamic,
		[Icon( "blur_on" )]
		WithoutShadows = SceneLight.FogLightingMode.DynamicNoShadows
	}

	protected override void OnAwake()
	{
		Tags.Add( "light" );

		base.OnAwake();
	}

	protected override void OnEnabled()
	{
		Assert.True( !_sceneObject.IsValid(), "_sceneObject should be null" );
		Assert.NotNull( Scene, "Scene should not be null" );

		_sceneObject = CreateSceneObject();

		if ( _sceneObject.IsValid() )
		{
			UpdateSceneObject( _sceneObject );
			OnTransformChanged();
			OnTagsChanged();

			Transform.OnTransformChanged += OnTransformChanged;
		}
	}

	protected override void OnDisabled()
	{
		Transform.OnTransformChanged -= OnTransformChanged;

		_sceneObject?.Delete();
		_sceneObject = null;
	}

	protected abstract SceneLight CreateSceneObject();

	protected virtual void UpdateSceneObject( SceneLight o )
	{
		o.Component = this;
		o.LightColor = LightColor;
		o.ShadowsEnabled = Shadows;

		o.FogLighting = (SceneLight.FogLightingMode)FogMode; // these should map directly
		o.FogStrength = FogStrength;

		o.ShadowBias = ShadowBias;
		o.ShadowHardness = ShadowHardness;
	}

	protected override void OnDirty()
	{
		if ( _sceneObject.IsValid() )
		{
			UpdateSceneObject( _sceneObject );
		}
	}

	void OnTransformChanged()
	{
		if ( !_sceneObject.IsValid() )
			return;

		_sceneObject.Transform = WorldTransform;
	}

	/// <summary>
	/// Tags have been updated - lets update our light's tags
	/// </summary>
	protected override void OnTagsChanged()
	{
		if ( !_sceneObject.IsValid() )
			return;

		_sceneObject?.Tags.SetFrom( Tags );
	}
}
