using Refit;
using Sandbox.Engine;
using Sandbox.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sandbox;

internal static partial class Api
{
	internal static bool UseSteamAuthentication = true;
	internal static bool IsConnected { get; private set; } = true;

	internal static void StartOffline()
	{
		IsConnected = false;
	}

	/// <summary>
	/// Get linked service credentials, ie "twitch". Under the hood, on our server, we will
	/// probably be renewing the token with the service (assume the token is only good for 2-3 hours).
	/// </summary>
	internal static async Task<LoginResult> GetAccountInformation()
	{
		LoginResult result = default;

		// For testing how the engine behaves when not receiving auth tokens.
		if ( !UseSteamAuthentication ) return default;

		var sw = Stopwatch.StartNew();

		try
		{
			var token = await Auth.GetWebTicketAsync();
			if ( string.IsNullOrEmpty( token ) )
			{
				Log.Warning( "Couldn't get a Steam web authentication ticket." );
				return default;
			}

			if ( sw.Elapsed.TotalSeconds > 2.0f )
			{
				Log.Warning( $"Took {sw.Elapsed.TotalSeconds}s to get steam auth ticket" );
			}

			string friendList = default;
			ulong friendHash = default;

			// embed friends list
			{
				var friends = Sandbox.SteamApi.SteamFriends.GetFriendIds();
				Array.Sort( friends );
				if ( friends.Length > 0 )
				{
					friendList = System.Convert.ToBase64String( MemoryMarshal.AsBytes( friends.AsSpan() ) );
					friendHash = Sandbox.Utility.Crc64.FromString( friendList );
				}
			}

			sw = Stopwatch.StartNew();

			result = await Sandbox.Backend.Account.Login( new
			{
				fa = friendList,
				fh = friendHash,
				st = token,
				d = new
				{
					Application.IsEditor,
					Application.IsBenchmark,
					Application.IsVR,
					Application.Version,
					Application.VersionDate,
					System = SystemInfo.AsObject()
				}
			} );

			if ( sw.Elapsed.TotalSeconds > 2.0f )
			{
				Log.Warning( $"Account information took {sw.Elapsed.TotalSeconds}s" );
			}

			if ( !string.IsNullOrWhiteSpace( result.MessagingEndpoint ) )
			{
				_ = ObserveMessagingStartup( Messaging.Initialize( result.MessagingEndpoint ) );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error getting account information ({e.Message})" );
		}
		finally
		{
			Auth.CancelWebTicket();
		}

		if ( result.Id == 0 )
		{
			await Task.Delay( 1000 );
			return default;
		}

		IsConnected = true;
		return result;
	}

	/// <summary>
	/// Report a background connection failure while allowing account login to complete independently.
	/// </summary>
	internal static async Task ObserveMessagingStartup( Task startup )
	{
		try
		{
			await startup.ConfigureAwait( false );
		}
		catch ( Exception e )
		{
			// Transport exceptions can include the signed messaging URL.
			Log.Warning( $"Messaging connection failed ({e.GetBaseException().GetType().Name})." );
		}
	}

}
