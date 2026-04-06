namespace Sandbox;

/// <summary>
/// Adds a 2D skybox to the world
/// </summary>
[Title( "2D Skybox" )]
[Category( "Rendering" )]
[Icon( "visibility" )]
[EditorHandle( "materials/gizmo/2dskybox.png" )]
public class SkyBox2D : Component, Component.ExecuteInEditor
{
	SceneCubemap _envProbe;

	[Property]
	public Color Tint
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			_sceneObject?.SkyTint = value;
			_envProbe?.TintColor = value;
		}
	} = Color.White;

	[Property, Description( "Whether to use the skybox for lighting as an envmap probe" ), DefaultValue( true )]
	public bool SkyIndirectLighting
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			if ( Active )
				UpdateEnvProbe();
		}
	} = true;

	[Property]
	public Material SkyMaterial
	{
		get;
		set
		{
			if ( field == value ) return;
			if ( value.native.IsNull ) return;

			// Only allow sky materials
			if ( !value.ShaderName.Contains( "sky" ) ) return;

			field = value;

			_sceneObject?.SkyMaterial = value;
			_envProbe?.Texture = SkyTexture;
		}
	} = Material.Load( "materials/skybox/skybox_day_01.vmat" );

	public Texture SkyTexture => SkyMaterial.GetTexture( "g_tSkyTexture" );

	SceneSkyBox _sceneObject;

	protected override void OnAwake()
	{
		Tags.Add( "skybox" );

		base.OnAwake();
	}

	protected override void OnEnabled()
	{
		if ( SkyMaterial is null ) return;

		Assert.True( !_sceneObject.IsValid() );
		Assert.NotNull( Scene );

		_sceneObject = new SceneSkyBox( Scene.SceneWorld, SkyMaterial );
		_sceneObject.SkyTint = Tint;
		_sceneObject.Tags.SetFrom( Tags );

		OnTransformChanged();
		Transform.OnTransformChanged += OnTransformChanged;

		UpdateEnvProbe();
	}

	protected override void OnDisabled()
	{
		Transform.OnTransformChanged -= OnTransformChanged;

		_sceneObject?.Delete();
		_sceneObject = null;

		_envProbe?.Delete();
		_envProbe = null;
	}

	private void OnTransformChanged()
	{
		if ( _sceneObject.IsValid() )
			_sceneObject.Transform = WorldTransform.WithScale( 1.0f );

		if ( _envProbe.IsValid() )
			_envProbe.Transform = WorldTransform.WithScale( 1.0f );
	}

	internal static void InitializeFromLegacy( GameObject go, Sandbox.MapLoader.ObjectEntry kv )
	{
		var component = go.Components.Create<SkyBox2D>();

		var skyMaterial = kv.GetResource<Material>( "skyname" );
		var tintColor = kv.GetValue<Color>( "tint_color" );
		var usesIbl = kv.GetValue<bool>( "ibl", true );

		if ( skyMaterial is null )
		{
			Log.Warning( $"Failed to load skybox material \"{kv.GetValue<string>( "skyname" )}\"" );
			return;
		}

		component.Tint = tintColor;
		component.SkyMaterial = skyMaterial;
		component.SkyIndirectLighting = usesIbl;
	}

	/// <summary>
	/// Tags have been updated
	/// </summary>
	protected override void OnTagsChanged()
	{
		_sceneObject?.Tags.SetFrom( Tags );

		if ( _envProbe.IsValid() )
		{
			_envProbe.Tags.SetFrom( Tags );
			_envProbe.RenderDirty();
		}
	}

	void UpdateEnvProbe()
	{
		_envProbe?.Delete();
		_envProbe = null;

		// Set up our global env probe
		// -5 means it's of lowest priority in ordering
		if ( SkyIndirectLighting )
		{
			_envProbe = new SceneCubemap( Scene.SceneWorld, SkyTexture, BBox.FromPositionAndSize( Vector3.Zero, int.MaxValue ), WorldTransform.WithScale( 1 ), Tint, 0.01f );
			_envProbe.Tags.SetFrom( Tags );
			_envProbe.Priority = -5;
		}
	}
}
