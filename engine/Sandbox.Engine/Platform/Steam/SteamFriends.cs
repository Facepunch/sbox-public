using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Steamworks.Data;

namespace Steamworks
{
	/// <summary>
	/// Functions for clients to access data about Steam friends
	/// </summary>
	internal class SteamFriends : SteamClientClass<SteamFriends>
	{
		internal static ISteamFriends Internal => Interface as ISteamFriends;

		internal static bool IsInstalled => Internal?.IsValid ?? false;

		internal override void InitializeInterface( bool server )
		{
			SetInterface( server, new ISteamFriends( server ) );
			InstallEvents();
		}

		internal void InstallEvents()
		{
			Dispatch.Install<GameLobbyJoinRequested_t>( x => OnGameLobbyJoinRequested?.Invoke( x.SteamIDLobby ) );
			Dispatch.Install<PersonaStateChange_t>( x => { OnPersonaStateChange?.Invoke( new Friend( x.SteamID ) ); Friend.SteamFriends_OnPersonaStateChange( x.SteamID, (PersonaChange)x.ChangeFlags ); } );
			Dispatch.Install<GameRichPresenceJoinRequested_t>( x => OnGameRichPresenceJoinRequested?.Invoke( new Friend( x.SteamIDFriend ), x.ConnectUTF8() ) );
			Dispatch.Install<FriendRichPresenceUpdate_t>( x => OnFriendRichPresenceUpdate?.Invoke( new Friend( x.SteamIDFriend ) ) );
		}

		/// <summary>
		/// Called when a friends' status changes
		/// </summary>
		public static Action<Friend> OnPersonaStateChange { get; set; }

		/// <summary>
		/// Called when the user tries to join a game from their friends list
		///	rich presence will have been set with the "connect" key which is set here
		/// </summary>
		internal static Action<Friend, string> OnGameRichPresenceJoinRequested { get; set; }

		/// <summary>
		/// Callback indicating updated data about friends rich presence information
		/// </summary>
		public static Action<Friend> OnFriendRichPresenceUpdate { get; set; }

		/// <summary>
		/// Called when the user tries to join a game from their friends list
		/// in a lobby
		/// </summary>
		public static Action<Sandbox.SteamId> OnGameLobbyJoinRequested { get; set; }

		private static IEnumerable<Friend> GetFriendsWithFlag( FriendFlags flag )
		{
			if ( !IsInstalled )
				yield break;

			for ( int i = 0; i < Internal.GetFriendCount( (int)flag ); i++ )
			{
				yield return new Friend( Internal.GetFriendByIndex( i, (int)flag ) );
			}
		}

		public static IEnumerable<Friend> GetFriends()
		{
			return GetFriendsWithFlag( FriendFlags.Immediate );
		}

		/// <summary>
		/// "steamid" - Opens the overlay web browser to the specified user or groups profile.
		/// "chat" - Opens a chat window to the specified user, or joins the group chat.
		/// "jointrade" - Opens a window to a Steam Trading session that was started with the ISteamEconomy/StartTrade Web API.
		/// "stats" - Opens the overlay web browser to the specified user's stats.
		/// "achievements" - Opens the overlay web browser to the specified user's achievements.
		/// "friendadd" - Opens the overlay in minimal mode prompting the user to add the target user as a friend.
		/// "friendremove" - Opens the overlay in minimal mode prompting the user to remove the target friend.
		/// "friendrequestaccept" - Opens the overlay in minimal mode prompting the user to accept an incoming friend invite.
		/// "friendrequestignore" - Opens the overlay in minimal mode prompting the user to ignore an incoming friend invite.
		/// </summary>
		internal static void OpenUserOverlay( SteamId id, string type )
		{
			if ( !IsInstalled ) return;
			Internal.ActivateGameOverlayToUser( type, id );
		}

		/// <summary>
		/// Activates the Steam Overlay to open the invite dialog. Invitations sent from this dialog will be for the provided lobby.
		/// </summary>
		internal static void OpenGameInviteOverlay( SteamId lobby )
		{
			if ( !IsInstalled ) return;
			Internal.ActivateGameOverlayInviteDialog( lobby );
		}

		/// <summary>
		/// Requests the persona name and optionally the avatar of a specified user.
		/// NOTE: It's a lot slower to download avatars and churns the local cache, so if you don't need avatars, don't request them.
		/// returns true if we're fetching the data, false if we already have it
		/// </summary>
		internal static bool RequestUserInformation( SteamId steamid, bool nameonly = true )
		{
			return IsInstalled && Internal.RequestUserInformation( steamid, nameonly );
		}


		internal static async Task CacheUserInformationAsync( SteamId steamid, bool nameonly )
		{
			// Got it straight away, skip any waiting.
			if ( !RequestUserInformation( steamid, nameonly ) )
				return;

			await Task.Delay( 100 );

			while ( RequestUserInformation( steamid, nameonly ) )
			{
				await Task.Delay( 50 );
			}

			//
			// And extra wait here seems to solve avatars loading as [?]
			//
			await Task.Delay( 500 );
		}

		internal static async Task<Data.Image?> GetSmallAvatarAsync( SteamId steamid )
		{
			if ( !IsInstalled ) return null;

			await CacheUserInformationAsync( steamid, false );
			return SteamUtils.GetImage( Internal.GetSmallFriendAvatar( steamid ) );
		}

		internal static async Task<Data.Image?> GetMediumAvatarAsync( SteamId steamid )
		{
			if ( !IsInstalled ) return null;

			await CacheUserInformationAsync( steamid, false );
			return SteamUtils.GetImage( Internal.GetMediumFriendAvatar( steamid ) );
		}

		internal static async Task<Data.Image?> GetLargeAvatarAsync( SteamId steamid )
		{
			if ( !IsInstalled ) return null;

			await CacheUserInformationAsync( steamid, false );

			var imageid = Internal.GetLargeFriendAvatar( steamid );

			// Wait for the image to download
			while ( imageid == -1 )
			{
				await Task.Delay( 50 );
				imageid = Internal.GetLargeFriendAvatar( steamid );
			}

			return SteamUtils.GetImage( imageid );
		}
	}
}
