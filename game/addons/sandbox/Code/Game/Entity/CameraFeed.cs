/// <summary>
/// Shared video output for physical cameras and camera weapons. Owns the render target
/// and render demand; producers only supply the lens view and TVs consume this component.
/// </summary>
[Title( "#entity.name.camera_feed" ), Icon( "videocam" ), SignalOutput( Default = true )]
public sealed class CameraFeed : Component, Global.ISaveEvents
{
	[Property] public CameraComponent Lens { get; set; }
	Texture _renderTexture;
	RealTimeSince _lastRequested = 999f;
	Transform _localView;
	bool _overridingView;

	public CameraComponent Camera
	{
		get
		{
			if ( !Lens.IsValid() ) Lens = GetComponentInChildren<CameraComponent>( true );
			return Lens;
		}
	}

	public Transform ViewTransform => Camera.IsValid() ? Camera.WorldTransform : WorldTransform;
	/// <summary>The authored lens mount, unaffected by temporary held/tracking view overrides.</summary>
	public Transform PhysicalViewTransform => _overridingView && Camera.IsValid()
		? (Camera.GameObject.Parent?.WorldTransform ?? global::Transform.Zero).ToWorld( _localView )
		: ViewTransform;
	public Texture RenderTexture => Active ? _renderTexture : null;

	/// <summary>A visible TV requests frames. Multiple TVs can share a feed without disabling each other.</summary>
	public Texture RequestFrame()
	{
		if ( !Active || !Camera.IsValid() ) return null;
		if ( !_renderTexture.IsValid() )
			_renderTexture = Texture.CreateRenderTarget().WithSize( 512, 512 ).Create();
		_lastRequested = 0;
		Camera.RenderTarget = _renderTexture;
		Camera.Enabled = true;
		return _renderTexture;
	}

	protected override void OnEnabled()
	{
		if ( !Camera.IsValid() ) return;
		Camera.IsMainCamera = false;
		Camera.BackgroundColor = Color.Black;
		Camera.ClearFlags = ClearFlags.Color | ClearFlags.Depth | ClearFlags.Stencil;
		Camera.RenderExcludeTags.Add( "viewmodel" );
		Camera.Enabled = false;
	}

	protected override void OnUpdate()
	{
		if ( Camera.IsValid() ) Camera.Enabled = _renderTexture.IsValid() && _lastRequested < 0.25f;
	}

	protected override void OnPreRender()
	{
		if ( Camera.IsValid() && _renderTexture.IsValid() ) Camera.RenderTarget = _renderTexture;
	}

	/// <summary>Use the lens's physical transform, restoring it after a held-camera override.</summary>
	public void UseLensView( float fov )
	{
		if ( !Camera.IsValid() ) return;
		RestoreLens();
		Camera.FieldOfView = fov;
		Camera.FovAxis = CameraComponent.Axis.Horizontal;
		Camera.RenderExcludeTags.Remove( "viewer" );
	}

	public void SetWorldView( Transform view, float fov, CameraComponent.Axis axis, bool hideViewer )
	{
		if ( !Camera.IsValid() ) return;
		if ( !_overridingView ) _localView = Camera.LocalTransform;
		_overridingView = true;
		Camera.WorldPosition = view.Position;
		Camera.WorldRotation = view.Rotation;
		Camera.FieldOfView = fov;
		Camera.FovAxis = axis;
		Camera.RenderExcludeTags.Set( "viewer", hideViewer );
	}

	void RestoreLens()
	{
		if ( _overridingView && Camera.IsValid() ) Camera.LocalTransform = _localView;
		_overridingView = false;
	}

	void Cleanup()
	{
		RestoreLens();
		if ( Camera.IsValid() ) { Camera.Enabled = false; Camera.RenderTarget = null; }
		_renderTexture?.Dispose();
		_renderTexture = null;
		_lastRequested = 999f;
	}

	protected override void OnDisabled() => Cleanup();
	protected override void OnDestroy() => Cleanup();

	void Global.ISaveEvents.BeforeSave( string filename )
	{
		// Save the authored lens offset, not its temporary held-player position.
		if ( _overridingView && Camera.IsValid() ) Camera.LocalTransform = _localView;
	}
}
