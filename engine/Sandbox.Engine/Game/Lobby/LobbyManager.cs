using Sandbox.Engine;
using Sandbox.Internal;
using Steamworks;

namespace Sandbox;

interface ILobby
{
	ulong Id { get; }
	void OnMemberEnter( Friend friend );
	void OnMemberLeave( Friend friend );
	void OnMemberUpdated( Friend friend );
	void OnLobbyUpdated();
	void OnMemberMessage( Friend friend, ByteStream stream );
}


/// <summary>
/// A class to orchestrate lobbies, globally.
/// </summary>
internal static class LobbyManager
{
	static Logger log = new Logger( "LobbyManager" );

	/// <summary>
	/// A low level list of lobbies we're in
	/// </summary>
	public static HashSet<ulong> ActiveLobbies { get; } = new HashSet<ulong>();

	static List<WeakReference<ILobby>> _lobbies = new();

	internal static void Register( ILobby lobby )
	{
		_lobbies.Add( new WeakReference<ILobby>( lobby ) );
	}

	internal static void Unregister( ILobby lobby )
	{
		_lobbies.RemoveAll( x => !x.TryGetTarget( out var l ) || l == lobby );
	}

	internal static void OnDataUpdate( ulong lobbyid, ulong targetid )
	{
		if ( lobbyid == targetid )
		{
			foreach ( var lobby in EnumerateLobbies( lobbyid ) )
			{
				lobby.OnLobbyUpdated();
			}
		}
		else
		{
			foreach ( var lobby in EnumerateLobbies( lobbyid ) )
			{
				lobby.OnMemberUpdated( new Friend( targetid ) );
			}
		}
	}

	internal static unsafe void OnChatMessage( ulong lobbyid, ulong memberid, IntPtr v, int length )
	{
		using var data = ByteStream.CreateReader( (void*)v, length );

		foreach ( var lobby in EnumerateLobbies( lobbyid ) )
		{
			lobby.OnMemberMessage( new Friend( memberid ), data );
		}
	}

	internal static void OnEntered( ulong lobbyid )
	{
		ActiveLobbies.Add( lobbyid );
	}

	/// <summary>
	/// Note there's not a callback for this afaik, so we call this manually
	/// </summary>
	internal static void OnLeave( ulong lobbyid )
	{
		log.Trace( $"Left Lobby [{lobbyid}]" );
		ActiveLobbies.Remove( lobbyid );
		SteamMatchmaking.Internal.LeaveLobby( lobbyid );
	}

	internal static void OnCreated( ulong lobbyid )
	{
		// The lobby creation is done in the game contexts
		ActiveLobbies.Add( lobbyid );
	}

	internal static void OnMemberLeave( ulong lobbyid, ulong memberid )
	{
		foreach ( var lobby in EnumerateLobbies( lobbyid ) )
		{
			lobby.OnMemberLeave( new Friend( memberid ) );
		}
	}

	internal static void OnMemberEntered( ulong lobbyid, ulong memberid )
	{
		foreach ( var lobby in EnumerateLobbies( lobbyid ) )
		{
			lobby.OnMemberEnter( new Friend( memberid ) );
		}
	}

	internal static void OnLobbyInvite( ulong lobbyid, ulong memberid )
	{
		Log.Info( $"Got invite to lobby {lobbyid} from {memberid}" );

		if ( IMenuSystem.Current is null )
			return;

		var friend = new Friend( memberid );
		var partyRoom = new PartyRoom.Entry( new Steamworks.Data.Lobby( lobbyid ) );

		// TODO - store pending invites somewhere, or something?
		// What if they're in a game?

		using ( IMenuDll.Current?.PushScope() )
		{
			IMenuSystem.Current.Question( $"{friend.Name} invited you to a party!", "celebration", () => _ = partyRoom.Join(), null );
		}
	}

	static IEnumerable<ILobby> EnumerateLobbies( ulong id )
	{
		_lobbies.RemoveAll( x => !x.TryGetTarget( out var _ ) );

		return _lobbies.Select( x => x.TryGetTarget( out var l ) ? l : null ).Where( x => x is not null && x.Id == id ).ToArray();
	}
}
