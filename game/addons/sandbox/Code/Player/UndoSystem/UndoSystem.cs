using Sandbox.UI;
using System.Collections.Generic;
using System.Linq;

internal class UndoSystem : GameObjectSystem<UndoSystem>
{
	public sealed class HistoryItem
	{
		public Guid Id { get; set; }
		public string Name { get; set; }

		/// <summary>
		/// Named parameters for the action title, resolved by the player viewing the history.
		/// </summary>
		public Dictionary<string, string> NameTokens { get; set; }

		public string Icon { get; set; }
		public int ObjectCount { get; set; }
	}

	/// <summary>
	/// Raised on the requesting client when the host sends the latest undo history.
	/// </summary>
	public static event Action<IReadOnlyList<HistoryItem>> HistoryUpdated;

	Dictionary<long, PlayerStack> stacks = new();

	public UndoSystem( Scene scene ) : base( scene )
	{
	}

	/// <summary>
	/// Get the undo stack for a specific SteamId
	/// </summary>
	public PlayerStack For( long steamId )
	{
		if ( !stacks.TryGetValue( steamId, out var stack ) )
		{
			stack = new PlayerStack( steamId );
			stacks[steamId] = stack;
		}
		return stack;
	}

	/// <summary>
	/// Call this when a player disconnects to prevent memory leaks!
	/// </summary>
	public void RemovePlayer( long steamId )
	{
		stacks.Remove( steamId );
	}

	/// <summary>
	/// Remove a GameObject from all player undo stacks.
	/// </summary>
	public void Remove( GameObject go )
	{
		foreach ( var stack in stacks.Values )
		{
			stack.Remove( go );
		}
	}

	/// <summary>
	/// Ask the host for the local player's current undo history.
	/// </summary>
	[Rpc.Host]
	public static void RequestHistory()
	{
		if ( Rpc.Caller is not { } caller ) return;

		SendHistory( caller );
	}

	/// <summary>
	/// Undo one specific history entry. The caller can only address their own stack.
	/// </summary>
	[Rpc.Host]
	public static void UndoEntry( Guid id )
	{
		if ( Rpc.Caller is not { } caller ) return;

		Current?.For( caller.SteamId ).Undo( id );
		SendHistory( caller );
	}

	private static void SendHistory( Connection target )
	{
		if ( !Networking.IsHost || target is null ) return;

		var history = Current?.For( target.SteamId ).GetHistory() ?? [];
		var json = Sandbox.Json.Serialize( history );

		using ( Rpc.FilterInclude( target ) )
		{
			ReceiveHistory( json );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private static void ReceiveHistory( string json )
	{
		var history = Sandbox.Json.Deserialize<List<HistoryItem>>( json ) ?? [];
		HistoryUpdated?.Invoke( history );
	}

	/// <summary>
	/// Per-player undo stack
	/// </summary>
	public class PlayerStack
	{
		long steamId;
		List<Entry> entries = new();
		const int MaxUndoSteps = 128; // Bounded history prevents indefinite memory leaks

		public PlayerStack( long steamId )
		{
			this.steamId = steamId;
		}

		/// <summary>
		/// Create a new undo entry
		/// </summary>
		public Entry Create()
		{
			var entry = new Entry( steamId );
			entries.Add( entry );

			if ( entries.Count > MaxUndoSteps )
			{
				entries.RemoveAt( 0 );
			}

			return entry;
		}

		/// <summary>
		/// Is this entry still in the history? False once it has been undone, evicted, or pruned.
		/// </summary>
		public bool Contains( Entry entry ) => entry is not null && entries.Contains( entry );

		/// <summary>
		/// Run the undo
		/// </summary>
		public void Undo()
		{
			while ( entries.Count > 0 )
			{
				var entry = entries[^1];
				if ( !entry.AllowUndo() ) break;

				entries.RemoveAt( entries.Count - 1 );

				if ( entry.Run() )
					break;
			}

			var target = Connection.All.FirstOrDefault( x => x.SteamId == steamId );
			if ( target is not null )
				SendHistory( target );
		}

		/// <summary>
		/// Run and remove one specific entry from this player's history.
		/// </summary>
		public void Undo( Guid id )
		{
			var index = entries.FindIndex( x => x.Id == id );
			if ( index < 0 ) return;

			var entry = entries[index];
			if ( !entry.AllowUndo() ) return;

			entries.RemoveAt( index );
			entry.Run();
		}

		/// <summary>
		/// Return newest-first history, pruning entries whose objects no longer exist.
		/// </summary>
		public List<HistoryItem> GetHistory()
		{
			entries.RemoveAll( x => !x.HasObjects );

			return entries
				.AsEnumerable()
				.Reverse()
				.Select( x => new HistoryItem
				{
					Id = x.Id,
					Name = x.Name,
					NameTokens = x.NameTokens,
					Icon = x.Icon,
					ObjectCount = x.ObjectCount
				} )
				.ToList();
		}

		/// <summary>
		/// Remove a GameObject from all entries in this stack.
		/// </summary>
		public void Remove( GameObject go )
		{
			foreach ( var entry in entries )
				entry.Remove( go );
		}
	}

	/// <summary>
	/// An undo entry
	/// </summary>
	public class Entry
	{
		public Guid Id { get; } = Guid.NewGuid();

		/// <summary>
		/// The action title, optionally a hash-prefixed phrase resolved by the receiving player.
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Named parameters for a localized action title. Keep phrase references unresolved here.
		/// </summary>
		public Dictionary<string, string> NameTokens { get; set; }

		public string Icon { get; set; }

		long SteamId;

		HashSet<GameObject> gameObjects = new();

		public bool HasObjects => gameObjects.Any( x => x.IsValid() );
		public int ObjectCount => gameObjects.Count( x => x.IsValid() );

		internal Entry( long steamId )
		{
			SteamId = steamId;
		}

		/// <summary>
		/// Add a GameObject that should be destroyed when the undo is undone
		/// </summary>
		public void Add( GameObject go ) => Add( [go] );

		/// <summary>
		/// Add a collection of GameObjects that should be destroyed when the undo is undone.
		/// The owner is sent their updated history, so add everything in one call.
		/// </summary>
		public void Add( params IEnumerable<GameObject> gos )
		{
			foreach ( var go in gos )
			{
				gameObjects.Add( go );
			}

			var target = Connection.All.FirstOrDefault( x => x.SteamId == SteamId );
			if ( target is not null )
				SendHistory( target );
		}

		/// <summary>
		/// Remove a GameObject from this entry so it will no longer be destroyed on undo.
		/// </summary>
		public void Remove( GameObject go )
		{
			gameObjects.Remove( go );
		}

		UndoEvent CreateEvent() => new()
		{
			Client = Connection.All.FirstOrDefault( x => x.SteamId == SteamId ),
			Name = Name,
			Objects = gameObjects.Where( x => x.IsValid() ).ToList()
		};

		/// <summary>
		/// Ask <see cref="Global.IUndoEvents.OnUndo"/> listeners whether this can be undone. Entries with nothing
		/// left to remove are always allowed, they're just skipped.
		/// </summary>
		internal bool AllowUndo()
		{
			if ( !HasObjects ) return true;

			var e = CreateEvent();
			Game.ActiveScene?.RunEvent<Global.IUndoEvents>( x => x.OnUndo( e ) );
			return !e.Cancelled;
		}

		/// <summary>
		/// Run this undo
		/// </summary>
		public bool Run( bool sendNotice = true )
		{
			var e = CreateEvent();
			var actioned = false;

			foreach ( var go in gameObjects )
			{
				if ( go.IsValid() )
				{
					go.Destroy();
					actioned = true;
				}
			}

			if ( !actioned )
				return false;

			Game.ActiveScene?.RunEvent<Global.IUndoEvents>( x => x.OnUndone( e ) );

			if ( sendNotice )
			{
				var c = Connection.All.FirstOrDefault( x => x.SteamId == SteamId );
				if ( c is not null )
				{
					using ( Rpc.FilterInclude( c ) )
					{
						UndoNotice( Name, Sandbox.Json.Serialize( NameTokens ) );
					}
				}
			}

			return true;
		}

		/// <summary>
		/// Displays the undone action using the receiving player's language.
		/// </summary>
		[Rpc.Broadcast]
		public static void UndoNotice( string title, string tokensJson = null )
		{
			var tokens = string.IsNullOrEmpty( tokensJson ) ? null : Sandbox.Json.Deserialize<Dictionary<string, string>>( tokensJson );
			var action = LocalizedText.Resolve( title, tokens );
			var message = string.IsNullOrWhiteSpace( action )
				? Game.Language.GetPhrase( "spawnmenu.utility.undo" )
				: Game.Language.GetPhrase( "undo.notice", new() { { "name", action } } );

			Notices.AddNotice( "cached", "#3273eb", message, 5 );
			Sound.Play( "sounds/ui/ui.undo.sound" );
		}
	}
}
