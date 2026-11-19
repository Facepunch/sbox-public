using Sandbox.UI;

/// <summary>
/// A world-placed SENT that spawns another SENT at its location.
/// Can be triggered manually via player input or automatically on a timer.
/// </summary>
[Alias( "entity_spawner" )]
[Title( "#entity.name.spawner" )]
public sealed class EntitySpawnerEntity : Component, IPlayerControllable
{
	/// <summary>
	/// The SENT to spawn.
	/// </summary>
	[Property, Title( "#entity.property.entity" ), Description( "#entity.description.entity" ), ClientEditable]
	public ScriptedEntity Entity { get; set; }

	/// <summary>
	/// Input binding that triggers a manual spawn when the player uses this entity.
	/// </summary>
	[Property, Title( "#entity.property.spawninput" ), Description( "#entity.description.spawninput" ), ClientEditable, Group( "#entity.group.input" )]
	public ClientInput SpawnInput { get; set; }

	/// <summary>
	/// When enabled, spawns the entity automatically every <see cref="SpawnInterval"/> seconds.
	/// </summary>
	[Property, Title( "#entity.property.autospawn" ), Description( "#entity.description.autospawn" ), ClientEditable, Group( "#entity.group.auto_spawn" )]
	public bool AutoSpawn { get; set; } = false;

	/// <summary>
	/// Seconds between automatic spawns.
	/// </summary>
	[Property, Title( "#entity.property.spawninterval" ), Description( "#entity.description.spawninterval" ), ClientEditable, Range( 1f, 300f ), Step( 1 ), Group( "#entity.group.auto_spawn" )]
	public float SpawnInterval { get; set; } = 5f;

	/// <summary>
	/// Maximum number of entities spawned by this spawner allowed to exist at once.
	/// New spawns are suppressed until existing ones are destroyed.
	/// </summary>
	[Property, Title( "#entity.property.maxentities" ), Description( "#entity.description.maxentities" ), ClientEditable, Range( 1, 50 ), Step( 1 ), Group( "#entity.group.auto_spawn" )]
	public float MaxEntities { get; set; } = 5;

	[Sync( SyncFlags.FromHost )]
	private TimeSince _timeSinceLastSpawn { get; set; }
	private readonly List<WeakReference<GameObject>> _spawnedEntities = new();

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		if ( !AutoSpawn ) return;
		if ( _timeSinceLastSpawn < SpawnInterval ) return;

		DoSpawn();
	}

	/// <summary>
	/// Spawns an entity when the signal input is triggered.
	/// </summary>
	[SignalInput( Id = nameof( SpawnInput ), Default = true ), Title( "#entity.property.spawninput" )]
	public void SpawnSignal() => DoSpawn();

	/// <summary>
	/// Spawns an entity when the owning player presses the spawn input.
	/// </summary>
	public void OnControl()
	{
		if ( SpawnInput.Pressed() ) DoSpawn();
	}

	[Rpc.Host]
	private void DoSpawn()
	{
		if ( !Entity.IsValid() || Entity.Prefab is null ) return;

		// Prune destroyed entities from the tracking list
		_spawnedEntities.RemoveAll( wr => !wr.TryGetTarget( out var go ) || !go.IsValid() );

		if ( _spawnedEntities.Count >= MaxEntities ) return;

		_timeSinceLastSpawn = 0;

		var caller = Rpc.Caller ?? GameObject.Network.Owner;
		var player = Player.FindForConnection( caller );

		var spawnData = new Global.ISpawnEvents.SpawnData
		{
			Spawner = new EntitySpawner( Entity ),
			Transform = WorldTransform,
			Player = caller
		};

		Scene.RunEvent<Global.ISpawnEvents>( x => x.OnSpawn( spawnData ) );
		if ( spawnData.Cancelled ) return;

		var spawned = GameObject.Clone( Entity.Prefab, new CloneConfig
		{
			Transform = WorldTransform,
			StartEnabled = false,
		} );

		spawned.Tags.Add( "removable" );

		Ownable.Set( spawned, caller );
		spawned.NetworkSpawn( true, null );
		spawned.Enabled = true;

		_spawnedEntities.Add( new WeakReference<GameObject>( spawned ) );

		if ( player is not null )
		{
			var undo = player.Undo.Create();
			undo.Name = "#undo.spawn";
			undo.NameTokens = new() { { "name", Entity.Title ?? Entity.ResourceName } };
			undo.Add( spawned );
		}

		Scene.RunEvent<Global.ISpawnEvents>( x => x.OnPostSpawn( new Global.ISpawnEvents.PostSpawnData
		{
			Spawner = spawnData.Spawner,
			Transform = spawnData.Transform,
			Player = caller,
			Objects = [spawned]
		} ) );
	}
}
