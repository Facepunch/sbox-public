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
	Color _tint = Color.White;

	[Property]
	public Color Tint
	{
		get => _tint;
		set
		{
			if ( _tint == value ) return;

			_tint = value;

			if ( _sceneObject is not null )
			{
				_sceneObject.SkyTint = Tint;
			}

			if ( _envProbe is not null )
			{
				_envProbe.TintColor = Tint;
			}
		}
	}

	[Property, Description( "Whether to use the skybox for lighting as an envmap probe" ), MakeDirty, DefaultValue( true )]
	public bool SkyIndirectLighting { get; set; } = true;

	Material _material = Material.Load( "materials/skybox/skybox_day_01.vmat" ); // todo - better default

	[Property]
	public Material SkyMaterial
	{
		get => _material;
		set
		{
			if ( _material == value ) return;

			if ( value.native.IsNull ) return;

			// Only allow sky materials
			if ( !value.ShaderName.Contains( "sky" ) ) return;

			_material = value;

			if ( _sceneObject is not null )
			{
				_sceneObject.SkyMaterial = _material;
			}

			if ( _envProbe is not null )
			{
				_envProbe.Texture = SkyTexture;
			}
		}
	}

	public Texture SkyTexture => _material.GetTexture( "g_tSkyTexture" );

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
		OnDirty();
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

		/*
		var startDisabled = kv.GetValue<bool>( "StartDisabled" );
		var fogType = kv.GetValue<SceneSkyBox.FogType>( "fog_type" );
		var fogMinStart = kv.GetValue<float>( "angular_fog_min_start" );
		var fogMinEnd = kv.GetValue<float>( "angular_fog_min_end" );
		var fogMaxStart = kv.GetValue<float>( "angular_fog_max_start" );
		var fogMaxEnd = kv.GetValue<float>( "angular_fog_max_end" );

		var fogParams = new SceneSkyBox.FogParamInfo
		{
			FogType = fogType,
			FogMinStart = fogMinStart,
			FogMinEnd = fogMinEnd,
			FogMaxStart = fogMaxStart,
			FogMaxEnd = fogMaxEnd,
		};
		*/

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

	protected override void OnDirty()
	{
		base.OnDirty();

		if ( !Active ) return;

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
