/// <summary>
/// Payload for spawning a scripted entity prefab.
/// </summary>
public sealed class EntitySpawner : ISpawner
{
	public string DisplayName { get; private set; }
	public string FullIdent => Path is not null && !Path.EndsWith( ".sent" ) ? Path : null;
	public string Icon => Path;
	public string Data => Path;
	public BBox Bounds { get; private set; }
	public bool IsReady => Entity is not null;
	public Task<bool> Loading { get; }
	public GameObject Prefab => Entity?.Prefab is not null ? GameObject.GetPrefab( Entity.Prefab.ResourcePath ) : null;

	public ScriptedEntity Entity { get; private set; }
	public string Path { get; }

	public EntitySpawner( string path )
	{
		Path = path;
		DisplayName = System.IO.Path.GetFileNameWithoutExtension( path );
		Loading = LoadAsync();
	}

	/// <summary>
	/// Wrap an entity that's already loaded.
	/// </summary>
	public EntitySpawner( ScriptedEntity entity )
	{
		Path = entity.ResourcePath;
		Entity = entity;
		DisplayName = entity.Title ?? System.IO.Path.GetFileNameWithoutExtension( Path );
		Bounds = SceneUtility.GetPrefabScene( entity.Prefab ).GetLocalBounds();
		Loading = Task.FromResult( true );
	}

	private async Task<bool> LoadAsync()
	{
		// Try local/installed first, then fall back to cloud
		Entity = await ResourceLibrary.LoadAsync<ScriptedEntity>( Path );
		Entity ??= await Cloud.Load<ScriptedEntity>( Path, true );

		if ( Entity is not null )
		{
			DisplayName = Entity.Title ?? DisplayName;
			var prefabScene = SceneUtility.GetPrefabScene( Entity.Prefab );
			Bounds = prefabScene.GetLocalBounds();
		}

		return IsReady;
	}

	/// <summary>
	/// Get a yaw correction so the longest horizontal axis of the bounds aligns with forward.
	/// </summary>
	private float GetYawCorrection()
	{
		var size = Bounds.Size;
		return size.x > size.y ? 90f : 0f;
	}

	private static Transform AlignUpright( Transform transform )
	{
		var forward = transform.Rotation.Forward.WithZ( 0 );
		if ( forward.LengthSquared < 0.01f )
			forward = Vector3.Forward;

		transform.Rotation = Rotation.LookAt( forward.Normal, Vector3.Up );
		return transform;
	}

	public void DrawPreview( Transform transform, Material overrideMaterial )
	{
		if ( !IsReady ) return;
		transform = AlignUpright( transform );

		// Draw a bounding box cube as a placeholder preview
		var size = Bounds.Size;
		if ( size.IsNearlyZero() ) return;

		transform.Rotation *= Rotation.FromYaw( GetYawCorrection() );

		var center = transform.PointToWorld( Bounds.Center );
		var previewTransform = new Transform( center, transform.Rotation, transform.Scale * (size / 50) );
		Game.ActiveScene.DebugOverlay.Model( Model.Cube, transform: previewTransform, overlay: false, materialOveride: overrideMaterial );
	}

	public Task<List<GameObject>> Spawn( Transform transform, Player player )
	{
		transform = AlignUpright( transform );
		var depth = -Bounds.Mins.z;
		transform.Position += transform.Up * depth;
		transform.Rotation *= Rotation.FromYaw( GetYawCorrection() );

		// Nudge to a nearby clear spot so entities don't spawn on top of each other.
		transform = SpawnPlacement.FindSpawnPosition( new[] { transform }, Bounds,
			dropToFloor: false, scanRadius: MathF.Max( Bounds.Size.x, Bounds.Size.y ) );

		var go = GameObject.Clone( Entity.Prefab, new CloneConfig { Transform = transform, StartEnabled = false } );
		go.Tags.Add( "removable" );

		// armed NPCs carry the weapon the player picked in the NPC tab
		foreach ( var npc in go.GetComponentsInChildren<Sandbox.Npcs.CombatNpc.CombatNpc>( true ) )
			npc.ArmWithPicked( player );

		Ownable.Set( go, player.Network.Owner );
		go.NetworkSpawn( true, null );

		return Task.FromResult( new List<GameObject> { go } );
	}

	public void PopulateContextMenu( Sandbox.UI.Menu menu, string ident, string metadata )
	{
		if ( Prefab?.GetComponent<BaseSandboxWeapon>( true ) is not null )
		{
			menu.AddOption( "#spawner.spawn_in_world", "public", () => GameManager.Spawn( ident, metadata, forceWorld: true ) );
		}
	}
}
