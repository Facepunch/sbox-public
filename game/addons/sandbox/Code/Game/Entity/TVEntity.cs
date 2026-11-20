/// <summary>
/// A TV screen entity that displays a linked CameraFeed, regardless of what produces it.
/// Use the Wire tool to wire a Camera's video feed to this TV's display input.
/// </summary>
[Title( "#entity.name.television" )]
public sealed class TVEntity : Component
{
	[Property]
	public string ScreenMaterialName { get; set; } = "screen";

	/// <summary>
	/// The camera whose view is on the screen. A component input: only a Camera
	/// can be linked here, and linking just sets this reference.
	/// </summary>
	[Property, Hide, Icon( "connected_tv" ), Title( "#entity.property.display" ), SignalInput( Default = true )]
	public CameraFeed Display { get; set; }

	/// <summary>
	/// The brightness of the television screen.
	/// </summary>
	[Property, Title( "#entity.property.tv_brightness" ), Description( "#entity.description.tv_brightness" ), Range( 0.5f, 10 ), Step( 0.5f ), ClientEditable, Group( "#entity.group.screen" )]
	public float Brightness { get; set; } = 1f;

	/// <summary>
	/// Whether the television screen is switched on.
	/// </summary>
	[Property, Title( "#entity.property.tv_on" ), Description( "#entity.description.tv_on" ), ClientEditable, Group( "#entity.group.screen" )]
	public bool On { get; set; } = true;

	public float MaxRenderDistance { get; set; } = 1024f;

	/// <summary>
	/// True when a linked camera is actively providing a render texture.
	/// </summary>
	public bool HasLinkedCamera => Display.IsValid() && Display.Active && Display.RenderTexture is not null;

	private Texture _lastTexture;
	private Material _materialCopy;
	private ModelRenderer _renderer;
	private bool _hasSignal;
	private RealTimeSince _timeSinceSignalChange;

	private static readonly float TransitionDuration = 0.4f;
	private static readonly float FadeStartFraction = 0.75f;

	protected override void OnStart()
	{
		_renderer = GetComponentInChildren<ModelRenderer>( true );
		_renderer?.SceneObject.Batchable = false;
	}

	protected override void OnUpdate()
	{
		var feed = Display.IsValid() ? Display : null;

		// Distance-based fade and RT camera culling
		float distanceToCamera = Vector3.DistanceBetween( WorldPosition, Scene.Camera.View.Position );
		float fadeStart = MaxRenderDistance * FadeStartFraction;
		float distanceFade = 1.0f - MathX.Clamp( ( distanceToCamera - fadeStart ) / ( MaxRenderDistance - fadeStart ), 0f, 1f );
		bool tooFar = distanceFade <= 0f;

		// The feed owns rendering, so a distant TV cannot disable another TV's camera.
		var linkedTexture = On && !tooFar ? feed?.RequestFrame() : feed?.RenderTexture;

		var newSignal = On && linkedTexture is not null && !tooFar;

		if ( newSignal != _hasSignal )
		{
			_timeSinceSignalChange = 0;
			_hasSignal = newSignal;
		}

		// Keep the last known texture alive during the off-transition,
		// but only if the linked feed still has a valid render target.
		if ( linkedTexture is not null )
		{
			_lastTexture = linkedTexture;
		}
		else
		{
			// Feed gone or disabled — its texture was disposed, don't use the cached copy.
			_lastTexture = null;
		}

		EnsureMaterialSetup();

		if ( _materialCopy is null || _renderer is null ) return;

		var inTransition = _timeSinceSignalChange < TransitionDuration;
		var textureToUse = linkedTexture ?? ( inTransition ? _lastTexture : null );

		_renderer.Attributes.Set( "Color", textureToUse is not null ? textureToUse : Texture.Black );

		if ( !_hasSignal && !inTransition )
		{
			_lastTexture = null;
		}

		_renderer.Attributes.Set( "HasSignal", _hasSignal ? 1.0f : 0.0f );
		_renderer.Attributes.Set( "ScreenOn", On ? 1.0f : 0.0f );
		_renderer.Attributes.Set( "TimeSinceSignalChange", (float)_timeSinceSignalChange );
		_renderer.Attributes.Set( "DistanceFade", distanceFade );
		_renderer.Attributes.Set( "Brightness", Brightness );
	}

	protected override void OnDestroy()
	{
		_materialCopy = null;
		base.OnDestroy();
	}

	private static readonly string ShaderPath = "entities/sents/tv/materials/tv_crt_screen.shader";

	private void EnsureMaterialSetup()
	{
		if ( _materialCopy is not null && _renderer.IsValid() ) return;
		if ( _renderer is null ) return;

		var materials = _renderer.Model?.Materials;
		if ( materials is not { } mats ) return;

		for ( int i = 0; i < mats.Length; i++ )
		{
			if ( mats[i]?.Name?.Contains( ScreenMaterialName ) is true )
			{
				_materialCopy = Material.FromShader( ShaderPath );
				_renderer.Materials.SetOverride( i, _materialCopy );
				return;
			}
		}
	}
}
