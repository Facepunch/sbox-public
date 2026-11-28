using NativeEngine;
using Sandbox.Network;
using System.Collections.Generic;

namespace Sandbox;

/// <summary>
/// Rich Presence System for Steam
/// </summary>
internal sealed class SteamRichPresenceSystem : IRichPresenceSystem
{
	Dictionary<string, string> _values = new();

	void SetValue( string key, string value )
	{
		if ( _values.TryGetValue( key, out var oldValue ) && oldValue == value )
			return;

		SteamNative.Friends.SetRichPresence( key, value );

		_values[key] = value;
	}

	void Clear()
	{
		if ( SteamNative.Friends.IsAvailable() )
		{
			SteamNative.Friends.ClearRichPresence();
		}

		_values.Clear();
	}

	void IRichPresenceSystem.Poll()
	{
		if ( !SteamNative.Friends.IsAvailable() )
			return;

		// Friends' menus group everyone sharing a party_id, and use the rest to label it "X's party 2/16"
		if ( PartyRoom.Current is PartyRoom party )
		{
			SetValue( "party_id", $"{party.Id}" );
			SetValue( "party_owner", $"{party.Owner.Id}" );
			SetValue( "party_size", $"{party.MemberCount}" );
			SetValue( "party_max", $"{party.MaxMembers}" );
			// Cap the roster to fit Steam's 256-byte value limit.
			SetValue( "party_members", string.Join( ",", party.Members.Select( x => x.Id ).Order().Take( 12 ) ) );
		}
		else
		{
			SetValue( "party_id", null );
			SetValue( "party_owner", null );
			SetValue( "party_size", null );
			SetValue( "party_max", null );
			SetValue( "party_members", null );
		}

		SetValue( "in_editor", Application.IsEditor ? "1" : "0" );

		if ( IGameInstance.Current is null )
		{
			SetValue( "steam_display", "#StatusMenu" );
			SetValue( "gametitle", null );
			SetValue( "steam_player_group", null );
			SetValue( "steam_player_group_size", null );
			SetValue( "lobby", null );
			SetValue( "connect", null );
			return;
		}

		if ( Networking.IsActive )
		{
			var gameLobby = Networking.System.Sockets
				.OfType<SteamLobbySocket>()
				.FirstOrDefault();

			if ( gameLobby is not null )
			{
				// Anyone with the lobby id can try to join, so keep it from people who'd just get kicked
				SetValue( "connect", gameLobby.AdvertiseJoin ? $"+connect {gameLobby.LobbySteamId}" : null );
				SetValue( "steam_player_group", gameLobby.PresenceGroup );
				SetValue( "steam_player_group_size", $"{gameLobby.LobbyMemberCount}" );
			}
			else
			{
				var connection = Networking.System.Connection;
				var playerCount = Connection.All.Count( c => c.State == Connection.ChannelState.Connected );

				if ( connection is SteamNetwork.IpConnection or SteamNetwork.IdConnection )
				{
					SetValue( "connect", $"+connect {connection.Address}" );
					SetValue( "steam_player_group", $"{connection.Address}" );
					SetValue( "steam_player_group_size", $"{playerCount}" );
				}
			}
		}
		else
		{
			SetValue( "connect", null );
			SetValue( "steam_player_group", null );
			SetValue( "steam_player_group_size", null );
		}

		if ( Application.IsEditor )
		{
			// In editor
			SetValue( "steam_display", "#StatusEditor" );
		}
		else if ( IGameInstance.Current.Package is Package gamePackage )
		{
			if ( Application.Map?.Title is { } mapTitle )
			{
				// Playing Sandbox - Construct
				SetValue( "steam_display", "#StatusGameMap" );
				SetValue( "maptitle", mapTitle );
			}
			else if ( Application.Map?.ShortName is { } mapName )
			{
				// Playing Sandbox - gm_construct
				SetValue( "steam_display", "#StatusGameMap" );
				SetValue( "maptitle", mapName );
			}
			else
			{
				// Playing Sandbox
				SetValue( "steam_display", "#StatusGame" );
				SetValue( "maptitle", "" );
			}

			SetValue( "gametitle", gamePackage.Title );
			SetValue( "map", "" );
			SetValue( "gamename", Application.GameIdent );
		}
		else
		{
			// Playing Secret Game
			SetValue( "steam_display", "#StatusGame" );
			SetValue( "gametitle", "Secret Game" );
			SetValue( "map", "" );
			SetValue( "gamename", Application.GameIdent );
		}
	}
}
