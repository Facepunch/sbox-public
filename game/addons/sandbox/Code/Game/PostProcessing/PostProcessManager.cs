/// <summary>
/// Discovers menu effects and manages the player's local post-process volume.
/// </summary>
public sealed partial class PostProcessManager : GameObjectSystem<PostProcessManager>
{
	private readonly Dictionary<string, GameObject> _active = new();
	private readonly HashSet<string> _enabled = new();
	private PostProcessVolume _volume;

	/// <summary>
	/// Class name of the effect selected in the menu.
	/// </summary>
	public string SelectedPath { get; private set; }

	/// <summary>
	/// Returns the selected effect's editable components.
	/// </summary>
	public IReadOnlyList<Component> GetSelectedComponents()
	{
		if ( SelectedPath == null ) return [];

		if ( !_active.TryGetValue( SelectedPath, out var go ) || !go.IsValid() )
			return [];

		return [.. go.GetComponentsInChildren<Component>( true )];
	}

	/// <summary>
	/// Creates the local menu effect system for a scene.
	/// </summary>
	public PostProcessManager( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 0, RefreshCatalog, "Post-process menu types" );
	}

	/// <summary>
	/// Whether the player enabled this effect.
	/// </summary>
	public bool IsEnabled( string id ) => _enabled.Contains( id );

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

	private void SetEnabled( string id, bool enabled )
	{
		if ( !_active.TryGetValue( id, out var go ) ) return;

		go.Enabled = enabled && !_suppressed;

		if ( enabled ) _enabled.Add( id );
		else _enabled.Remove( id );
	}

	private PostProcessVolume GetLocalVolume()
	{
		if ( _volume.IsValid() ) return _volume;

		var root = Scene.CreateObject();
		root.Name = "Local Post Processing";
		root.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		_volume = root.AddComponent<PostProcessVolume>();
		_volume.SceneVolume = new Sandbox.Volumes.SceneVolume { Type = Sandbox.Volumes.SceneVolume.VolumeTypes.Infinite };
		_volume.Priority = int.MaxValue;
		return _volume;
	}

	private void SpawnGo( string id, bool startEnabled )
	{
		var go = CreateEffect( GetLocalVolume().GameObject, id, startEnabled && !_suppressed );
		if ( go.IsValid() ) _active[id] = go;
	}

	/// <summary>
	/// Selects an effect for editing.
	/// </summary>
	public void Select( string id )
	{
		SelectedPath = id;
		if ( !_active.ContainsKey( id ) )
			SpawnGo( id, startEnabled: false );
	}

	private string _previewPath;

	/// <summary>
	/// Temporarily applies an effect while hovering over it.
	/// </summary>
	public void Preview( string id )
	{
		if ( _previewPath == id ) return;
		Unpreview();

		_previewPath = id;
		if ( IsEnabled( id ) ) return;

		if ( _active.TryGetValue( id, out var go ) )
		{
			go.Enabled = !_suppressed;
		}
		else
		{
			SpawnGo( id, startEnabled: true );
		}
	}

	/// <summary>
	/// Ends the hover preview.
	/// </summary>
	public void Unpreview()
	{
		if ( _previewPath is null ) return;

		if ( !IsEnabled( _previewPath ) && _active.TryGetValue( _previewPath, out var go ) )
			go.Enabled = false;

		_previewPath = null;
	}

	/// <summary>
	/// Clears the menu selection.
	/// </summary>
	public void Deselect()
	{
		SelectedPath = null;
	}

	/// <summary>
	/// Toggles the selected effect on the player's camera.
	/// </summary>
	public void Toggle( string id )
	{
		if ( FindEffect( id ) is null ) return;

		Select( id );
		SetEnabled( id, !IsEnabled( id ) );
	}


	/// <summary>
	/// Changes the enabled state of an instantiated effect.
	/// </summary>
	public void Set( string id, bool state )
	{
		if ( state == IsEnabled( id ) || FindEffect( id ) is null ) return;

		if ( !_active.ContainsKey( id ) ) SpawnGo( id, startEnabled: false );

		SetEnabled( id, state );
	}

	/// <summary>
	/// Destroys the local effect and clears its selection.
	/// </summary>
	public void Remove( string id )
	{
		if ( _active.TryGetValue( id, out var go ) )
		{
			if ( go.IsValid() ) go.Destroy();
			_active.Remove( id );
		}

		_enabled.Remove( id );

		if ( _previewPath == id ) _previewPath = null;
		if ( SelectedPath == id )
			SelectedPath = null;
	}
}
