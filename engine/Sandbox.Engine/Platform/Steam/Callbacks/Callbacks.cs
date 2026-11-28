using Sandbox.Engine;
using Sandbox.Network;
using Sandbox.Services;

namespace Sandbox.SteamApi;

/// <summary>
/// Entry points called by the native Steam client and game server.
/// </summary>
internal static class Callbacks
{
	internal static void SteamApiCallCompleted( ulong call )
	{
		SteamCalls.Complete( call );
	}

	internal static void WebAuthTicketReceived( string ticket ) => Auth.OnWebTicketReceived( ticket );

	internal static void SteamShutdown()
	{
		SteamClient.Cleanup();
	}

	internal static void NewUrlLaunchParameters()
	{
		if ( SteamClient.IsValid )
			IMenuDll.Current?.OnNewSteamUrlLaunchParameters();
	}

	internal static void ValidateAuthTicket( ulong steamId, AuthResponse response, ulong owner, bool server )
	{
		if ( server == Application.IsDedicatedServer && (server || SteamClient.IsValid) )
			SteamNetwork.SocketConnection.OnValidateAuthTicket( steamId, response, owner );
	}

	internal static void FriendChanged( ulong steamId, PersonaChange flags )
	{
		if ( !SteamClient.IsValid ) return;

		Friend.InvalidateCache( steamId, flags );
		IMenuDll.Current?.OnSteamFriendChanged( new Friend( steamId ) );
	}

	internal static void GameRichPresenceJoinRequested( string connect )
	{
		if ( SteamClient.IsValid )
			IMenuDll.Current?.OnSteamGameJoinRequested( connect );
	}

	internal static void GameLobbyJoinRequested( ulong lobbyId )
	{
		if ( SteamClient.IsValid )
			IMenuDll.Current?.OnSteamLobbyJoinRequested( lobbyId );
	}

	internal static unsafe void InventoryDefinitionsUpdated( IntPtr ids, int count )
	{
		Inventory.LoadDefinitions( new ReadOnlySpan<int>( (void*)ids, count ) );
	}

	internal static void InventoryPricesUpdated( string catalog ) => Inventory.LoadPrices( catalog );

	internal static void ItemInstalled( uint appId, ulong fileId, bool server )
	{
		if ( Ugc.AcceptsSteamNotifications( appId, server ) )
			Ugc.OnItemInstalled( fileId );
	}

	internal static void ItemDownloadFinished( uint appId, ulong fileId, Result result, bool server )
	{
		if ( Ugc.AcceptsSteamNotifications( appId, server ) )
			Ugc.OnDownloadResult( fileId, result );
	}

	internal static void WorkshopQueryCompleted( ulong requestId, string json ) => Workshop.OnQueryCompleted( requestId, json );

	internal static void WorkshopSubscriptionsChanged( uint appId )
	{
		if ( Ugc.AcceptsSteamNotifications( appId, server: false ) )
			Ugc.OnSubscriptionsChanged();
	}

	internal static void BrowserNeedsPaint( uint browser, IntPtr pixels, uint width, uint height )
	{
		WebSurface.GetBrowser( browser )?.OnNeedsRepaint( pixels, width, height );
	}

	internal static bool BrowserAllowsNavigation( uint browser, string url )
	{
		return WebSurface.GetBrowser( browser )?.AllowNavigation( url ) ?? false;
	}

	internal static void BrowserUrlChanged( uint browser, string url )
	{
		WebSurface.GetBrowser( browser )?.OnURLChanged( url );
	}

	internal static void BrowserTitleChanged( uint browser, string title )
	{
		WebSurface.GetBrowser( browser )?.OnChangedTitle( title );
	}

	internal static void BrowserFinishedRequest( uint browser, string url, string title )
	{
		WebSurface.GetBrowser( browser )?.OnFinishedRequest( url, title );
	}

	internal static void BrowserSetCursor( uint browser, string cursor )
	{
		if ( WebSurface.GetBrowser( browser ) is { } surface )
			surface.Cursor = cursor;
	}

	internal static void SteamServersConnected() => DedicatedServer.OnConnected();

	internal static void SteamServerConnectFailure( Result result, bool stillRetrying ) => DedicatedServer.OnConnectionFailed( result, stillRetrying );

	internal static void SteamServersDisconnected( Result result ) => DedicatedServer.OnDisconnected( result );

	internal static void LobbyInvite( ulong lobbyId, ulong memberId ) => LobbyManager.OnLobbyInvite( lobbyId, memberId );

	internal static void LobbyMemberEntered( ulong lobbyId, ulong memberId ) => LobbyManager.OnMemberEntered( lobbyId, memberId );

	internal static void LobbyMemberLeft( ulong lobbyId, ulong memberId ) => LobbyManager.OnMemberLeave( lobbyId, memberId );

	internal static void LobbyEntered( ulong lobbyId ) => LobbyManager.OnEntered( lobbyId );

	internal static void LobbyDataUpdated( ulong lobbyId, ulong targetId ) => LobbyManager.OnDataUpdate( lobbyId, targetId );

	internal static unsafe void LobbyChatMessage( ulong lobbyId, ulong memberId, IntPtr message, int length )
	{
		LobbyManager.OnChatMessage( lobbyId, memberId, new ReadOnlySpan<byte>( (void*)message, length ) );
	}

	internal static void SocketConnected( HSteamListenSocket socket, HSteamNetConnection connection ) => SteamNetwork.OnSocketConnection( socket, connection );

	internal static void NetworkingDebugOutput( int level, string message ) => Log.Info( $"SteamNetwork: {level}: {message}" );

	internal static void SocketDisconnected( HSteamListenSocket socket, HSteamNetConnection connection ) => SteamNetwork.OnSocketDisconnection( socket, connection );

	internal static bool ShouldAcceptConnection( HSteamListenSocket socket, HSteamNetConnection connection ) => SteamNetwork.ShouldAcceptConnection( socket, connection );

	internal static void Disconnected( HSteamNetConnection connection, int reasonCode, string reasonString ) => SteamNetwork.OnDisconnection( connection, reasonCode, reasonString );

	internal static void MessagingSessionAccepted( ulong steamId ) => SteamNetwork.OnSessionAccepted( steamId );

	internal static void MessagingSessionFailed( ulong steamId, ConnectionState state, int reasonCode, string reason )
	{
		SteamNetwork.OnSessionFailed( steamId, new SteamSessionStatus( state, reasonCode, reason ) );
	}
}
