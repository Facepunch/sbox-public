using System.Threading;

namespace Sandbox.SteamApi
{
	/// <summary>
	/// Functions for clients to access data about Steam friends
	/// </summary>
	internal static class SteamFriends
	{
		internal static bool IsInstalled => SteamClient.IsValid && SteamNative.Friends.IsAvailable();

		/// <summary>
		/// Whether this process can open the Steam overlay for the current user.
		/// </summary>
		static bool IsOverlayAvailable => !Sandbox.Application.IsEditor && !Sandbox.Application.IsHeadless
			&& IsInstalled && SteamNative.Utils.IsOverlayEnabled();

		/// <summary>
		/// Returns a snapshot of the local user's immediate Steam friends.
		/// </summary>
		internal static ulong[] GetFriendIds()
		{
			if ( !SteamClient.IsValid ) return [];

			using var friends = SteamNative.Friends.GetFriendIds();
			return friends.AsSpan<ulong>().ToArray();
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
			if ( !IsOverlayAvailable ) return false;

			SteamNative.Friends.ActivateGameOverlayToUser( type, id );
			return true;
		}

		/// <summary>
		/// Activates the Steam overlay's web browser at this URL. Returns false when the overlay is unavailable.
		/// </summary>
		internal static bool OpenWebOverlay( string url )
		{
			if ( !IsOverlayAvailable ) return false;
			SteamNative.Friends.ActivateGameOverlayToWebPage( url );
			return true;
		}

		/// <summary>
		/// Activates the Steam Overlay to open the invite dialog. Invitations sent from this dialog will be for the provided lobby.
		/// </summary>
		internal static void OpenGameInviteOverlay( SteamId lobby )
		{
			if ( !IsOverlayAvailable ) return;
			SteamNative.Friends.ActivateGameOverlayInviteDialog( lobby );
		}

		/// <summary>
		/// Requests the persona name and optionally the avatar of a specified user.
		/// NOTE: It's a lot slower to download avatars and churns the local cache, so if you don't need avatars, don't request them.
		/// returns true if we're fetching the data, false if we already have it
		/// </summary>
		internal static bool RequestUserInformation( SteamId steamid, bool nameonly = true )
		{
			return IsInstalled && SteamNative.Friends.RequestUserInformation( steamid, nameonly );
		}

		/// <summary>
		/// Steam can shut down or install a new friends interface while a request is waiting.
		/// </summary>
		static bool IsCurrentSession( int requestSession ) => requestSession == SteamClient.Session && IsInstalled;

		static async Task CacheAvatarAsync( int requestSession, SteamId steamid )
		{
			// Steam returns false if the user information is already cached.
			if ( !IsCurrentSession( requestSession ) || !SteamNative.Friends.RequestUserInformation( steamid, false ) )
				return;

			await Task.Delay( 100 );

			while ( IsCurrentSession( requestSession ) )
			{
				if ( !SteamNative.Friends.RequestUserInformation( steamid, false ) )
				{
					// Give Steam time to replace the placeholder avatar after the download.
					await Task.Delay( 500 );
					return;
				}

				await Task.Delay( 50 );
			}
		}

		/// <summary>
		/// Fetches the URL of the user's equipped animated avatar.
		/// </summary>
		internal static Task<string> GetAnimatedAvatarUrlAsync( SteamId steamId, CancellationToken token )
		{
			var call = SteamNative.Friends.RequestEquippedProfileItems( steamId );
			return SteamCalls.GetResultAsync( call, SteamNative.Friends.GetAnimatedAvatarUrlResult, token );
		}

		internal static async Task<AvatarImage?> GetAvatarAsync( SteamId steamId, AvatarSize size )
		{
			var session = SteamClient.Session;
			await CacheAvatarAsync( session, steamId );

			while ( IsCurrentSession( session ) )
			{
				var handle = SteamNative.Friends.GetAvatar( steamId, size );
				// Steam can still be downloading the large image after the user's profile is cached.
				if ( size != AvatarSize.Large || handle != -1 )
					return AvatarImage.FromHandle( handle );

				await Task.Delay( 50 );
			}

			return null;
		}
	}
}
