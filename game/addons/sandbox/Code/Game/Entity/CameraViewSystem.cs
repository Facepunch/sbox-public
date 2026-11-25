/// <summary>
/// Local viewing state. A camera never switches another player's main view.
/// A loose camera answers only its owner's binding, from anywhere. A camera linked to a
/// contraption with a seat answers whoever is sitting in one of its seats, and nobody else.
/// </summary>
[Hide]
public sealed class CameraViewSystem : Component, Global.ISaveEvents
{
	const string BodyTag = "remote_camera_body";
	readonly Dictionary<string, CameraEntity> _targets = new( StringComparer.OrdinalIgnoreCase );
	readonly HashSet<string> _held = new( StringComparer.OrdinalIgnoreCase );
	// Rebuilt at the start of each input pass; the view pass later in the frame reads it.
	readonly HashSet<CameraEntity> _viewable = new();
	readonly Dictionary<GameObject, bool?> _seatAccess = new();
	readonly LinkedGameObjectBuilder _linked = new();
	CameraComponent _mainCamera;
	bool _hadViewmodel, _hadFirstperson, _hadViewer;
	CameraComponent.Axis _originalAxis;
	IDisposable _inputHook;
	public CameraEntity ActiveCamera { get; private set; }

	public static CameraViewSystem Get( Scene scene ) => scene.IsValid() ? scene.Get<CameraViewSystem>() : null;

	public static CameraViewSystem GetOrCreate( Scene scene )
	{
		if ( Get( scene ) is { } existing ) return existing;
		// A local component can also be installed in a game already running during hotload.
		using ( scene.Push() )
		{
			var go = new GameObject( true, "Camera view controller" );
			go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			return go.AddComponent<CameraViewSystem>();
		}
	}

	bool CanView( CameraEntity camera ) => camera.IsValid() && camera.Active && camera.Scene == Scene && _viewable.Contains( camera );

	BaseChair _refreshedSeat;
	RealTimeSince _sinceRefresh;
	HashSet<string> _actionNames;

	void RefreshViewable( Player player )
	{
		// Resolving seat access walks each camera's whole contraption, so not every frame unless the seat changed
		var localSeat = player.GetComponentInParent<BaseChair>( false );
		if ( localSeat == _refreshedSeat && _sinceRefresh < 0.25f ) return;
		_refreshedSeat = localSeat;
		_sinceRefresh = 0;
		_actionNames = null;

		_viewable.Clear();
		_seatAccess.Clear();
		foreach ( var camera in Scene.GetAllComponents<CameraEntity>() )
		{
			if ( !camera.IsValid() || !camera.Active ) continue;
			var root = camera.GameObject.Root;
			if ( !_seatAccess.TryGetValue( root, out var seatAccess ) )
			{
				seatAccess = ResolveSeatAccess( camera.GameObject, localSeat );
				foreach ( var linked in _linked.Objects ) _seatAccess[linked] = seatAccess;
				_seatAccess[root] = seatAccess;
				_linked.Clear();
			}
			// No seat on the contraption: the owner's global bind. Otherwise: only from its seats.
			if ( seatAccess ?? camera.IsLocallyOwned ) _viewable.Add( camera );
		}
	}

	/// <summary>
	/// Returns null for a loose camera, or whether the local player occupies a linked seat.
	/// </summary>
	bool? ResolveSeatAccess( GameObject source, BaseChair localSeat )
	{
		_linked.AddConnected( source );
		bool? hasAccess = null;
		foreach ( var obj in _linked.Objects )
		{
			foreach ( var chair in obj.GetComponentsInChildren<BaseChair>() )
			{
				if ( chair == localSeat ) return true;
				hasAccess = false;
			}
		}

		return hasAccess;
	}

	protected override void OnEnabled() => RegisterInputHook();

	protected override void OnUpdate()
	{
		// Install the hook for controllers that already existed when this code hotloaded.
		if ( _inputHook is null ) RegisterInputHook();
	}

	void RegisterInputHook()
	{
		_inputHook?.Dispose();
		_inputHook = Scene.AddHook( GameObjectSystem.Stage.StartUpdate, -100, UpdateInput, nameof( CameraViewSystem ), "Camera view bindings" );
	}

	void UpdateInput()
	{
		if ( Scene != Game.ActiveScene ) return;
		var player = Player.FindLocalPlayer();
		// Leave before freecam applies its own render filters later in this frame.
		if ( !player.IsValid() || player.Health <= 0 || Input.Keyboard.Pressed( "J" ) || Scene.Get<FreeCamGameObjectSystem>().IsActive )
		{
			_viewable.Clear();
			_sinceRefresh = float.MaxValue;
			Clear();
			_held.Clear();
			return;
		}

		// Leaving the seat drops a contraption camera here, ending the view below.
		RefreshViewable( player );
		if ( ActiveCamera is not null && (!CanView( ActiveCamera ) || !IsBound( ActiveCamera.ViewInput )) )
			Clear();

		// Nothing to bind and nothing held, which is most frames.
		if ( _viewable.Count == 0 && _held.Count == 0 ) return;

		// Keep bindings in discovery order when several actions are pressed in the same frame.
		foreach ( var binding in _targets.Keys )
			_targets[binding] = null;

		foreach ( var camera in _viewable )
		{
			if ( !CanView( camera ) || !IsBound( camera.ViewInput ) ) continue;
			var binding = camera.ViewInput.BindingId;

			if ( !_targets.TryGetValue( binding, out var latest ) || latest is null || camera.ViewOrder >= latest.ViewOrder )
				_targets[binding] = camera;
		}

		foreach ( var (binding, latest) in _targets )
		{
			if ( latest is null )
			{
				_held.Remove( binding );
				continue;
			}

			// Give the active camera its own binding so the player can always leave its view.
			var target = string.Equals( ActiveCamera?.ViewInput.BindingId, binding, StringComparison.OrdinalIgnoreCase ) ? ActiveCamera : latest;
			bool down = target.ViewInput.LocalDown();
			bool newlyHeld = down && _held.Add( binding );
			if ( !down ) _held.Remove( binding );
			ProcessInput( target, down, target.ToggleMode ? target.ViewInput.LocalPressed() : newlyHeld );
			// A bound camera owns Change View; don't also flip first/third person,
			// especially when releasing a hold-to-view camera.
			if ( !target.ViewInput.HasKey && string.Equals( binding, player.Controller.ToggleCameraModeButton, StringComparison.OrdinalIgnoreCase ) )
				Input.Clear( binding );
		}
	}

	bool IsBound( ClientInput input ) => input.HasKey || (input.IsEnabled && (_actionNames ??= Input.ActionNames.ToHashSet( StringComparer.OrdinalIgnoreCase )).Contains( input.Action ));

	void StopInputHook()
	{
		_inputHook?.Dispose();
		_inputHook = null;
		Clear();
	}

	protected override void OnDisabled() => StopInputHook();
	protected override void OnDestroy() => StopInputHook();

	// The next camera pass reapplies the active view. Persist only the normal camera's
	// filters, otherwise a save made while watching could restore hidden viewmodels.
	void Global.ISaveEvents.BeforeSave( string filename ) => RestoreCameraTags();
	void Global.ISaveEvents.BeforeLoad( string filename ) => Clear();

	internal void ProcessInput( CameraEntity camera, bool down, bool pressed )
	{
		if ( !CanView( camera ) ) return;
		if ( camera.ToggleMode )
		{
			if ( pressed )
			{
				if ( ActiveCamera == camera ) Clear();
				else SetActive( camera );
			}
		}
		else if ( pressed && down ) SetActive( camera );
		else if ( !down && ActiveCamera == camera ) Clear();
	}

	public void SetActive( CameraEntity camera )
	{
		if ( !CanView( camera ) || camera == ActiveCamera ) return;
		Clear();
		ActiveCamera = camera;
	}

	public void Release( CameraEntity camera )
	{
		if ( ActiveCamera == camera ) Clear();
	}

	public void Clear()
	{
		if ( ActiveCamera.IsValid() )
		{
			foreach ( var model in ActiveCamera.GetComponentsInChildren<ModelRenderer>( true ) )
				if ( model.SceneObject.IsValid() ) model.SceneObject.Tags.Remove( BodyTag );
		}
		ActiveCamera = null;
		RestoreCameraTags();
	}

	void RestoreCameraTags()
	{
		if ( _mainCamera.IsValid() )
		{
			_mainCamera.RenderExcludeTags.Set( "viewmodel", _hadViewmodel );
			_mainCamera.RenderExcludeTags.Set( "firstperson", _hadFirstperson );
			_mainCamera.RenderExcludeTags.Set( "viewer", _hadViewer );
			_mainCamera.RenderExcludeTags.Remove( BodyTag );
			_mainCamera.FovAxis = _originalAxis;
		}
		_mainCamera = null;

		// back to the player's own effects
		_viewEffects.Clear();
		if ( Scene.GetSystem<PostProcessManager>() is { } effects )
			effects.Suppressed = false;
	}

	readonly CameraEffectStack _viewEffects = new();

	internal void ApplyView( CameraEntity entity, CameraComponent camera, ref CameraView view )
	{
		if ( entity != ActiveCamera || !CanView( entity ) ) return;

		if ( CameraEffectSystem.Get( Scene ) is { } effects )
			effects.Scale = 0f;

		if ( _mainCamera != camera )
		{
			RestoreCameraTags();
			_mainCamera = camera;
			_hadViewmodel = camera.RenderExcludeTags.Has( "viewmodel" );
			_hadFirstperson = camera.RenderExcludeTags.Has( "firstperson" );
			_hadViewer = camera.RenderExcludeTags.Has( "viewer" );
			_originalAxis = camera.FovAxis;
		}
		foreach ( var model in entity.GetComponentsInChildren<ModelRenderer>( true ) )
			if ( model.SceneObject.IsValid() ) model.SceneObject.Tags.Add( BodyTag );
		camera.RenderExcludeTags.Add( BodyTag );
		camera.RenderExcludeTags.Add( "viewmodel" );
		camera.RenderExcludeTags.Add( "firstperson" );
		camera.RenderExcludeTags.Remove( "viewer" );
		camera.FovAxis = CameraComponent.Axis.Horizontal;

		// the camera's own effects, not the player's, so looking through matches its displays
		if ( Scene.GetSystem<PostProcessManager>() is { } playerEffects )
			playerEffects.Suppressed = true;
		_viewEffects.Apply( camera.GameObject, entity );

		view.Position = entity.ViewTransform.Position;
		view.Rotation = entity.ViewTransform.Rotation;
		view.FieldOfView = entity.ViewFov;
		view.ZNear = entity.Feed?.Camera?.ZNear ?? 1f;
	}
}
