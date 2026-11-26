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
		/// <returns>True when the dialog was requested, false when the overlay is unavailable.</returns>
		internal static bool OpenUserOverlay( SteamId id, string type )
		{
			if ( !SteamUtils.IsOverlayAvailable ) return false;

			Internal.ActivateGameOverlayToUser( type, id );
			return true;
		}

		/// <summary>
		/// Activates the Steam overlay's web browser at this URL. Returns false when the overlay is unavailable.
		/// </summary>
		internal static bool OpenWebOverlay( string url )
		{
			if ( !SteamUtils.IsOverlayAvailable ) return false;
			Internal.ActivateGameOverlayToWebPage( url, ActivateGameOverlayToWebPageMode.Default );
			return true;
		}

		/// <summary>
		/// Activates the Steam Overlay to open the invite dialog. Invitations sent from this dialog will be for the provided lobby.
		/// </summary>
		internal static void OpenGameInviteOverlay( SteamId lobby )
		{
			if ( !SteamUtils.IsOverlayAvailable ) return;
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

		/// <summary>
		/// Steam can shut down or install a new friends interface while a request is waiting.
		/// </summary>
		static bool IsCurrentInterface( ISteamFriends friends ) => friends is { IsValid: true } && friends == Interface;

		internal static async Task CacheUserInformationAsync( ISteamFriends friends, SteamId steamid, bool nameonly )
		{
			// Steam returns false if the user information is already cached.
			if ( !IsCurrentInterface( friends ) || !friends.RequestUserInformation( steamid, nameonly ) )
				return;

			await Task.Delay( 100 );

			while ( IsCurrentInterface( friends ) )
			{
				if ( !friends.RequestUserInformation( steamid, nameonly ) )
				{
					// Give Steam time to replace the placeholder avatar after the download.
					await Task.Delay( 500 );
					return;
				}

				await Task.Delay( 50 );
			}
		}

		internal static async Task<Data.Image?> GetSmallAvatarAsync( SteamId steamid )
		{
			var friends = Internal;
			await CacheUserInformationAsync( friends, steamid, false );

			if ( !IsCurrentInterface( friends ) )
				return null;

			return SteamUtils.GetImage( friends.GetSmallFriendAvatar( steamid ) );
		}

		internal static async Task<Data.Image?> GetMediumAvatarAsync( SteamId steamid )
		{
			var friends = Internal;
			await CacheUserInformationAsync( friends, steamid, false );

			if ( !IsCurrentInterface( friends ) )
				return null;

			return SteamUtils.GetImage( friends.GetMediumFriendAvatar( steamid ) );
		}

		internal static async Task<Data.Image?> GetLargeAvatarAsync( SteamId steamid )
		{
			var friends = Internal;
			await CacheUserInformationAsync( friends, steamid, false );

			// Wait for the image to download
			while ( IsCurrentInterface( friends ) )
			{
				var imageid = friends.GetLargeFriendAvatar( steamid );
				if ( imageid != -1 )
					return SteamUtils.GetImage( imageid );

				await Task.Delay( 50 );
			}

			return null;
		}
	}
}
