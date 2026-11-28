using Sandbox.Engine;
using Sandbox.Modals;
using Sandbox.Network;
using Sandbox.SteamApi;
using System.Globalization;

namespace Sandbox;

/// <summary>
/// A Party. A Party with your friends.
/// </summary>
public partial class PartyRoom : ILobby
{
	/// <summary>
	/// The party we currently belong to, if any.
	/// </summary>
	public static PartyRoom Current { get; private set; }

	Sandbox.SteamApi.Lobby steamLobby;

	/// <summary>
	/// The unique identifier of this party.
	/// </summary>
	public SteamId Id => steamLobby.Id;

	/// <summary>
	/// The server this party is playing on, if any. This is set by the owner of the party, and read by clients to know where to connect to.
	/// </summary>
	internal string GameAddress => steamLobby.GetData( "gameaddress" );

	/// <summary>
	/// The party name, filtered using the local player's chat filter settings. Only the leader can change it.
	/// </summary>
	public string Name
	{
		get => GetDisplayName( steamLobby );
		set
		{
			if ( !Owner.IsMe )
			{
				throw new InvalidOperationException( "Only the party leader can rename the party." );
			}

			value = value?.Trim();
			if ( string.IsNullOrWhiteSpace( value ) || value.Length > MaxNameLength || value.Any( char.IsControl ) )
			{
				throw new ArgumentException( $"Choose a party name between 1 and {MaxNameLength} characters, on a single line." );
			}

			if ( steamLobby.GetData( "name" ) == value ) return;

			if ( !steamLobby.SetData( "name", value ) )
			{
				throw new InvalidOperationException( "Could not rename the party. Please try again." );
			}
		}
	}

	/// <summary>
	/// The maximum length of a custom party name.
	/// </summary>
	public const int MaxNameLength = 64;

	/// <summary>
	/// Whether this party is discoverable and joinable by everyone.
	/// </summary>
	public bool IsPublic => steamLobby.GetData( "public" ) == "1";

	static string GetDisplayName( Lobby lobby )
	{
		var name = Utility.Steam.FilterChat( lobby.GetData( "name" ), lobby.Owner.Id );
		return string.IsNullOrWhiteSpace( name ) ? "Party" : name;
	}

	/// <summary>
	/// The maximum number of members allowed in this party.
	/// </summary>
	public int MaxMembers
	{
		get => steamLobby.MaxMembers;
		set
		{
			if ( Current != this || !Owner.IsMe )
			{
				throw new InvalidOperationException( "Only the party leader can change the party size." );
			}

			if ( value < Math.Max( 1, MemberCount ) || value > 32 )
			{
				throw new ArgumentOutOfRangeException( nameof( value ), "The party size must fit its current members and cannot exceed 32." );
			}

			steamLobby.MaxMembers = value;
		}
	}

	/// <summary>
	/// The current number of members in this party.
	/// </summary>
	public int MemberCount => steamLobby.MemberCount;

	PartyRoom( Lobby value )
	{
		steamLobby = value;
		UpdateLobbyData();

		if ( Owner.IsMe )
		{
			steamLobby.SetData( "lobby_type", "party" );
			steamLobby.SetData( "api", Protocol.Api.ToString() );
			steamLobby.SetData( "protocol", Protocol.Network.ToString() );
			steamLobby.SetData( "buildid", $"{Application.Version}" );
			steamLobby.SetData( "dev", Application.IsEditor ? "1" : "0" );
			steamLobby.SetData( "_ownerid", Owner.Id.ToString() );
		}

		LobbyManager.Register( this );
	}

	/// <summary>
	/// Leave this party and stop following its leader.
	/// </summary>
	public void Leave()
	{
		_join?.Dispose();
		_join = null;

		RemoveVoiceSpeakers();

		using ( GlobalContext.MenuScope() )
		{
			Event.EventSystem.RunInterface<IEventListener>( x => x.OnLeftParty( Current ) );
		}

		steamLobby.Leave();
		steamLobby = default;

		if ( Current == this )
		{
			Current = default;
		}

		// this is no longer needed, we don't need to get messages about it
		// we don't need to know when people enter and leave, so forget about it.
		LobbyManager.Unregister( this );
	}

	/// <summary>
	/// Set the owner to someone else. You need to be the owner
	/// </summary>
	public bool SetOwner( SteamId friend )
	{
		return steamLobby.SetOwner( friend );
	}


	internal void InviteFriend( SteamId steamid )
	{
		if ( steamLobby.InviteFriend( steamid.ValueUnsigned ) )
		{
			Log.Info( $"Party invite to {steamid} sent" );

			// They'll follow us into the game we're in, private or not
			Networking.AllowJoin( steamid );
		}
		else
		{
			Log.Warning( $"Party invite to {steamid} was not sent" );
		}
	}

	internal void InviteFriend()
	{
		steamLobby.InviteOverlay();
	}

	RealTimeSince timeSinceUpdate = 0;

	/// <summary>
	/// Determine our state to be sent to other party members
	/// </summary>
	OwnerJoinState DetermineJoinState()
	{
		// Still connecting until the host will let the party in, or they'd race our invites and be kicked from a private game
		var connecting = Networking.IsConnecting || !(Networking.System?.IsReadyForParty ?? true);

		return DetermineJoinState( IGameInstance.Current?.IsLoading ?? LoadingScreen.IsVisible,
			connecting, GetGameAddress(), Application.GamePackage is not null && Application.GamePackage is not LocalPackage );
	}

	internal static OwnerJoinState DetermineJoinState( bool loading, bool connecting, string address, bool hasGame )
	{
		if ( loading || connecting ) return OwnerJoinState.Loading;
		if ( !string.IsNullOrWhiteSpace( address ) ) return OwnerJoinState.Ready;
		return hasGame ? OwnerJoinState.Unavailable : OwnerJoinState.None;
	}

	static string GetGameAddress()
	{
		if ( Networking.System?.Sockets.OfType<SteamLobbySocket>().FirstOrDefault() is { } lobby )
			return lobby.LobbySteamId.ToString();
		if ( Networking.IsClient && Networking.LastConnectionString != "local" )
			return Networking.LastConnectionString;
		return null;
	}

	internal void Tick()
	{
		TickVoice();

		if ( timeSinceUpdate < 0.5f ) return;
		timeSinceUpdate = 0;

		ShareMemberStatus();

		if ( Owner.IsMe )
		{
			steamLobby.SetData( "api", Protocol.Api.ToString() );
			_join?.Dispose();
			_join = null;
			steamLobby.SetData( "protocol", Protocol.Network.ToString() );
			steamLobby.SetData( "buildid", $"{Application.Version}" );
			steamLobby.SetData( "dev", Application.IsEditor ? "1" : "0" );
			steamLobby.SetData( "_ownerid", Owner.Id.ToString() );
			steamLobby.SetData( "package", Application.GamePackage?.GetVersionedIdent() );
			steamLobby.SetData( "packagetitle", Application.GamePackage?.Title );

			var state = DetermineJoinState();
			steamLobby.SetData( "joinstate", state.ToString() );
			var progress = state == OwnerJoinState.Loading ? LoadingScreen.Progress : null;
			steamLobby.SetData( "download_fraction", progress?.Fraction.ToString( "F4", CultureInfo.InvariantCulture ) ?? "" );
			steamLobby.SetData( "download_title", progress?.Title ?? "" );

			if ( state is OwnerJoinState.Ready )
			{
				steamLobby.SetData( "gameaddress", GetGameAddress() );
			}
			else
			{
				steamLobby.DeleteData( "gameaddress" );
			}
		}
		else
		{
			UpdateFollowing();
		}
	}

	/// <summary>
	/// Create an open party with the local player as its leader.
	/// </summary>
	[Obsolete]
	public static Task<PartyRoom> Create( int maxMembers )
	{
		return Create( maxMembers, $"{Utility.Steam.PersonaName}'s Party", true );
	}

	/// <summary>
	/// Create a party, optionally making it discoverable and joinable by everyone.
	/// </summary>
	public static async Task<PartyRoom> Create( int maxMembers, string name, bool ispublic )
	{
		var lobby = await Sandbox.SteamApi.SteamMatchmaking.CreateLobbyAsync( ispublic ? LobbyType.Public : LobbyType.Private, maxMembers );

		if ( !lobby.HasValue )
		{
			Log.Warning( "Failed to create party" );
			return null;
		}

		lobby.Value.SetData( "name", name );
		lobby.Value.SetData( "public", ispublic ? "1" : "0" );

		var party = new PartyRoom( lobby.Value );

		Current = party;

		return party;
	}

	/// <summary>
	/// Make this party discoverable and joinable by everyone. Only the leader can do this.
	/// </summary>
	public void MakePublic()
	{
		SetPublic( true );
	}

	/// <summary>
	/// Switch between a public party and an invitation-only party. Only the leader can do this.
	/// </summary>
	public void SetPublic( bool isPublic )
	{
		if ( Current != this || !Owner.IsMe )
		{
			throw new InvalidOperationException( "Only the party leader can change party access." );
		}

		if ( !(isPublic ? steamLobby.SetPublic() : steamLobby.SetPrivate()) )
		{
			throw new InvalidOperationException( "Could not change party access. Please try again." );
		}

		steamLobby.SetData( "public", isPublic ? "1" : "0" );
	}

	/// <summary>
	/// Join a party by its identifier, checking that it still exists and is a party.
	/// </summary>
	public static async Task<bool> Join( ulong id )
	{
		if ( Current?.Id.ValueUnsigned == id ) return true;

		var party = new Lobby( id );
		if ( !await party.Refresh() || !party.IsParty ) return false;

		return await Join( party );
	}

	internal static async Task<bool> Join( Lobby lobby )
	{
		Current?.Leave();

		if ( Application.IsEditor )
			return false;

		var result = await lobby.Join();
		if ( result != RoomEnter.Success )
		{
			switch ( result )
			{
				case RoomEnter.DoesntExist:
					IModalSystem.Current?.Notice( "Joining failed", "The party doesn't exist anymore.", "heart_broken" );
					break;
				case RoomEnter.Full:
					IModalSystem.Current?.Notice( "Joining failed", "The party is full.", "heart_broken" );
					break;
				default:
					IModalSystem.Current?.Notice( "Joining failed", $"Failed to join the party: {result}.", "heart_broken" );
					break;
			}

			Log.Warning( $"Failed to join party ({result})" );
			return false;
		}

		Current?.Leave();
		Current = new PartyRoom( lobby );

		using ( GlobalContext.MenuScope() )
		{
			Event.EventSystem.RunInterface<IEventListener>( x => x.OnJoinedParty( Current ) );
		}

		return true;
	}


	/// <summary>
	/// A list of members in this party.
	/// </summary>
	public IEnumerable<Friend> Members => steamLobby.Members;

	/// <summary>
	/// The current party leader.
	/// </summary>
	public Friend Owner { get; private set; }

	/// <summary>
	/// Find open parties with space for another member.
	/// </summary>
	public static async Task<Entry[]> Find()
	{
		var found = await Sandbox.SteamApi.SteamMatchmaking.LobbyList
			.WithMaxResults( 1000 )
			.WithKeyValue( "lobby_type", "party" )
			.WithSlotsAvailable( 1 )
			.RequestAsync( default );

		if ( found is null )
			return Array.Empty<Entry>();

		return found.Select( x => new Entry( x ) ).ToArray();
	}

	ulong ILobby.Id => steamLobby.Id;

	/// <summary>
	/// What package is this party's owner playing?
	/// </summary>
	public string PackageIdent => steamLobby.GetData( "package" );

	/// <summary>
	/// The game the party intends to play, without starting downloads or following the leader.
	/// </summary>
	public string SelectedGameIdent => steamLobby.GetData( "selected_game" );

	/// <summary>
	/// The title of the game the party intends to play.
	/// </summary>
	public string SelectedGameTitle => steamLobby.GetData( "selected_game_title" );

	/// <summary>
	/// Whether this package matches the chosen game, including its revision when one was selected.
	/// </summary>
	public bool IsGameSelected( Package package ) => package is not null && (SelectedGameIdent == package.GetVersionedIdent() || SelectedGameIdent == package.FullIdent);

	/// <summary>
	/// Publish the leader's choice of game. Pass null to clear the choice.
	/// </summary>
	public void SelectGame( Package package )
	{
		if ( Current != this || !Owner.IsMe )
		{
			throw new InvalidOperationException( "Only the party leader can choose the game." );
		}

		steamLobby.SetData( "selected_game", package?.GetVersionedIdent() ?? "" );
		steamLobby.SetData( "selected_game_title", package?.Title ?? "" );
	}

	/// <summary>
	/// Refresh a party's details without joining it. Returns null if it is no longer available.
	/// </summary>
	public static async Task<Entry?> GetEntryAsync( ulong id )
	{
		if ( id == 0 ) return null;

		var lobby = new Lobby( id );
		if ( !await lobby.Refresh() || !lobby.IsParty || lobby.MemberCount == 0 ) return null;

		return new Entry( lobby );
	}

	void ILobby.OnMemberEnter( Friend friend )
	{
		Log.Info( $"Party member entered {friend}" );
		OnJoin?.Invoke( friend );

		using ( GlobalContext.MenuScope() )
		{
			Event.EventSystem.RunInterface<IEventListener>( x => x.OnMemberJoin( friend ) );
		}
	}

	void ILobby.OnMemberLeave( Friend friend )
	{
		Log.Info( $"Party member leave {friend}" );
		OnLeave?.Invoke( friend );

		using ( GlobalContext.MenuScope() )
		{
			Event.EventSystem.RunInterface<IEventListener>( x => x.OnMemberLeave( friend ) );
		}
	}

	void ILobby.OnMemberUpdated( Friend friend )
	{
		// Members share their join progress through this every half second - trace, not info
		Log.Trace( $"Party member updated {friend}" );
	}

	void ILobby.OnLobbyUpdated()
	{
		UpdateLobbyData();
	}

	void UpdateLobbyData()
	{
		Owner = steamLobby.Owner;
	}


	/// <summary>
	/// A discoverable party with its current membership and game details.
	/// </summary>
	public struct Entry
	{
		private Lobby x;

		/// <summary>
		/// The unique identifier of this party.
		/// </summary>
		public readonly ulong Id => x.Id;

		/// <summary>
		/// The party name, filtered using the local player's chat filter settings.
		/// </summary>
		public readonly string Name => GetDisplayName( x );

		/// <summary>
		/// The number of members currently in the party.
		/// </summary>
		public readonly int Members => x.MemberCount;

		/// <summary>
		/// The maximum number of members allowed in the party.
		/// </summary>
		public readonly int MaxMembers => x.MaxMembers;

		/// <summary>
		/// Whether the party has no space for another member.
		/// </summary>
		public readonly bool IsFull => x.MemberCount >= x.MaxMembers;

		/// <summary>
		/// The Steam identifier of the party leader.
		/// </summary>
		public readonly long OwnerId => x.GetData( "_ownerid" ).ToLong( 0 );

		/// <summary>
		/// Whether the party leader has a game server to follow.
		/// </summary>
		public readonly bool IsPlaying => !string.IsNullOrWhiteSpace( x.GetData( "gameaddress" ) );

		/// <summary>
		/// The package the party leader is playing, or has chosen to play next.
		/// </summary>
		public readonly string Package => string.IsNullOrWhiteSpace( x.GetData( "package" ) )
			? x.GetData( "selected_game" ) : x.GetData( "package" );

		/// <summary>
		/// The title of the game the party leader is playing, or has chosen to play next.
		/// </summary>
		public readonly string GameTitle => string.IsNullOrWhiteSpace( x.GetData( "package" ) )
			? x.GetData( "selected_game_title" ) : x.GetData( "packagetitle" );

		internal Entry( Lobby x )
		{
			this.x = x;
		}

		/// <summary>
		/// Join this party.
		/// </summary>
		public async Task Join()
		{
			await PartyRoom.Join( x );
		}
	}
}
