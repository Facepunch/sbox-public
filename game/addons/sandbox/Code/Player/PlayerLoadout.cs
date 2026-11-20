/// <summary>
/// Remembers the weapons a player carries and, when the server has Re-equip Last on
/// (<see cref="ServerSettings.ReEquipLast"/>), gives them back on respawn instead of the starter items.
/// Lives on the Player GameObject alongside PlayerInventory.
/// Listens to inventory events to auto-save, and handles all loadout RPCs directly.
/// </summary>
public sealed class PlayerLoadout : Component, Local.IPlayerEvents, Global.IPlayerEvents, Global.ISaveEvents
{
	[RequireComponent] public Player Player { get; set; }
	[RequireComponent] public PlayerInventory Inventory { get; set; }

	private bool _isRestoringLoadout;

	/// <summary>
	/// One entry in a serialized loadout: the prefab resource path.
	/// </summary>
	public struct LoadoutEntry
	{
		public string PrefabPath { get; set; }
	}

	public string SerializeLoadout()
	{
		var entries = Inventory.Weapons
			.Where( w => !string.IsNullOrEmpty( w.GameObject.PrefabInstanceSource ) )
			.Select( w => new LoadoutEntry
			{
				PrefabPath = w.GameObject.PrefabInstanceSource
			} )
			.ToList();

		return entries.Count > 0 ? Json.Serialize( entries ) : null;
	}

	public void SaveLoadout()
	{
		if ( _isRestoringLoadout ) return;

		var json = SerializeLoadout();
		if ( string.IsNullOrEmpty( json ) ) return;

		if ( Player.IsLocalPlayer )
		{
			LocalData.Set( "hotbar", json );
		}
		else
		{
			PushLoadoutToClient( json );
		}
	}

	public void GiveLoadoutWeapons( string json )
	{
		var entries = Json.Deserialize<List<LoadoutEntry>>( json );
		if ( entries is null ) return;

		_isRestoringLoadout = true;
		try
		{
			foreach ( var entry in entries )
			{
				Inventory.Pickup( entry.PrefabPath, false );
			}
		}
		finally
		{
			_isRestoringLoadout = false;
		}
	}

	[Rpc.Owner]
	private void PushLoadoutToClient( string loadoutJson )
	{
		LocalData.Set( "hotbar", loadoutJson );
	}

	[Rpc.Owner]
	private void RequestClientLoadout()
	{
		var json = GetSpawnLoadout();
		if ( string.IsNullOrEmpty( json ) )
			HostGiveStarterItems();
		else
			HostRestoreLoadoutFromClient( json );
	}

	/// <summary>
	/// The saved hotbar to restore on spawn, or null for the starter items. Read on the owning client,
	/// where the hotbar is stored; the replicated server setting decides whether it's used.
	/// </summary>
	private static string GetSpawnLoadout()
		=> ServerSettings.ReEquipLast ? LocalData.Get<string>( "hotbar" ) : null;

	/// <summary>
	/// Clears the current inventory, waits a frame, then gives the loadout from JSON and equips the best weapon.
	/// A layout that gives back nothing falls back to the starter items.
	/// </summary>
	private async Task ReplaceLoadoutAsync( string json )
	{
		foreach ( var weapon in Inventory.Weapons.ToList() )
			weapon.DestroyGameObject();

		await Task.Yield();

		GiveLoadoutWeapons( json );

		GiveStarterItemsIfEmpty();

		var best = Inventory.GetBestWeapon();
		if ( best.IsValid() )
			Inventory.SwitchWeapon( best );
	}

	private void GiveStarterItemsIfEmpty()
	{
		if ( !Networking.IsHost || Inventory.Weapons.Any() ) return;

		var e = new PlayerLoadoutEvent { Player = Player, Items = Inventory.StartingItems?.ToList() ?? new() };
		Global.IPlayerEvents.Post( x => x.OnPlayerLoadout( e ) );
		if ( e.Cancelled ) return;

		// the engine gives the starting items, so hand it the edited list for this one call
		var startingItems = Inventory.StartingItems;
		Inventory.StartingItems = e.Items ?? new();

		try
		{
			Inventory.GiveLoadout();
		}
		finally
		{
			Inventory.StartingItems = startingItems;
		}
	}

	[Rpc.Host]
	private void HostGiveStarterItems()
	{
		if ( Rpc.Caller != Player.Network.Owner ) return;

		GiveStarterItemsIfEmpty();
	}

	[Rpc.Host]
	private async void HostRestoreLoadoutFromClient( string loadoutJson )
	{
		await ReplaceLoadoutAsync( loadoutJson );
	}

	void Global.IPlayerEvents.OnPlayerSpawned( Player player )
	{
		if ( player != Player ) return;
		if ( !Networking.IsHost ) return;

		_ = RestoreOnSpawnAsync();
	}

	private async Task RestoreOnSpawnAsync()
	{
		if ( !Player.IsLocalPlayer )
		{
			RequestClientLoadout();
			return;
		}

		var json = GetSpawnLoadout();
		if ( !string.IsNullOrEmpty( json ) )
		{
			await ReplaceLoadoutAsync( json );
			return;
		}

		GiveStarterItemsIfEmpty();

		var best = Inventory.GetBestWeapon();
		if ( best.IsValid() )
			Inventory.SwitchWeapon( best );
	}

	void Local.IPlayerEvents.OnDied( PlayerDiedParams args )
	{
		if ( !Networking.IsHost ) return;
		SaveLoadout();
	}

	void Local.IPlayerEvents.OnPickup( PlayerPickupEvent e )
	{
		if ( e.Cancelled ) return;
		if ( !Networking.IsHost ) return;
		SaveLoadout();
	}

	void Local.IPlayerEvents.OnDrop( PlayerDropEvent e )
	{
		if ( e.Cancelled ) return;
		if ( !Networking.IsHost ) return;
		_ = SaveLoadoutAfterYield();
	}

	void Local.IPlayerEvents.OnRemoveWeapon( PlayerRemoveWeaponEvent e )
	{
		if ( e.Cancelled ) return;
		if ( !Networking.IsHost ) return;
		_ = SaveLoadoutAfterYield();
	}

	void Local.IPlayerEvents.OnMoveSlot( PlayerMoveSlotEvent e )
	{
		if ( e.Cancelled ) return;
		if ( !Networking.IsHost ) return;
		SaveLoadout();
	}

	private async Task SaveLoadoutAfterYield()
	{
		await Task.Yield();
		SaveLoadout();
	}

	void Global.ISaveEvents.BeforeSave( string filename )
	{
		if ( !Networking.IsHost ) return;

		var steamId = (long)(Player.Network.Owner?.SteamId ?? default);
		if ( steamId == 0 ) return;

		var json = SerializeLoadout();
		if ( string.IsNullOrEmpty( json ) ) return;

		SaveSystem.Current?.SetMetadata( $"Loadout_{steamId}", json );
	}

	void Global.ISaveEvents.AfterLoad( string filename )
	{
		if ( !Networking.IsHost ) return;

		var steamId = (long)(Player.Network.Owner?.SteamId ?? default);
		if ( steamId == 0 ) return;

		var json = SaveSystem.Current?.GetMetadata( $"Loadout_{steamId}" );
		if ( string.IsNullOrEmpty( json ) ) return;

		_ = RestoreLoadoutFromSaveAsync( json );
	}

	private async Task RestoreLoadoutFromSaveAsync( string json )
	{
		await ReplaceLoadoutAsync( json );
	}
}
