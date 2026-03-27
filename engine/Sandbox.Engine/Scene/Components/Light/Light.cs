using static Sandbox.Component;

namespace Sandbox;

[Expose]
public abstract class Light : Component, IColorProvider, ExecuteInEditor, ITintable
{
	SceneLight _sceneObject;

	/// <summary>
	/// The main color of the light
	/// </summary>
	[Property]
	public Color LightColor
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	} = "#E9FAFF";

	/// <summary>
	/// Should this light cast shadows?
	/// </summary>
	[Property]
	public bool Shadows
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	} = true;


	[Property, Category( "Fog Settings" )]
	public FogInfluence FogMode
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	} = FogInfluence.Enabled;
	[Property, Range( 0, 1 ), Category( "Fog Settings" )]
	public float FogStrength
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	} = 1.0f;


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
		o.LightColor = LightColor;
		o.ShadowsEnabled = Shadows;

		o.FogLighting = (SceneLight.FogLightingMode)FogMode; // these should map directly
		o.FogStrength = FogStrength;
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
