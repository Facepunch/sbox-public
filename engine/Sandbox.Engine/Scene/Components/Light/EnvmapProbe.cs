using static Sandbox.SceneCubemap;

namespace Sandbox;

/// <summary>
/// A cubemap probe that captures the environment around it.
/// </summary>
[Expose]
[Title( "Envmap Probe" )]
[Category( "Light" )]
[Icon( "radio_button_unchecked" )]
[Alias( "EnvmapComponent" )]
public sealed partial class EnvmapProbe : Component, Component.ExecuteInEditor
{
	[Expose]
	public enum EnvmapProbeMode
	{
		[Icon( "🥖" )]
		Baked,

		[Icon( "📹" )]
		Realtime,

		[Icon( "📄" )]
		CustomTexture
	}

	[Expose]
	public enum CubemapResolution
	{
		[Title( "128x128" )]
		Small = 128,

		[Title( "256x256" )]
		Medium = 256,

		[Title( "512x512" )]
		Large = 512,

		[Title( "1024x1024" )]
		Huge = 1024
	}

	[Expose]
	public enum CubemapDynamicUpdate
	{
		/// <summary>
		/// Update once, when the cubemap is enabled
		/// </summary>
		OnEnabled,

		/// <summary>
		/// Update every frame (slow, not recommended)
		/// </summary>
		EveryFrame,

		/// <summary>
		/// Update every x frames
		/// </summary>
		FrameInterval,

		/// <summary>
		/// Update on a time based interval
		/// </summary>
		TimeInterval,
	}

	SceneCubemap _sceneObject;
	Texture _dynamicTexture;
	int _bouncesLeft;
	int _queuedFrames;
	float _queuedTime;

	public bool Dirty;

	[Property, EnumButtonGroup, WideMode( HasLabel = false )]
	public EnvmapProbeMode Mode
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;
			UpdateSceneObject();
		}
	}

	[Space]
	[Property, MakeDirty] public SceneCubemap.ProjectionMode Projection { get; set; }
	[Property, MakeDirty] public Color TintColor { get; set; } = Color.White;
	[Property, MakeDirty] public BBox Bounds { get; set; } = BBox.FromPositionAndSize( 0, 1024 );


	[Property, Range( -32.0f, 32.0f ), MakeDirty] public float Feathering { get; set; } = 8.0f;

	/// <summary>
	/// Gets or sets the priority level for the object.
	/// </summary>
	[Property, Range( 0, 100 )]
	public int Priority
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			if ( _sceneObject.IsValid() )
			{
				_sceneObject.Priority = field;
			}
		}
	} = 0;

	/// <summary>
	/// If this is set, the EnvmapProbe will use a custom cubemap texture instead of rendering dynamically
	/// </summary>
	[Property, MakeDirty]
	[ShowIf( nameof( Mode ), EnvmapProbeMode.CustomTexture )]
	public Texture Texture { get; set; }

	/// <summary>
	/// The texture that was baked for this envmap probe
	/// </summary>
	[Property, Hide]
	public Texture BakedTexture { get; set; }

	[Obsolete( "Use Mode to select the update mode" ), Hide]
	public bool RenderDynamically
	{
		get => Mode == EnvmapProbeMode.Realtime;
		set => Mode = value ? EnvmapProbeMode.Realtime : EnvmapProbeMode.CustomTexture;
	}

	/// <summary>
	/// Resolution of the cubemap texture
	/// </summary>
	[Header( "Rendering" )]
	[HideIf( nameof( Mode ), EnvmapProbeMode.CustomTexture )]
	[Property, MakeDirty]
	[EnumButtonGroup]
	public CubemapResolution Resolution { get; set; } = CubemapResolution.Small;

	[HideIf( nameof( Mode ), EnvmapProbeMode.CustomTexture )]
	[Property, MakeDirty]
	public float ZNear { get; set; } = 16;

	[HideIf( nameof( Mode ), EnvmapProbeMode.CustomTexture )]
	[Property, MakeDirty]
	public float ZFar { get; set; } = 4096;

	[Header( "Realtime Updates" )]
	[ShowIf( nameof( Mode ), EnvmapProbeMode.Realtime )]
	[Property, MakeDirty]
	public CubemapDynamicUpdate UpdateStrategy { get; set; }

	/// <summary>
	/// Only update dynamically if we're this close to it
	/// </summary>
	[ShowIf( nameof( Mode ), EnvmapProbeMode.Realtime )]
	[Property]
	public float MaxDistance { get; set; } = 512;

	[ShowIf( nameof( Mode ), EnvmapProbeMode.Realtime )]
	[ShowIf( nameof( UpdateStrategy ), CubemapDynamicUpdate.TimeInterval )]
	[Property, Range( 0, 10 )]
	public float DelayBetweenUpdates { get; set; } = 0.1f;

	[ShowIf( nameof( Mode ), EnvmapProbeMode.Realtime )]
	[ShowIf( nameof( UpdateStrategy ), CubemapDynamicUpdate.FrameInterval )]
	[Property, Range( 0, 16 )]
	public int FrameInterval { get; set; } = 5;

	/// <summary>
	/// Minimum amount of reflection bounces to render when first enabled before settling, at cost of extra performance on load
	/// Often times you don't need this
	/// </summary>
	[HideIf( nameof( Mode ), EnvmapProbeMode.CustomTexture )]
	[ShowIf( nameof( UpdateStrategy ), CubemapDynamicUpdate.OnEnabled )]
	[Property, MakeDirty]
	public bool MultiBounce { get; set; } = false;

	protected override void OnEnabled()
	{
		Assert.True( !_sceneObject.IsValid() );
		Assert.NotNull( Scene );

		_sceneObject = new SceneCubemap( Scene.SceneWorld, null, Bounds, WorldTransform, TintColor, Feathering, (int)Projection );
		_sceneObject.Tags.SetFrom( Tags );
		_sceneObject.Priority = Priority;

		Transform.OnTransformChanged += OnTransformChanged;
		UpdateSceneObject();
	}

	protected override void OnDisabled()
	{
		Transform.OnTransformChanged -= OnTransformChanged;

		_sceneObject?.Delete();
		_sceneObject = null;

		_dynamicTexture?.Dispose();
		_dynamicTexture = null;
	}

	protected override async Task OnLoad( LoadingContext context )
	{
		if ( Application.IsHeadless )
			return;

		if ( Mode == EnvmapProbeMode.Realtime && Active && _dynamicTexture is null )
		{
			Dirty = true;
		}

		if ( Dirty )
		{
			context.Title = "Rendering Envmap";

			while ( Dirty )
			{
				await Task.DelayRealtime( 10 );
			}
		}
	}

	protected override void OnDirty()
	{
		UpdateSceneObject();
	}

	void OnTransformChanged()
	{
		UpdateSceneObject();
	}

	void UpdateSceneObject()
	{
		if ( !_sceneObject.IsValid() )
			return;

		var tx = WorldTransform;
		var bounds = Bounds;

		if ( Mode == EnvmapProbeMode.Realtime )
		{
			tx = tx.WithScale( -1 );
			bounds = new BBox( -Bounds.Maxs, -Bounds.Mins );
		}

		_sceneObject.Transform = tx;
		_sceneObject.Projection = Projection;
		_sceneObject.TintColor = TintColor;
		_sceneObject.ProjectionBounds = bounds;
		_sceneObject.LocalBounds = _sceneObject.ProjectionBounds;
		_sceneObject.Radius = Bounds.Size.Length;
		_sceneObject.Feathering = Feathering;

		// Update bounce count when strategy or multibounce changes
		if ( UpdateStrategy == CubemapDynamicUpdate.OnEnabled )
		{
			_bouncesLeft = MultiBounce ? 4 : 0;
		}

		if ( Mode == EnvmapProbeMode.Baked )
		{
			_sceneObject.Texture = BakedTexture;
		}
		else if ( Mode == EnvmapProbeMode.CustomTexture )
		{
			_sceneObject.Texture = Texture;
		}
		else if ( Mode == EnvmapProbeMode.Realtime )
		{
			CreateTexture();
			_sceneObject.Texture = _dynamicTexture;
		}
	}

	/// <summary>
	/// Tags have been updated - lets update our tags
	/// </summary>
	protected override void OnTagsChanged()
	{
		if ( _sceneObject.IsValid() )
		{
			_sceneObject.Tags.SetFrom( Tags );
			_sceneObject.RenderDirty();
		}
	}

	internal static void InitializeFromLegacy( GameObject go, Sandbox.MapLoader.ObjectEntry kv )
	{
		var component = go.Components.Create<EnvmapProbe>();

		var boundsMin = kv.GetValue( "box_mins", new Vector3( -72.0f, -72.0f, -72.0f ) );
		var boundsMax = kv.GetValue( "box_maxs", new Vector3( 72.0f, 72.0f, 72.0f ) );
		var indoorOutdoorLevel = kv.GetValue<int>( "indoor_outdoor_level" );
		var feathering = kv.GetValue( "cubemap_feathering", 0.25f );

		component.Bounds = new BBox( boundsMin, boundsMax );
		component.Feathering = feathering * 8.0f;
		if ( kv.TypeName == "env_combined_light_probe_volume" || kv.TypeName == "env_cubemap_box" )
		{
			component.Projection = ProjectionMode.Box;
		}
		else
		{
			component.Projection = ProjectionMode.Sphere;
		}

		//
		// Because we don't render cubemaps in map compiled anymore, the imported texture is likely BLACK.
		// So instead we switch this up to create the texture dynamically, once, on startup
		//

		component.UpdateStrategy = CubemapDynamicUpdate.OnEnabled;
		component.Texture = default;
		component.Mode = EnvmapProbeMode.Realtime;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		TryToDirty();
	}

	void TryToDirty()
	{
		if ( Mode != EnvmapProbeMode.Realtime )
		{
			// Reset counters when not rendering dynamically
			_queuedFrames = 0;
			_queuedTime = 0;
			return;
		}

		// Update counters
		_queuedFrames++;
		_queuedTime += Time.Delta;

		if ( !IsReadyToUpdate() )
			return;

		Dirty = true;
	}

	internal bool IsReadyToUpdate()
	{
		if ( UpdateStrategy == CubemapDynamicUpdate.EveryFrame )
			return true;

		if ( UpdateStrategy == CubemapDynamicUpdate.FrameInterval && _queuedFrames > FrameInterval )
			return true;

		if ( UpdateStrategy == CubemapDynamicUpdate.TimeInterval && _queuedTime > DelayBetweenUpdates )
			return true;

		// If it's dirty, always update even if we're render once
		if ( _sceneObject?.RequiresUpdate ?? false )
			return true;

		if ( UpdateStrategy == CubemapDynamicUpdate.OnEnabled && _bouncesLeft > 0 )
			return true;

		return false;
	}

	void CreateTexture()
	{
		var cubemapSize = (int)Resolution;
		var numMips = 7; // Cubemapper is calibrated for 7 mipmaps

		if ( _dynamicTexture is not null && _dynamicTexture.Width == cubemapSize && _dynamicTexture.UAVAccess )
			return;

		// Dispose old texture if it exists
		_dynamicTexture?.Dispose();

		_dynamicTexture = Texture.CreateCube( cubemapSize, cubemapSize )
							.WithUAVBinding()
							.WithMips( numMips )
							.WithFormat( ImageFormat.RGBA16161616F )
							.Finish();
	}

	internal void RenderCubemap()
	{
		RenderCubemap( _dynamicTexture, CubemapRendering.GGXFilterType.Fast );
	}

	internal void RenderCubemap( Texture target, CubemapRendering.GGXFilterType filterType )
	{
		if ( target is null )
			return;

		target.Clear( Color.Red );

		if ( target.UAVAccess )
		{
			CubemapRendering.Render( Scene.SceneWorld, target, WorldTransform.WithScale( 1 ), ZNear.Clamp( 1, ZFar ), ZFar.Clamp( ZNear, 1024 * 16 ), filterType );
		}

		// Just finished rendering, signal to component that we're done
		_sceneObject?.RequiresUpdate = false;

		// Reset counters after rendering
		_queuedFrames = 0;
		_queuedTime = 0;

		if ( _bouncesLeft > 0 && UpdateStrategy == CubemapDynamicUpdate.OnEnabled )
			_bouncesLeft--;

		Dirty = false;
	}
}
