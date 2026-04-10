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
			if ( field == value ) return;
			field = value;

			if ( _sceneObject.IsValid() )
				_sceneObject.LightColor = value;
		}
	} = "#E9FAFF";

	[Property, Category( "Fog Settings" )]
	public FogInfluence FogMode
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			if ( _sceneObject.IsValid() )
				_sceneObject.FogLighting = (SceneLight.FogLightingMode)value;
		}
	} = FogInfluence.Enabled;

	[Property, Range( 0, 1 ), Category( "Fog Settings" )]
	public float FogStrength
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			if ( _sceneObject.IsValid() )
				_sceneObject.FogStrength = value;
		}
	} = 1.0f;

	/// <summary>
	/// Should this light cast shadows?
	/// </summary>
	[Property, Category( "Shadows" ), Order( -10 )]
	public bool Shadows
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			if ( _sceneObject.IsValid() )
				_sceneObject.ShadowsEnabled = value;
		}
	} = true;

	[Property, Range( 0, 1 ), Category( "Shadows" ), Advanced]
	public float ShadowBias
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			if ( _sceneObject.IsValid() )
				_sceneObject.ShadowBias = value;
		}
	} = 0.0005f;

	[Property, Range( 0, 1 ), Category( "Shadows" )]
	public float ShadowHardness
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			if ( _sceneObject.IsValid() )
				_sceneObject.ShadowHardness = value;
		}
	} = 0.0f;

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
			_sceneObject.Component = this;
			_sceneObject.LightColor = LightColor;
			_sceneObject.ShadowsEnabled = Shadows;
			_sceneObject.FogLighting = (SceneLight.FogLightingMode)FogMode;
			_sceneObject.FogStrength = FogStrength;
			_sceneObject.ShadowBias = ShadowBias;
			_sceneObject.ShadowHardness = ShadowHardness;

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
