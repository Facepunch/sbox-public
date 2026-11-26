using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// Data passed to <see cref="ICleanupEvents.OnCleaningUp"/>. Take objects out of <see cref="Objects"/> to keep them,
/// or set <see cref="Cancelled"/> to true to stop the cleanup.
/// </summary>
public class CleanupEvent
{
	/// <summary>
	/// The client whose objects are being cleaned up, or null for the whole map.
	/// </summary>
	public Connection Client { get; init; }

	/// <summary>
	/// The objects about to be removed.
	/// </summary>
	public List<GameObject> Objects { get; init; }

	public bool Cancelled { get; set; }
}

/// <summary>
/// Implement this on a <see cref="Component"/> to hear about cleanups.
/// </summary>
public interface ICleanupEvents : ISceneEvent<ICleanupEvents>
{
	/// <summary>
	/// Called on the host before the map, or one player's objects, are cleaned up.
	/// </summary>
	public void OnCleaningUp( CleanupEvent e ) { }

	/// <summary>
	/// Called on every machine after the host has cleaned up the map.
	/// </summary>
	public void OnCleanup( int removedObjects, int restoredObjects ) { }
}

/// <summary>
/// A system that tracks the baseline scene state and allows resetting the map to its original state.
/// Removes all spawned props and restores destroyed map objects while leaving players untouched.
/// </summary>
internal sealed class CleanupSystem : GameObjectSystem<CleanupSystem>, ISceneLoadingEvents, ISceneStartup
{
	/// <summary>
	/// Set of GameObjects that existed in the original scene baseline.
	/// </summary>
	private readonly HashSet<Guid> _baselineObjectIds = new();

	/// <summary>
	/// Serialized data of baseline objects so we can restore them if destroyed.
	/// </summary>
	private readonly Dictionary<Guid, string> _baselineObjectData = new();

	private static bool _restorePersistedBaseline;
	private static HashSet<Guid> _persistedBaselineIds;
	private static Dictionary<Guid, string> _persistedBaselineData;

	/// <summary>
	/// Whether a baseline has been captured.
	/// </summary>
	public bool HasBaseline => _baselineObjectIds.Count > 0;

	public CleanupSystem( Scene scene ) : base( scene )
	{
	}

	/// <summary>
	/// Call from SaveSystem before Game.ChangeScene() to snapshot the current baseline
	/// </summary>
	public static void PreserveBaselineForSaveLoad()
	{
		if ( Current is null || !Current.HasBaseline ) return;

		_restorePersistedBaseline = true;
		_persistedBaselineIds = new HashSet<Guid>( Current._baselineObjectIds );
		_persistedBaselineData = new Dictionary<Guid, string>( Current._baselineObjectData );
	}

	void ISceneLoadingEvents.BeforeLoad( Scene scene, SceneLoadOptions options )
	{
		// Clear any existing baseline when a new scene is loading
		_baselineObjectIds.Clear();
		_baselineObjectData.Clear();
	}

	async Task ISceneLoadingEvents.OnLoad( Scene scene, SceneLoadOptions options, LoadingContext context )
	{
		// Not the editor's own scene. Not Game.IsPlaying either - a game only sets that once its startup scene's loaded
		if ( scene.IsEditor ) return;

		// Wait for the scene's objects, and any map it loads, so they're all part of the baseline
		if ( !await WaitForMaps() ) return;

		// When loading a save, restore the baseline captured before the scene was destroyed
		if ( _restorePersistedBaseline && _persistedBaselineIds is not null )
		{
			_baselineObjectIds.UnionWith( _persistedBaselineIds );
			foreach ( var kvp in _persistedBaselineData )
				_baselineObjectData.TryAdd( kvp.Key, kvp.Value );

			_restorePersistedBaseline = false;
			Log.Info( $"CleanupSystem: Restored persisted baseline with {_baselineObjectIds.Count} objects." );
		}
		else
		{
			CaptureBaseline();
		}
	}

	/// <summary>
	/// Clients take their own baseline once they've got the host's scene, so they can clear what only they spawned.
	/// </summary>
	void ISceneStartup.OnClientInitialize()
	{
		// After the scene's loading has finished, maps and all
		if ( Networking.IsHost ) return;

		CaptureBaseline( false );
	}

	/// <summary>
	/// Waits a frame, then for the scene's maps to finish loading, or failing to - their objects come after the
	/// scene's. False if the scene's gone.
	/// </summary>
	async Task<bool> WaitForMaps()
	{
		await Task.Yield();

		// While a map's loading - flagged for the whole of it, loaded or failed, however long its download takes
		while ( Scene.IsValid() && Scene.GetAllComponents<MapInstance>().Any( x => x.GameObject.Flags.Contains( GameObjectFlags.Loading ) ) )
			await Task.Delay( 100 );

		return Scene.IsValid();
	}

	/// <summary>
	/// Captures the current scene state as the baseline.
	/// All objects that exist at this point are considered part of the original map.
	/// </summary>
	public void CaptureBaseline( bool restorable = true )
	{
		_baselineObjectIds.Clear();
		_baselineObjectData.Clear();

		foreach ( var go in Scene.Children?.ToArray() ?? [] )
		{
			CaptureObjectRecursive( go, restorable );
		}

		Log.Info( $"CleanupSystem: Captured baseline with {_baselineObjectIds.Count} objects." );
	}

	private void CaptureObjectRecursive( GameObject go, bool restorable )
	{
		if ( !go.IsValid() )
			return;

		// Skip player objects
		if ( IsPlayerObject( go ) )
			return;

		if ( go.Flags.Contains( GameObjectFlags.DontDestroyOnLoad ) )
			return;

		_baselineObjectIds.Add( go.Id );

		var serialized = restorable ? go.Serialize() : null;
		if ( serialized is not null )
		{
			_baselineObjectData[go.Id] = serialized.ToJsonString();
		}

		foreach ( var child in go.Children?.ToArray() ?? [] )
		{
			CaptureObjectRecursive( child, restorable );
		}
	}

	/// <summary>
	/// Determines if a GameObject is a player or belongs to a player.
	/// </summary>
	private static bool IsPlayerObject( GameObject go )
	{
		if ( !go.IsValid() )
			return false;

		if ( go.Components.Get<Player>( true ) is not null )
			return true;

		if ( go.Components.Get<PlayerData>( true ) is not null )
			return true;

		var parent = go.Parent;
		while ( parent is not null && parent != go.Scene )
		{
			if ( parent.Components.Get<Player>( true ) is not null )
				return true;
			if ( parent.Components.Get<PlayerData>( true ) is not null )
				return true;
			parent = parent.Parent;
		}

		return false;
	}

	/// <summary>
	/// Cleans up the scene by removing all spawned objects and restoring destroyed baseline objects.
	/// Players and their belongings are preserved.
	/// </summary>
	public void Cleanup()
	{
		if ( !HasBaseline )
		{
			Log.Warning( "CleanupSystem: No baseline captured. Cannot cleanup." );
			return;
		}

		if ( !Networking.IsHost )
		{
			Log.Warning( "CleanupSystem: Only the host can perform cleanup." );
			return;
		}

		var removedCount = 0;
		var restoredCount = 0;
		var objectsToRemove = new List<GameObject>();
		var existingBaselineIds = new HashSet<Guid>();

		foreach ( var go in Scene.GetAllObjects( true ) )
		{
			if ( !go.IsValid() )
				continue;

			// Never remove player objects
			if ( IsPlayerObject( go ) )
				continue;

			if ( go.Flags.Contains( GameObjectFlags.DontDestroyOnLoad ) )
				continue;

			if ( _baselineObjectIds.Contains( go.Id ) )
			{
				existingBaselineIds.Add( go.Id );
			}
			else if ( IsSpawned( go ) )
			{
				objectsToRemove.Add( go );
			}
		}

		var e = new CleanupEvent { Objects = objectsToRemove };
		Scene.RunEvent<ICleanupEvents>( x => x.OnCleaningUp( e ) );
		if ( e.Cancelled ) return;

		// Remove spawned objects
		foreach ( var go in objectsToRemove )
		{
			if ( go.IsValid() )
			{
				go.Destroy();
				removedCount++;
			}
		}

		// Restore destroyed baseline objects
		foreach ( var kvp in _baselineObjectData )
		{
			var id = kvp.Key;

			// Skip if the object still exists
			if ( existingBaselineIds.Contains( id ) )
				continue;

			// Skip if we already processed the parent object
			var go = Scene.Directory.FindByGuid( id );
			if ( go.IsValid() )
				continue;

			try
			{
				var json = System.Text.Json.Nodes.JsonNode.Parse( kvp.Value );
				if ( json is System.Text.Json.Nodes.JsonObject jso )
				{
					var restored = new GameObject();
					restored.Deserialize( jso );
					restoredCount++;
				}
			}
			catch ( System.Exception ex )
			{
				Log.Warning( $"CleanupSystem: Failed to restore object {id}: {ex.Message}" );
			}
		}

		BroadcastCleanup( removedCount, restoredCount );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private static void BroadcastCleanup( int removedObjects, int restoredObjects )
	{
		// The host's own went in Cleanup, minus any OnCleaningUp kept
		if ( !Networking.IsHost ) Current?.RemoveLocalLeftovers();

		Game.ActiveScene?.RunEvent<ICleanupEvents>( x => x.OnCleanup( removedObjects, restoredObjects ) );

		Log.Info( $"Cleanup complete. Removed {removedObjects} spawned objects, restored {restoredObjects} destroyed objects." );
	}

	/// <summary>
	/// A client's own spawned objects - gibs, impacts, effects - which the host's cleanup can't reach. Networked ones
	/// are the host's to remove.
	/// </summary>
	private void RemoveLocalLeftovers()
	{
		if ( !HasBaseline ) return;

		foreach ( var go in Scene.GetAllObjects( true ).ToArray() )
		{
			if ( !go.IsValid() || go.IsNetworkRoot || !IsSpawned( go ) ) continue;

			go.Destroy();
		}
	}

	/// <summary>
	/// Whether this was spawned since the baseline, and is the top of what was - on its own, or stuck to the map like
	/// an impact. Not players' things, nor what the engine makes under objects (bones, attachments, hidden helpers).
	/// </summary>
	private bool IsSpawned( GameObject go )
	{
		// The scene's own in the list, with no parent
		if ( go.Parent is not { } parent || _baselineObjectIds.Contains( go.Id ) ) return false;
		if ( parent != Scene && !_baselineObjectIds.Contains( parent.Id ) ) return false;
		if ( go.Flags.Contains( GameObjectFlags.DontDestroyOnLoad ) ) return false;
		if ( go.Flags.Contains( GameObjectFlags.Bone ) || go.Flags.Contains( GameObjectFlags.Attachment ) || go.Flags.Contains( GameObjectFlags.Hidden ) ) return false;

		return !IsPlayerObject( go );
	}

	/// <summary>
	/// Console command to cleanup the map.
	/// </summary>
	[ConCmd( "cleanup" )]
	public static void CleanupCommand( string targetName = null )
	{
		if ( !Networking.IsHost ) return;

		//
		// Targeted cleanup, doesn't use the same cleanup shit
		//
		if ( !string.IsNullOrEmpty( targetName ) )
		{
			var target = GameManager.FindPlayerWithName( targetName );
			if ( target is not null )
			{
				CleanupPlayer( target );
			}
			else
			{
				Notices.AddNotice( "cleaning_services", Color.Red, Game.Language.GetPhrase( "cleanup.player_not_found", new() { { "name", targetName } } ) );
			}

			return;
		}

		if ( Current is null )
		{
			Log.Warning( "CleanupSystem: No active cleanup system." );
			return;
		}

		Current.Cleanup();
	}

	[Rpc.Host]
	public static void RpcCleanUpMine()
	{
		CleanupPlayer( Rpc.Caller );
	}

	[Rpc.Host]
	public static void RpcCleanUpAll()
	{
		if ( !Permissions.Has( Rpc.Caller, Permissions.Admin ) ) return;

		Current?.Cleanup();
	}

	[Rpc.Host]
	public static void RpcCleanUpTarget( Connection target )
	{
		if ( !Permissions.Has( Rpc.Caller, Permissions.Admin ) ) return;

		CleanupPlayer( target );
	}

	public static void CleanupPlayer( Connection caller )
	{
		Assert.True( Networking.IsHost, "Only the host may call this method!" );

		var removable = Game.ActiveScene.GetAllComponents<Ownable>()
			.Where( o => o.Owner == caller )
			.Select( o => o.GameObject )
			.ToList();

		var e = new CleanupEvent { Client = caller, Objects = removable };
		Game.ActiveScene.RunEvent<ICleanupEvents>( x => x.OnCleaningUp( e ) );
		if ( e.Cancelled ) return;

		var count = 0;
		foreach ( var go in removable )
		{
			if ( !go.IsValid() ) continue;

			go.Destroy();
			count++;
		}

		Notices.SendLocalizedNotice( caller, "cleaning_services", Color.Green, "#cleanup.player", new() { { "count", count.ToString() } } );
	}

}
