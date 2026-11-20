public sealed class CameraEffectStack
{
	readonly Dictionary<string, GameObject> _spawned = new( StringComparer.OrdinalIgnoreCase );
	GameObject _parent;
	int? _appliedVersion;

	public void Apply( GameObject parent, CameraEntity camera )
	{
		if ( !parent.IsValid() || !camera.IsValid() )
		{
			Clear();
			return;
		}

		var version = camera.EffectsVersion;
		if ( parent == _parent && version == _appliedVersion && _spawned.Values.All( x => x.IsValid() ) ) return;
		if ( parent != _parent ) Clear();

		_parent = parent;
		_appliedVersion = version;

		var wanted = camera.ActiveEffects.ToHashSet( StringComparer.OrdinalIgnoreCase );
		foreach ( var path in _spawned.Keys.Where( x => !wanted.Contains( x ) ).ToList() )
		{
			_spawned[path]?.Destroy();
			_spawned.Remove( path );
		}

		foreach ( var path in camera.ActiveEffects )
		{
			if ( !_spawned.TryGetValue( path, out var go ) || !go.IsValid() )
			{
				go = Spawn( parent, path );
				if ( go is null ) continue;
				_spawned[path] = go;
			}

			EffectSettings.Apply( path, go.GetComponentsInChildren<Component>( true ), setting => camera.GetEffectValue( path, setting ) );
		}
	}

	static GameObject Spawn( GameObject parent, string path )
	{
		// a workshop effect only resolves once its package is mounted on this client
		var resource = ResourceLibrary.Get<PostProcessResource>( path );
		if ( resource?.Prefab is null ) return null;

		var go = GameObject.Clone( resource.Prefab, new CloneConfig { StartEnabled = true, Parent = parent } );
		go.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		return go;
	}

	/// <summary>
	/// The live components of one effect, for editing its settings.
	/// </summary>
	public IReadOnlyList<Component> GetComponents( string path )
	{
		if ( !_spawned.TryGetValue( path, out var go ) || !go.IsValid() ) return [];
		return [.. go.GetComponentsInChildren<Component>( true )];
	}

	public void Clear()
	{
		foreach ( var go in _spawned.Values )
		{
			if ( go.IsValid() ) go.Destroy();
		}

		_spawned.Clear();
		_parent = null;
		_appliedVersion = null;
	}
}
