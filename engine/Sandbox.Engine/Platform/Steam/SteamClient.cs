namespace Sandbox.SteamApi
{
	internal static class SteamClient
	{
		static bool initialized;
		internal static int Session { get; private set; }

		/// <summary>
		/// Initialize the steam client.
		/// </summary>
		internal static void Init()
		{
			if ( initialized )
				throw new Exception( "Calling SteamClient.Init but is already initialized" );

			// Without Steam (-nosteam, or Steam not running) the interfaces would call native with null pointers.

			if ( !SteamNative.User.IsAvailable() )
				return;

			initialized = true;

			Session++;

			SteamId = SteamNative.User.GetSteamId();

			if ( SteamNative.Friends.IsAvailable() )
			{
				Name = SteamNative.Friends.GetPersonaName();
			}
		}

		/// <summary>
		/// Check if Steam is loaded and accessible.
		/// </summary>		
		internal static bool IsValid => initialized;

		internal static void Cleanup()
		{
			initialized = false;
			SteamCalls.Shutdown();
			WebSurface.Shutdown();
			Friend.ClearCache();
			Services.Auth.CancelWebTicket();
			Services.Ugc.ClientShutdown();
		}


		/// <summary>
		/// Gets the Steam ID of the account currently logged into the Steam client. This is 
		/// commonly called the 'current user', or 'local user'.
		/// A Steam ID is a unique identifier for a Steam accounts, Steam groups, Lobbies and Chat 
		/// rooms, and used to differentiate users in all parts of the Steamworks API.
		/// </summary>
		internal static SteamId SteamId;

		/// <summary>
		/// returns the local players name - guaranteed to not be NULL.
		/// this is the same name as on the users community profile page
		/// </summary>
		internal static string Name;
	}
}
