using Sandbox.Engine;
using Steamworks;
using Steamworks.Data;
using System;
using System.Text;

namespace Sandbox;

/// <summary>
/// A global chat channel backed by Steam. Message formats belong to the menu addon.
/// Dispose joined handles to leave; search results do not own membership.
/// </summary>
public sealed class MenuChatChannel : IDisposable, ILobby
{
	const int MessageProtocol = 0x4D454E55;
	Lobby lobby;
	bool joined;
	bool disposed;

	MenuChatChannel( Lobby lobby, bool joined )
	{
		this.lobby = lobby;
		this.joined = joined;
		if ( joined ) LobbyManager.Register( this );
	}

	/// <summary>
	/// The chat channel identifier.
	/// </summary>
	public ulong Id => lobby.Id;

	/// <summary>
	/// The current chat channel owner.
	/// </summary>
	public Friend Owner => new( lobby.Owner );

	/// <summary>
	/// Number of players currently in the chat channel.
	/// </summary>
	public int MemberCount => lobby.MemberCount;

	/// <summary>
	/// Maximum number of players allowed in the chat channel.
	/// </summary>
	public int MaxMembers => lobby.MaxMembers;

	/// <summary>
	/// Members of a joined chat channel. Search results cannot enumerate members.
	/// </summary>
	public IEnumerable<Friend> Members => lobby.Members.Select( x => new Friend( x ) );

	/// <summary>
	/// Whether this handle still owns Steam membership.
	/// </summary>
	public bool IsJoined => joined && !disposed;

	/// <summary>
	/// Called when channel metadata or membership changes.
	/// </summary>
	public event Action Changed;

	/// <summary>
	/// Receives a bounded menu message with its Steam-authenticated sender.
	/// </summary>
	public event Action<Friend, string> MessageReceived;

	/// <summary>
	/// Read an owner-controlled metadata value.
	/// </summary>
	public string GetData( string key ) => lobby.GetData( key );

	/// <summary>
	/// Set an owner-controlled metadata value.
	/// </summary>
	public void SetData( string key, string value )
	{
		if ( !IsJoined || !Owner.IsMe ) throw new InvalidOperationException( "Only the joined owner can change this chat channel." );
		if ( GetData( key ) == value ) return;
		if ( !lobby.SetData( key, value ) ) throw new InvalidOperationException( "Steam could not update the chat channel." );
	}

	/// <summary>
	/// Read metadata published by a member.
	/// </summary>
	public string GetMemberData( Friend member, string key ) => lobby.GetMemberData( member.Internal, key );

	/// <summary>
	/// Publish metadata for the local member.
	/// </summary>
	public void SetMemberData( string key, string value )
	{
		if ( !IsJoined ) throw new InvalidOperationException( "Join the chat channel first." );
		lobby.SetMemberData( key, value );
	}

	/// <summary>
	/// Send a menu payload, limited to 3 KB of UTF-8 text.
	/// </summary>
	public bool Send( string text )
	{
		if ( !IsJoined || text is null || Encoding.UTF8.GetByteCount( text ) > 3000 ) return false;
		using var stream = ByteStream.Create( 128 );
		stream.Write( MessageProtocol );
		stream.Write( text );
		return lobby.SendChatData( stream.ToArray() );
	}

	/// <summary>
	/// Find compatible chat channels with the supplied metadata filters.
	/// </summary>
	public static async Task<MenuChatChannel[]> FindAsync( Dictionary<string, string> filters )
	{
		var query = SteamMatchmaking.LobbyList.FilterDistanceWorldwide()
			.WithKeyValue( "menu_protocol", "1" )
			.WithKeyValue( "api", Protocol.Api.ToString() )
			.WithKeyValue( "protocol", Protocol.Network.ToString() )
			.WithKeyValue( "dev", Application.IsEditor ? "1" : "0" );
		foreach ( var pair in filters ) query = query.WithKeyValue( pair.Key, pair.Value );
		var found = await query.RequestAsync( default );
		return found?.Select( x => new MenuChatChannel( x, false ) ).ToArray() ?? [];
	}

	/// <summary>
	/// Create a searchable invisible lobby for menu chat.
	/// </summary>
	public static async Task<MenuChatChannel> CreateAsync( int capacity, Dictionary<string, string> data )
	{
		var result = await SteamMatchmaking.CreateLobbyAsync( LobbyType.Invisible, Math.Clamp( capacity, 2, 250 ) );
		if ( result is null ) throw new InvalidOperationException( "Steam could not create the chat channel." );
		await WaitForMembership( result.Value );
		var handle = new MenuChatChannel( result.Value, true );
		try
		{
			handle.SetData( "menu_protocol", "1" );
			handle.SetData( "api", Protocol.Api.ToString() );
			handle.SetData( "protocol", Protocol.Network.ToString() );
			handle.SetData( "dev", Application.IsEditor ? "1" : "0" );
			foreach ( var pair in data ) handle.SetData( pair.Key, pair.Value );
			return handle;
		}
		catch
		{
			handle.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Join a chat channel, reporting Steam failures to the caller.
	/// </summary>
	public static async Task<MenuChatChannel> JoinAsync( ulong id )
	{
		var result = await SteamMatchmaking.JoinLobbyAsync( id );
		if ( result.Response != RoomEnter.Success || result.Lobby is null )
			throw new InvalidOperationException( $"Could not join the chat channel: {result.Response}." );
		await WaitForMembership( result.Lobby.Value );
		return new MenuChatChannel( result.Lobby.Value, true );
	}

	static async Task WaitForMembership( Lobby lobby )
	{
		for ( var i = 0; i < 200; i++ )
		{
			if ( LobbyManager.ActiveLobbies.Contains( lobby.Id ) ) return;
			await Task.Delay( 10 );
		}
		lobby.Leave();
		throw new InvalidOperationException( "Steam did not confirm chat membership. Please try again." );
	}

	void Detach()
	{
		LobbyManager.Unregister( this );
		joined = false;
	}

	/// <summary>
	/// Release this handle, leaving Steam only if it still owns membership.
	/// </summary>
	public void Dispose()
	{
		if ( disposed ) return;
		disposed = true;
		if ( joined )
		{
			Detach();
			lobby.Leave();
		}
		Changed = null;
		MessageReceived = null;
	}

	void NotifyChanged()
	{
		using var scope = GlobalContext.MenuScope();
		Changed?.Invoke();
	}

	void ILobby.OnMemberEnter( Friend friend ) => NotifyChanged();
	void ILobby.OnMemberLeave( Friend friend ) => NotifyChanged();
	void ILobby.OnMemberUpdated( Friend friend ) => NotifyChanged();
	void ILobby.OnLobbyUpdated() => NotifyChanged();

	void ILobby.OnMemberMessage( Friend friend, ByteStream stream )
	{
		if ( friend.IsBlocked || stream.ReadRemaining > 4096 || !stream.TryRead<int>( out var protocol ) || protocol != MessageProtocol ) return;
		try
		{
			var text = stream.Read<string>();
			if ( text is null || Encoding.UTF8.GetByteCount( text ) > 3000 ) return;
			using var scope = GlobalContext.MenuScope();
			MessageReceived?.Invoke( friend, text );
		}
		catch ( Exception e )
		{
			Log.Trace( $"Ignored invalid chat channel message: {e.Message}" );
		}
	}
}
