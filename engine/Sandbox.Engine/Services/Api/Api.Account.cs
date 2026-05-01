using NativeEngine;
using Refit;
using Sandbox.Engine;
using Sandbox.Services;
using System.Diagnostics;

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
		string token = null;
		LoginResult result = default;

		// For testing how the engine behaves when not receiving auth tokens.
		if ( !UseSteamAuthentication ) return default;

		var sw = Stopwatch.StartNew();

		while ( true )
		{
			EngineGlue.RequestWebAuthTicket();

			// Check for ticket for 10 seconds total, every half a second
			for ( int i = 0; i < 10000 / 100; i++ )
			{
				token = EngineGlue.GetWebAuthTicket();

				if ( token != null ) goto gotTicket;

				await Task.Delay( 100 );
			}

			// we didn't get a ticket - start in offline mode
			EngineGlue.CancelWebAuthTicket();
			await Task.Delay( 1000 );

			Log.Warning( "Couldn't connect to Steam." );

			return default;
		}

		gotTicket:
		try
		{
			if ( sw.Elapsed.TotalSeconds > 2.0f )
			{
				Log.Warning( $"Took {sw.Elapsed.TotalSeconds}s to get steam auth ticket" );
			}

			string friendList = default;
			ulong friendHash = default;

			// embed friends list
			{
				var friends = Steamworks.SteamFriends.GetFriends().Where( x => x.IsFriend ).Select( x => x.Id.Value ).Order().ToArray();
				if ( friends != null && friends.Length > 0 )
				{
					byte[] outputArray = new byte[friends.Length * sizeof( ulong )];
					Buffer.BlockCopy( friends, 0, outputArray, 0, outputArray.Length );

					friendList = System.Convert.ToBase64String( outputArray );
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
				// Connect to messaging in the background, no big deal
				_ = Sandbox.Services.Messaging.Initialize( result.MessagingEndpoint );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error getting account information ({e.Message})" );
		}
		finally
		{
			EngineGlue.CancelWebAuthTicket();
		}

		if ( result.Id == 0 )
		{
			await Task.Delay( 1000 );
			return default;
		}

		IsConnected = true;
		return result;
	}

}
