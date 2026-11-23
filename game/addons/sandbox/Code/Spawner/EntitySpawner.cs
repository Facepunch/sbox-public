/// <summary>
/// Spawns a prefab offered by a scripted entity or a spawnlist.
/// </summary>
public sealed class EntitySpawner : ISpawner
{
	public string DisplayName { get; private set; }
	public string FullIdent => ListedPrefab is null && Path is not null && !Path.EndsWith( ".sent" ) ? Path : null;
	public string Icon => Path;
	public string Data => Path;
	public BBox Bounds { get; private set; }
	public bool IsReady => ListedPrefab is not null || Entity is not null && Entity.IsMountAvailable
		&& (string.IsNullOrEmpty( Entity.RequiredMount ) || Sandbox.Mounting.Directory.Get( Entity.RequiredMount )?.IsMounted == true);
	public Task<bool> Loading { get; }
	public GameObject Prefab => PrefabFile is not null ? GameObject.GetPrefab( PrefabFile.ResourcePath ) : null;

	internal PrefabFile ListedPrefab { get; }
	PrefabFile PrefabFile => ListedPrefab ?? Entity?.Prefab;

	public ScriptedEntity Entity { get; private set; }
	public string Path { get; }

	internal EntitySpawner( SpawnlistResource.Entry entry )
	{
		ListedPrefab = entry.Prefab;
		Path = ListedPrefab.ResourcePath;
		DisplayName = entry.Title ?? ListedPrefab.ResourceName;
		Bounds = ListedPrefab.GetScene().GetLocalBounds();
		Loading = Task.FromResult( true );
	}

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
		if ( string.IsNullOrEmpty( entity.RequiredMount ) )
		{
			Bounds = SceneUtility.GetPrefabScene( entity.Prefab ).GetLocalBounds();
			Loading = Task.FromResult( true );
		}
		else
		{
			Loading = LoadAsync();
		}
	}

	private async Task<bool> LoadAsync()
	{
		Entity = await ResourceLibrary.LoadAsync<ScriptedEntity>( Path );
		Entity ??= await Cloud.Load<ScriptedEntity>( Path, true );

		if ( Entity is not null && !string.IsNullOrEmpty( Entity.RequiredMount ) )
		{
			var mount = await Sandbox.Mounting.Directory.Mount( Entity.RequiredMount );
			if ( mount?.IsMounted != true )
			{
				Log.Warning( $"{Entity.Title} requires '{Entity.RequiredMount}' installed on the host." );
				Entity = null;
				return false;
			}
		}

		if ( Entity is not null )
		{
			DisplayName = Entity.Title ?? DisplayName;
			var prefabScene = SceneUtility.GetPrefabScene( Entity.Prefab );
			Bounds = prefabScene.GetLocalBounds();

			if ( await Entity.LoadPreviewModelAsync() is { } model )
				Bounds = MountedBounds( prefabScene, model );
		}

		return IsReady;
	}

	/// <summary>
	/// A mounted entity's prefab has an empty renderer the mount fills in at runtime - its bounds are the model's, wherever
	/// that renderer sits in the prefab.
	/// </summary>
	internal static BBox MountedBounds( GameObject prefab, Model model )
	{
		var renderer = prefab.GetComponentsInChildren<ModelRenderer>( true ).FirstOrDefault( x => x.Model is null );
		var local = renderer.IsValid() ? prefab.WorldTransform.ToLocal( renderer.WorldTransform ) : global::Transform.Zero;
		return model.Bounds.Transform( local );
	}

	/// <summary>
	/// Get a yaw correction so the longest horizontal axis of the bounds aligns with forward.
	/// </summary>
	private static float GetYawCorrection( BBox bounds )
	{
		var size = bounds.Size;
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
		DrawBounds( Bounds, transform, overrideMaterial );
	}

	/// <summary>
	/// Draw a bounding box cube as a placeholder preview, upright and turned like <see cref="SpawnPrefab"/> places it.
	/// </summary>
	internal static void DrawBounds( BBox bounds, Transform transform, Material overrideMaterial )
	{
		transform = AlignUpright( transform );

		var size = bounds.Size;
		if ( size.IsNearlyZero() ) return;

		transform.Rotation *= Rotation.FromYaw( GetYawCorrection( bounds ) );

		var center = transform.PointToWorld( bounds.Center );
		var previewTransform = new Transform( center, transform.Rotation, transform.Scale * (size / 50) );
		Game.ActiveScene.DebugOverlay.Model( Model.Cube, transform: previewTransform, overlay: false, materialOveride: overrideMaterial );
	}

	public Task<List<GameObject>> Spawn( Transform transform, Player player ) => Task.FromResult( SpawnPrefab( PrefabFile, Bounds, transform, player ) );

	/// <summary>
	/// Clone an entity prefab standing on the spot, clear of others, with the sandbox's rules: removable, owned by
	/// <paramref name="player"/>, weapons fired through <see cref="WeaponControls"/>, NPCs armed with the player's pick.
	/// </summary>
	internal static List<GameObject> SpawnPrefab( PrefabFile prefab, BBox bounds, Transform transform, Player player )
	{
		transform = AlignUpright( transform );
		var depth = -bounds.Mins.z;
		transform.Position += transform.Up * depth;
		transform.Rotation *= Rotation.FromYaw( GetYawCorrection( bounds ) );

		// Nudge to a nearby clear spot so entities don't spawn on top of each other.
		transform = SpawnPlacement.FindSpawnPosition( new[] { transform }, bounds,
			dropToFloor: false, scanRadius: MathF.Max( bounds.Size.x, bounds.Size.y ) );

		var go = GameObject.Clone( prefab, new CloneConfig { Transform = transform, StartEnabled = false } );
		go.Tags.Add( "removable" );

		foreach ( var weapon in go.GetComponentsInChildren<BaseCombatWeapon>( true ) )
			if ( !weapon.IsHeld )
				WeaponControls.Attach( weapon );

		foreach ( var npc in go.GetComponentsInChildren<Sandbox.Npcs.BaseCombatNpc>( true ) )
			npc.ArmWithPicked( player );

		Ownable.Set( go, player.Network.Owner );
		go.NetworkSpawn( true, null );

		return new List<GameObject> { go };
	}

	public void PopulateContextMenu( Sandbox.UI.Menu menu, string ident, string metadata )
	{
		if ( Prefab?.GetComponent<BaseCombatWeapon>( true ) is not null )
		{
			menu.AddOption( "#spawner.spawn_in_world", "public", () => GameManager.Spawn( ident, metadata, forceWorld: true ) );
		}
	}
}
