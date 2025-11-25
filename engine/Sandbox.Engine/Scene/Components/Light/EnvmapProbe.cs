using static Sandbox.SceneCubemap;

namespace Sandbox;

/// <summary>
/// A cubemap probe that captures the environment around it.
/// </summary>
[Expose]
[Title( "Envmap Probe" )]
[Category( "Light" )]
[Icon( "radio_button_unchecked" )]
[EditorHandle( "materials/gizmo/envmap.png" )]
[Alias( "EnvmapComponent" )]
public sealed class EnvmapProbe : Component, Component.ExecuteInEditor
{
	SceneCubemap _sceneObject;

	[Property, MakeDirty] public SceneCubemap.ProjectionMode Projection { get; set; }
	[Property, MakeDirty] public Color TintColor { get; set; } = Color.White;

	[Property, MakeDirty] public BBox Bounds { get; set; } = BBox.FromPositionAndSize( 0, 1024 );

	[Property, Range( -32.0f, 32.0f ), MakeDirty] public float Feathering { get; set; } = 8.0f;

	internal int ArrayIndex;
	internal int Priority;

	/// <summary>
	/// If this is set, the EnvmapProbe will use a custom cubemap texture instead of render dynamically
	/// </summary>
	[Property, MakeDirty]
	[ShowIf( nameof( RenderDynamically ), false )]
	public Texture Texture { get; set; }

	/// <summary>
	/// Enable time slicing to spread cubemap rendering across multiple frames
	/// </summary>
	[Property, ToggleGroup(nameof(RenderDynamically))]
	public bool TimeSlicing { get; set; } = false;

	/// <summary>
	/// Current face being rendered when time slicing is enabled (0-5)
	/// </summary>
	private int _currentFace = 0;

	/// <summary>
	/// Track if we're in the middle of a time-sliced render
	/// </summary>
	private bool _isTimeSlicing = false;
	
	Texture _dynamicTexture;

	public bool Dirty;

	int BouncesLeft;

	/// <summary>
	/// Cubemaps in Source 2 have an inverted Y axis, for rendering them dynamically it uses correct axis
	/// We used to invert-Y but since we are rendering directly to cubemaps (and can't manipulate Y projection matrix
	/// without breaking culling ), we invert the matrix of the cubemap being drawn
	/// </summary>
	internal bool NeedsInvertedAxis => RenderDynamically;

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected )
			return;

		Gizmo.Draw.Color = TintColor;
		Gizmo.Draw.LineBBox( Bounds );

		Gizmo.Draw.Color = TintColor.WithAlpha( 0.1f );

		Gizmo.Draw.LineBBox( Bounds.Grow( Feathering ) );
	}

	protected override void OnEnabled()
	{
		Assert.True( !_sceneObject.IsValid() );
		Assert.NotNull( Scene );

		_sceneObject = new SceneCubemap( Scene.SceneWorld, null, Bounds, WorldTransform, TintColor, Priority, Feathering, (int)Projection );
		_sceneObject.Tags.SetFrom( Tags );

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
		
		_isTimeSlicing = false;
		_currentFace = 0;
	}

	protected override async Task OnLoad()
	{
		if ( Application.IsHeadless )
			return;

		if ( RenderDynamically && Active )
		{
			Dirty = true;
		}

		while ( Dirty )
		{
			LoadingScreen.Title = "Generating Envmaps";
			LoadingScreen.Subtitle = "Creating reflection maps";
			await Task.DelayRealtime( 10 );
		}
	}

	protected override void OnDirty()
	{
		if ( _sceneObject.IsValid() )
		{
			UpdateSceneObject();
		}
	}

	void OnTransformChanged()
	{
		if ( !_sceneObject.IsValid() )
			return;

		UpdateSceneObject();
	}

	void UpdateSceneObject()
	{
		if ( !_sceneObject.IsValid() )
			return;

		var tx = WorldTransform;
		var bounds = Bounds;

		if ( NeedsInvertedAxis )
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
			BouncesLeft = MultiBounce ? 4 : 0;
		}

		// Update texture based on current settings
		if ( RenderDynamically )
		{
			CreateTexture();
			_sceneObject.Texture = _dynamicTexture;
		}
		else
		{
			// When switching to static texture, dispose dynamic texture
			if ( _dynamicTexture != null )
			{
				_dynamicTexture.Dispose();
				_dynamicTexture = null;
			}
			_sceneObject.Texture = Texture;
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
		component.RenderDynamically = true;

	}

	[Property, ToggleGroup( nameof( RenderDynamically ), Label = "Render Dynamically" ), MakeDirty]
	public bool RenderDynamically { get; set; }

	/// <summary>
	/// Resolution of the cubemap texture
	/// </summary>
	[Property, ToggleGroup( nameof( RenderDynamically ) ), MakeDirty]
	public CubemapResolution Resolution { get; set; } = CubemapResolution.Small;

	/// <summary>
	/// Only update dynamically if we're this close to it
	/// </summary>
	[Property, ToggleGroup( nameof( RenderDynamically ) )]
	public float MaxDistance { get; set; } = 512;

	[Property, ToggleGroup( nameof( RenderDynamically ) )]
	public float ZNear { get; set; } = 16;

	[Property, ToggleGroup( nameof( RenderDynamically ) )]
	public float ZFar { get; set; } = 4096;

	[Property, ToggleGroup( nameof( RenderDynamically ) ), MakeDirty]
	public CubemapDynamicUpdate UpdateStrategy { get; set; }

	[Property, ToggleGroup( nameof( RenderDynamically ) ), ShowIf( "UpdateStrategy", CubemapDynamicUpdate.TimeInterval ), Range( 0, 10 )]
	public float DelayBetweenUpdates { get; set; } = 0.1f;

	[Property, ToggleGroup( nameof( RenderDynamically ) ), ShowIf( "UpdateStrategy", CubemapDynamicUpdate.FrameInterval ), Range( 0, 16 )]
	public int FrameInterval { get; set; } = 5;

	/// <summary>
	/// Minimum amount of reflection bounces to render when first enabled before settling, at cost of extra performance on load
	/// Often times you don't need this
	/// </summary>
	[Property, ToggleGroup( nameof( RenderDynamically ) ), ShowIf( "UpdateStrategy", CubemapDynamicUpdate.OnEnabled ), MakeDirty]
	public bool MultiBounce { get; set; } = false;

	protected override void OnUpdate()
	{
		base.OnUpdate();
		TryToDirty();
	}

	void TryToDirty()
	{
		if ( !RenderDynamically )
		{
			// Reset counters when not rendering dynamically
			QueuedFrames = 0;
			QueuedTime = 0;
			return;
		}

		// Update counters
		QueuedFrames++;
		QueuedTime += Time.Delta;

		if ( !IsReadyToUpdate() )
			return;

		Dirty = true;
	}

	int QueuedFrames = 0;
	float QueuedTime = 0;

	internal bool IsReadyToUpdate()
	{
		// If we're in the middle of time slicing, always continue
		if (_isTimeSlicing)
			return true;
		
		// If it's dirty, always update even if we're render once
		if ( _sceneObject?.RequiresUpdate ?? false )
			return true;

		if ( UpdateStrategy == CubemapDynamicUpdate.EveryFrame )
			return true;

		if ( UpdateStrategy == CubemapDynamicUpdate.FrameInterval && QueuedFrames > FrameInterval )
			return true;

		if ( UpdateStrategy == CubemapDynamicUpdate.TimeInterval && QueuedTime > DelayBetweenUpdates )
			return true;

		if ( UpdateStrategy == CubemapDynamicUpdate.OnEnabled && BouncesLeft > 0 )
			return true;

		return false;
	}

	void CreateTexture()
	{
		if ( !RenderDynamically )
			return;

		var CubemapSize = (int)Resolution;
		var numMips = 7; // Cubemapper is calibrated for 7 mipmaps

		// Only create if we don't have a texture or the resolution changed
		if ( _dynamicTexture is null || _dynamicTexture.Width != CubemapSize )
		{
			// Dispose old texture if it exists
			_dynamicTexture?.Dispose();

			_dynamicTexture = Texture.CreateCube( CubemapSize, CubemapSize )
								.WithUAVBinding()
								.WithMips( numMips )
								.WithFormat( ImageFormat.RGBA16161616F )
								.Finish();
		}
	}

	int _renderCount;

	internal void RenderCubemap()
	{
		if ( _dynamicTexture is null )
			return;

		_renderCount++;

		CubemapRendering.GGXFilterType filterType;
		if ( UpdateStrategy == CubemapDynamicUpdate.OnEnabled )
		{
			filterType = CubemapRendering.GGXFilterType.Quality;
		}
		else
		{
			filterType = CubemapRendering.GGXFilterType.Fast;
		}

		if ( TimeSlicing )
		{
			// Render one face at a time
			if ( !_isTimeSlicing )
			{
				// Start new time-sliced render
				_currentFace = 0;
				_isTimeSlicing = true;
			}

			// Render current face
			CubemapRendering.RenderSingleFace(
				Scene.SceneWorld, 
				_dynamicTexture, 
				WorldTransform.WithScale(1), 
				ZNear.Clamp(1, ZFar), 
				ZFar.Clamp(ZNear, 1024 * 16), 
				_currentFace,
				filterType
			);
			
			_currentFace++;

			// Check if we've rendered all 6 faces
			if (_currentFace >= 6)
			{
				// All faces rendered, finalize
				_isTimeSlicing = false;
				_sceneObject.RequiresUpdate = false;
				QueuedFrames = 0;
				QueuedTime = 0;

				if ( BouncesLeft > 0 && UpdateStrategy == CubemapDynamicUpdate.OnEnabled )
					BouncesLeft--;

				Dirty = false;
			}
			else
			{
				Dirty = true;
			}
		}
		else
		{
			// Original behavior (Render all faces at once)
			CubemapRendering.Render(
				Scene.SceneWorld, 
				_dynamicTexture, 
				WorldTransform.WithScale(1), 
				ZNear.Clamp(1, ZFar), 
				ZFar.Clamp(ZNear, 1024 * 16), 
				filterType
			);

			_sceneObject.RequiresUpdate = false;
			QueuedFrames = 0;
			QueuedTime = 0;

			if ( BouncesLeft > 0 && UpdateStrategy == CubemapDynamicUpdate.OnEnabled )
				BouncesLeft--;

			Dirty = false;
		}
	}

	public enum CubemapResolution
	{
		[Title( "Small (128²)" )]
		Small = 128,

		[Title( "Medium (256²)" )]
		Medium = 256,

		[Title( "Large (512²)" )]
		Large = 512
	}

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
}
