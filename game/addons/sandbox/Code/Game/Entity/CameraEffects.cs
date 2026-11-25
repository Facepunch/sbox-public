/// <summary>
/// Applies the camera's saved effects to a lens or to the player's view through it.
/// </summary>
public sealed class CameraEffectStack
{
	readonly Dictionary<string, EffectSettings> _spawned = new( StringComparer.OrdinalIgnoreCase );
	GameObject _parent;
	int _catalogRevision;
	int? _appliedVersion;

	/// <summary>
	/// Updates the target camera's components and restores any saved property overrides.
	/// </summary>
	public void Apply( GameObject parent, CameraEntity camera )
	{
		if ( !parent.IsValid() || !camera.IsValid() )
		{
			Clear();
			return;
		}

		var manager = camera.Scene.GetSystem<PostProcessManager>();
		var revision = manager.CatalogRevision;
		if ( parent != _parent || revision != _catalogRevision )
			Clear();

		var version = camera.EffectsVersion;
		if ( version == _appliedVersion && AllSpawnedValid() ) return;

		Respawn( parent, camera, version, manager );
	}

	bool AllSpawnedValid()
	{
		foreach ( var settings in _spawned.Values )
			if ( !settings.Component.IsValid() ) return false;

		return true;
	}

	// Separate so the settings closure below is only allocated when something changed
	void Respawn( GameObject parent, CameraEntity camera, int version, PostProcessManager manager )
	{
		_parent = parent;
		_catalogRevision = manager.CatalogRevision;
		_appliedVersion = version;

		foreach ( var id in _spawned.Keys.Where( x => !camera.HasEffect( x ) ).ToList() )
		{
			if ( _spawned[id].GameObject.IsValid() ) _spawned[id].GameObject.Destroy();
			_spawned.Remove( id );
		}

		foreach ( var id in camera.ActiveEffects )
		{
			if ( !_spawned.TryGetValue( id, out var settings ) || !settings.Component.IsValid() )
			{
				if ( settings?.GameObject.IsValid() == true ) settings.GameObject.Destroy();

				var go = manager.CreateEffect( parent, id, true );
				if ( go is null ) continue;

				settings = new EffectSettings( go );
				_spawned[id] = settings;
			}

			settings.Apply( key => camera.GetEffectValue( id, key ) );
		}
	}

	/// <summary>
	/// Removes this stack's local copies of the camera effects.
	/// </summary>
	public void Clear()
	{
		foreach ( var settings in _spawned.Values )
		{
			if ( settings.GameObject.IsValid() ) settings.GameObject.Destroy();
		}

		_spawned.Clear();
		_parent = null;
		_appliedVersion = null;
	}
}
