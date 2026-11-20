using Sandbox.Npcs;
using Sandbox.UI;

public sealed partial class GameManager : GameObjectSystem<GameManager>, Component.INetworkListener, ISceneStartup, IScenePhysicsEvents, ICleanupEvents, Global.ISaveEvents
{
	public GameManager( Scene scene ) : base( scene )
	{
	}

	void ISceneStartup.OnHostInitialize()
	{
		if ( !Networking.IsActive )
		{
			Networking.CreateLobby( new Sandbox.Network.LobbyConfig() { Privacy = Sandbox.Network.LobbyPrivacy.Public, MaxPlayers = 32, Name = "Sandbox", DestroyWhenHostLeaves = false } );
		}
	}

	internal void Notify( string text )
	{
		Assert.True( Networking.IsHost, "Only the host can send notifications" );
		NotifyRpc( text );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void NotifyRpc( string text )
	{
		Sandbox.Platform.Chat.AddText( text );
	}

	/// <summary>
	/// Broadcasts a phrase reference and data so each player formats the message locally.
	/// </summary>
	internal void NotifyLocalized( string phrase, Dictionary<string, string> tokens )
	{
		Assert.True( Networking.IsHost, "Only the host can send notifications" );
		NotifyLocalizedRpc( phrase, Sandbox.Json.Serialize( tokens ) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void NotifyLocalizedRpc( string phrase, string tokensJson )
	{
		var tokens = Sandbox.Json.Deserialize<Dictionary<string, string>>( tokensJson );
		Sandbox.Platform.Chat.AddText( LocalizedText.Resolve( phrase, tokens ) );
	}

	bool Component.INetworkListener.AcceptConnection( Connection channel, ref string reason )
	{
		var e = new ConnectingEvent { Client = channel, Reason = reason };
		Scene.RunEvent<Global.IConnectionEvents>( x => x.OnConnecting( e ) );

		reason = e.Reason;
		return !e.Cancelled;
	}

	void Component.INetworkListener.OnActive( Connection channel )
	{
		channel.CanSpawnObjects = false;

		var playerData = CreatePlayerInfo( channel );
		Scene.RunEvent<Global.IConnectionEvents>( x => x.OnPlayerJoined( playerData ) );

		SpawnPlayer( playerData );
		CheckConnectionAchievement( channel );
		CheckFriendsOnlineStat();
	}

	/// <summary>
	/// Called when someone leaves the server. This will only be called for the host.
	/// </summary>
	void Component.INetworkListener.OnDisconnected( Connection channel )
	{
		var pd = PlayerData.For( channel );
		if ( pd is not null )
		{
			Scene.RunEvent<Global.IConnectionEvents>( x => x.OnPlayerLeft( pd ) );
			pd.GameObject.Destroy();
		}

		UndoSystem.Current?.RemovePlayer( channel.SteamId );
	}

	private PlayerData CreatePlayerInfo( Connection channel )
	{
		var existingPlayerInfo = PlayerData.For( channel );
		if ( existingPlayerInfo.IsValid() )
			return existingPlayerInfo;

		var go = new GameObject( true, $"PlayerInfo - {channel.DisplayName}" );
		var data = go.AddComponent<PlayerData>();

		go.NetworkSpawn( channel );
		go.Network.SetOwnerTransfer( OwnerTransfer.Fixed );

		return data;
	}

	/// <summary>
	/// Spawns a player for this connection if they don't already have one. Host only.
	/// Fires <see cref="Global.IPlayerEvents.OnPlayerRespawning"/> first, so listeners can move or
	/// cancel the spawn. Pass <paramref name="force"/> to skip that event - for addons that cancelled
	/// the normal flow and now want the player in the world. Returns the new player, or null if the
	/// connection already has one or a listener cancelled.
	/// </summary>
	public Player SpawnPlayer( Connection connection, bool force = false ) => SpawnPlayer( PlayerData.For( connection ), force );

	/// <inheritdoc cref="SpawnPlayer(Connection, bool)"/>
	public Player SpawnPlayer( PlayerData playerData, bool force = false )
	{
		Assert.NotNull( playerData, "PlayerData is null" );
		Assert.True( Networking.IsHost, $"Client tried to SpawnPlayer: {playerData.Network.Owner?.DisplayName}" );

		// does this connection already have a player?
		if ( Scene.GetAll<Player>().Any( x => x.Network.Owner == playerData.Network.Owner ) )
			return null;

		// Find a spawn location for this player
		var startLocation = FindSpawnLocation().WithScale( 1 );

		// Fire pre-respawn event - listeners can move or cancel the spawn, unless we're forcing it
		if ( !force )
		{
			var respawnEvent = new PlayerRespawnEvent { PlayerData = playerData, SpawnLocation = startLocation };
			Global.IPlayerEvents.Post( x => x.OnPlayerRespawning( respawnEvent ) );

			if ( respawnEvent.Cancelled )
				return null;

			startLocation = respawnEvent.SpawnLocation;
		}

		// Spawn this object and make the client the owner
		var playerGo = GameObject.Clone( "/prefabs/engine/player.prefab", new CloneConfig { Name = playerData.Network.Owner?.DisplayName, StartEnabled = false, Transform = startLocation } );

		var player = playerGo.Components.Get<Player>( true );
		player.PlayerData = playerData;

		var owner = playerData.Network.Owner;
		playerGo.NetworkSpawn( owner );

		Local.IPlayerEvents.PostToGameObject( player.GameObject, x => x.OnSpawned() );
		Global.IPlayerEvents.Post( x => x.OnPlayerSpawned( player ) );

		return player;
	}

	/// <summary>
	/// Forcefully respawns a player, whether they're dead or alive. Host only. Removes their current
	/// player object and any observer without a death, then spawns a fresh one without consulting
	/// <see cref="Global.IPlayerEvents.OnPlayerRespawning"/>. This is the entry point for addons
	/// that run their own round flow: cancel the normal respawn, then call this when it's time.
	/// </summary>
	public Player Respawn( Connection connection )
	{
		Assert.True( Networking.IsHost, "Only the host can respawn players" );

		var playerData = PlayerData.For( connection );
		if ( !playerData.IsValid() )
			return null;

		var existing = Player.FindForConnection( connection );
		if ( existing.IsValid() )
		{
			existing.GameObject.Destroy();
		}

		DestroyObservers( connection );

		return SpawnPlayer( playerData, force: true );
	}

	/// <summary>
	/// Clean up any lingering death observers for this connection.
	/// </summary>
	private void DestroyObservers( Connection connection )
	{
		foreach ( var observer in Scene.GetAllComponents<PlayerObserver>().Where( x => x.Network.Owner == connection ).ToArray() )
		{
			observer.GameObject.Destroy();
		}
	}

	void Global.ISaveEvents.AfterLoad( string filename )
	{
		if ( !Networking.IsHost ) return;

		// Make sure we spawn any players that weren't included in the loaded save
		foreach ( var connection in Connection.All )
		{
			var playerData = CreatePlayerInfo( connection );
			SpawnPlayer( playerData );
		}
	}

	/// <summary>
	/// Called by the client (via PlayerObserver) when they want to respawn. If a listener cancels the
	/// respawn the observer is kept, so the player stays spectating until an addon calls
	/// <see cref="Respawn"/> or the next request goes through.
	/// </summary>
	[Rpc.Host]
	internal void RequestRespawn()
	{
		var connection = Rpc.Caller;

		var player = SpawnPlayer( connection );
		if ( !player.IsValid() )
			return;

		DestroyObservers( connection );
	}

	/// <summary>Approximate standing-player hull, used for spawn clearance and floor settling.</summary>
	private static readonly BBox PlayerHull = new( new Vector3( -16f, -16f, 0f ), new Vector3( 16f, 16f, 72f ) );

	/// <summary>The last position we spawned a player at, so we can avoid reusing it.</summary>
	private Vector3? _lastSpawnPosition;

	/// <summary>
	/// Find a place to respawn: a floor-settled spawn point nobody is standing on, avoiding the
	/// last one used.
	/// </summary>
	Transform FindSpawnLocation()
	{
		var spawnPoints = Scene.GetAllComponents<SpawnPoint>().ToArray();

		// No spawn points — fall back to the world origin and warn. The map needs a SpawnPoint.
		if ( spawnPoints.Length == 0 )
		{
			Log.Warning( "No SpawnPoint components in the scene — spawning at the world origin. Add a SpawnPoint to the map to fix this." );
			return Transform.Zero;
		}

		var transform = SpawnPlacement.FindSpawnPosition(
			spawnPoints.OrderBy( _ => Random.Shared.Next() ).Select( x => x.Transform.World ),
			PlayerHull,
			avoid: _lastSpawnPosition );

		_lastSpawnPosition = transform.Position;
		return transform;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private static void NotifyConsole( string msg )
	{
		Log.Info( msg );
	}

	/// <summary>
	/// Called on the host when a played is killed
	/// </summary>
	internal void OnDeath( Player player, DamageInfo dmg )
	{
		Assert.True( Networking.IsHost );

		Assert.True( player.IsValid(), "Player invalid" );
		Assert.True( player.PlayerData.IsValid(), $"{player.GameObject.Name}'s PlayerData invalid" );

		// credit the player who threw the prop or drove the car
		var source = DamageBlame.Find( dmg ) as IKillSource ?? dmg.Attacker?.GetComponentInParent<IKillSource>( true );
		if ( source == null ) return;

		var isSuicide = source is Player p && p == player;

		if ( !isSuicide )
			source.OnKill( player.GameObject );

		// Fire kill event on the killer if they're a player
		if ( !isSuicide && source is Player killer )
		{
			var killEvent = new PlayerKillEvent { Player = killer, Victim = player.GameObject, DamageInfo = dmg };
			Local.IPlayerEvents.PostToGameObject( killer.GameObject, x => x.OnKill( killEvent ) );
			Global.IPlayerEvents.Post( x => x.OnPlayerKill( killEvent ) );
		}

		player.PlayerData.Deaths++;

		var weapon = dmg.Weapon;
		var w = weapon.IsValid() ? weapon.GetComponentInChildren<IKillIcon>() : null;
		var damageTags = dmg.Tags.ToString() + ( isSuicide ? " suicide" : "" );
		var attackerTags = isSuicide ? "" : source.Tags;
		var attackerName = isSuicide ? null : source.DisplayName;
		var attackerSteamId = isSuicide ? 0L : source.SteamId;
		var playerName = player.Network.Owner?.DisplayName ?? "Unknown";
		var playerSteamId = (long)(player.Network.Owner?.SteamId ?? default);
		Scene.RunEvent<Feed>( x => x.NotifyKill( playerName, playerSteamId, attackerName, attackerSteamId, damageTags, attackerTags, "", w?.DisplayIcon ) );

		if ( string.IsNullOrEmpty( attackerName ) )
		{
			NotifyConsole( $"{playerName} died (tags: {dmg.Tags})" );
		}
		else if ( weapon.IsValid() )
		{
			NotifyConsole( $"{attackerName} killed {(isSuicide ? "self" : playerName)} with {weapon.Name} (tags: {dmg.Tags})" );
		}
		else
		{
			NotifyConsole( $"{attackerName} killed {(isSuicide ? "self" : playerName)} (tags: {dmg.Tags})" );
		}
	}

	/// <summary>
	/// Called on the host when an NPC is killed. Credits the attacker and adds a kill feed entry.
	/// </summary>
	internal void OnNpcDeath( Npc victim, DamageInfo dmg )
	{
		Assert.True( Networking.IsHost );
		Assert.True( victim.IsValid(), "NPC invalid" );

		var source = DamageBlame.Find( dmg ) as IKillSource ?? dmg.Attacker?.GetComponent<IKillSource>();
		source?.OnKill( victim.GameObject );

		if ( source is Player killer )
		{
			var killEvent = new PlayerKillEvent { Player = killer, Victim = victim.GameObject, DamageInfo = dmg };
			Local.IPlayerEvents.PostToGameObject( killer.GameObject, x => x.OnKill( killEvent ) );
			Global.IPlayerEvents.Post( x => x.OnPlayerKill( killEvent ) );
		}

		var w = dmg.Weapon.IsValid() ? dmg.Weapon.GetComponentInChildren<IKillIcon>() : null;
		var attackerName = source?.DisplayName;
		var attackerSteamId = source?.SteamId ?? 0L;
		var attackerTags = source?.Tags ?? "";

		Scene.RunEvent<Feed>( x => x.NotifyKill( victim.DisplayName, 0, attackerName, attackerSteamId, dmg.Tags.ToString(), attackerTags, "npc", w?.DisplayIcon ) );
	}

	/// <summary>
	/// Change a property, remotely
	/// </summary>
	[Rpc.Host]
	internal static void ChangeProperty( Component c, string propertyName, object value )
	{
		if ( !c.IsValid() ) return;

		var tl = TypeLibrary.GetType( c.GetType() );
		if ( tl is null ) return;

		var prop = tl.GetProperty( propertyName );
		if ( prop is null ) return;

		prop.SetValue( c, value );

		// Broadcast the change to everyone

		// BUG - this is optimal I think, but doesn't work??
		// c.GameObject.Network.Refresh( c );

		c.GameObject.Network?.Refresh();
	}

	/// <summary>
	/// Apply a debounced batch of morph changes to a <see cref="SkinnedModelRenderer"/>,
	/// replicated to all clients. Only the morphs present in the batch are modified.
	/// </summary>
	[Rpc.Host]
	internal static void ApplyMorphBatch( SkinnedModelRenderer smr, string morphsJson )
	{
		if ( !smr.IsValid() ) return;

		smr.GameObject.GetOrAddComponent<MorphState>().ApplyBatch( morphsJson );
	}

	/// <summary>
	/// Apply a full morph preset (as json), and captures with <see cref="MorphState"/> which replicates changes to other clients
	/// </summary>
	[Rpc.Host]
	internal static void ApplyFacePosePreset( SkinnedModelRenderer smr, string morphsJson )
	{
		if ( !smr.IsValid() ) return;

		smr.GameObject.GetOrAddComponent<MorphState>().ApplyPreset( morphsJson );
	}

	[Rpc.Host]
	internal static async void ChangeMaterialOverride( ModelRenderer renderer, int materialIndex, string materialPath )
	{
		if ( !renderer.IsValid() ) return;

		Material material = null;

		if ( !string.IsNullOrEmpty( materialPath ) )
		{
			material = Material.Load( materialPath );
			material ??= await Cloud.Load<Material>( materialPath );
		}

		if ( !renderer.IsValid() ) return;

		renderer.Materials.SetOverride( materialIndex, material );

		renderer.GameObject.Network?.Refresh();
	}

	/// <summary>
	/// Set the material that replaces every slot on a renderer, or clear it with a null/empty path.
	/// </summary>
	[Rpc.Host]
	internal static async void ChangeWholeMaterialOverride( ModelRenderer renderer, string materialPath )
	{
		if ( !renderer.IsValid() ) return;

		Material material = null;

		if ( !string.IsNullOrEmpty( materialPath ) )
		{
			material = Material.Load( materialPath );
			material ??= await Cloud.Load<Material>( materialPath );
		}

		if ( !renderer.IsValid() ) return;

		renderer.MaterialOverride = material;

		renderer.GameObject.Network?.Refresh();
	}

	[Rpc.Host]
	internal static void ChangeMaterialGroup( ModelRenderer renderer, string group )
	{
		if ( !renderer.IsValid() ) return;

		renderer.MaterialGroup = group;

		var prop = renderer.GetComponent<Prop>();
		if ( prop.IsValid() )
			prop.MaterialGroup = group;

		renderer.GameObject.Network?.Refresh();
	}

	[Rpc.Host]
	internal static void ChangeBodyGroup( ModelRenderer renderer, string partName, int choice )
	{
		if ( !renderer.IsValid() ) return;

		// several objects can be selected at once, skip the ones whose model doesn't have this choice
		var part = renderer.Model?.Parts.Get( partName );
		if ( part is null || choice < 0 || choice >= part.Choices.Count ) return;

		renderer.SetBodyGroup( part.Index, choice );

		var prop = renderer.GetComponent<Prop>();
		if ( prop.IsValid() )
			prop.BodyGroups = renderer.BodyGroups;

		renderer.GameObject.Network?.Refresh();
	}

	void IScenePhysicsEvents.OnOutOfBounds( Rigidbody body )
	{
		body.DestroyGameObject();
	}

	void ICleanupEvents.OnCleanup( int removedObjects, int restoredObjects )
	{
		// player corpses aren't networked, so the host's cleanup misses everyone else's
		foreach ( var corpse in Scene.GetAllComponents<DeathCameraTarget>().ToArray() )
			corpse.GameObject.Destroy();

		var phrase = restoredObjects > 0 ? "cleanup.restored" : "cleanup.removed";
		var message = Game.Language.GetPhrase( phrase, new()
		{
			{ "removed", removedObjects },
			{ "restored", restoredObjects }
		} );

		Notices.AddNotice( "cleaning_services", Color.Green, message );
	}
}
