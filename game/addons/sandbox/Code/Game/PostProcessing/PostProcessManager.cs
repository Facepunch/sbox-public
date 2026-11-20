public sealed class PostProcessManager : GameObjectSystem<PostProcessManager>
{
	private readonly Dictionary<string, GameObject> _active = new();
	private readonly HashSet<string> _enabled = new();

	public string SelectedPath { get; private set; }

	public IReadOnlyList<Component> GetComponents( string resourcePath )
	{
		if ( !_active.TryGetValue( resourcePath, out var go ) || !go.IsValid() )
			return [];

		return [.. go.GetComponentsInChildren<Component>( true )];
	}

	public IReadOnlyList<Component> GetSelectedComponents()
	{
		if ( SelectedPath == null ) return [];

		if ( !_active.TryGetValue( SelectedPath, out var go ) || !go.IsValid() )
			return [];

		return [.. go.GetComponentsInChildren<Component>( true )];
	}

	public PostProcessManager( Scene scene ) : base( scene ) { }

	public bool IsEnabled( string resourcePath ) => _enabled.Contains( resourcePath );

	bool _suppressed;

	/// <summary>
	/// Hide the player's effects while looking through another camera view or something
	/// </summary>
	public bool Suppressed
	{
		get => _suppressed;
		set
		{
			if ( _suppressed == value ) return;
			_suppressed = value;

			foreach ( var (path, go) in _active )
			{
				if ( go.IsValid() )
					go.Enabled = !value && (_enabled.Contains( path ) || path == _previewPath);
			}
		}
	}

	private void SetEnabled( string resourcePath, bool enabled )
	{
		if ( !_active.TryGetValue( resourcePath, out var go ) ) return;

		go.Enabled = enabled && !_suppressed;

		if ( enabled ) _enabled.Add( resourcePath );
		else _enabled.Remove( resourcePath );
	}

	private void SpawnGo( string resourcePath, bool startEnabled )
	{
		var resource = ResourceLibrary.Get<PostProcessResource>( resourcePath );
		if ( resource?.Prefab is null ) return;

		var camera = Scene.Camera?.GameObject;
		if ( camera is null ) return;

		// Spawn enabled so components initialize, then disable if not wanted
		var go = GameObject.Clone( resource.Prefab, new CloneConfig { StartEnabled = true, Parent = camera } );
		go.Flags |= GameObjectFlags.NotNetworked;

		_active[resourcePath] = go;

		if ( !startEnabled || _suppressed )
			go.Enabled = false;
	}

	public void Select( string resourcePath )
	{
		SelectedPath = resourcePath;
		if ( !_active.ContainsKey( resourcePath ) )
			SpawnGo( resourcePath, startEnabled: false );
	}

	private string _previewPath;

	public void Preview( string resourcePath )
	{
		if ( _previewPath == resourcePath ) return;
		Unpreview();

		_previewPath = resourcePath;
		if ( IsEnabled( resourcePath ) ) return;

		if ( !_active.ContainsKey( resourcePath ) )
			SpawnGo( resourcePath, startEnabled: true );
		else
			SetEnabled( resourcePath, true );
	}

	public void Unpreview()
	{
		if ( _previewPath is null ) return;

		if ( !IsEnabled( _previewPath ) )
			SetEnabled( _previewPath, false );

		_previewPath = null;
	}

	public void Deselect()
	{
		SelectedPath = null;
	}

	public void Toggle( string resourcePath )
	{
		SelectedPath = resourcePath;

		if ( IsEnabled( resourcePath ) )
		{
			_enabled.Remove( resourcePath );
			SetEnabled( resourcePath, false );
			return;
		}

		_enabled.Add( resourcePath );

		if ( !_active.ContainsKey( resourcePath ) )
			SpawnGo( resourcePath, startEnabled: true );
		else
			SetEnabled( resourcePath, true );
	}


	public void Set( string resourcePath, bool state )
	{
		if ( state == IsEnabled( resourcePath ) ) return;

		SetEnabled( resourcePath, state );
	}

	public void Remove( string resourcePath )
	{
		if ( _active.TryGetValue( resourcePath, out var go ) )
		{
			go.Destroy();
			_active.Remove( resourcePath );
		}

		_enabled.Remove( resourcePath );

		if ( SelectedPath == resourcePath )
			SelectedPath = null;
	}
}
