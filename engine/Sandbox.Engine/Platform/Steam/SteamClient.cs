namespace Steamworks
{
	internal static class SteamClient
	{
		static bool initialized;

		/// <summary>
		/// Initialize the steam client.
		/// </summary>
		internal static void Init( int appid )
		{
			if ( initialized )
				throw new Exception( "Calling SteamClient.Init but is already initialized" );

			AppId = appid;

			// Without Steam (-nosteam, or Steam not running) the interfaces would call native with null pointers.
			if ( !NativeEngine.Steam.SteamUser().IsValid )
				return;

			initialized = true;

			AddInterface<SteamFriends>();
			AddInterface<SteamMatchmaking>();
			AddInterface<SteamUtils>();

			var user = NativeEngine.Steam.SteamUser();
			if ( user.IsValid )
			{
				SteamId = user.GetSteamID();
			}

			var friends = NativeEngine.Steam.SteamFriends();
			if ( friends.IsValid )
			{
				Name = friends.GetPersonaName();
			}
		}

		internal static void AddInterface<T>() where T : SteamClass, new()
		{
			var t = new T();
			t.InitializeInterface( false );
			openInterfaces.Add( t );
		}

		static readonly List<SteamClass> openInterfaces = new List<SteamClass>();

		internal static void ShutdownInterfaces()
		{
			foreach ( var e in openInterfaces )
			{
				e.DestroyInterface( false );
			}

			openInterfaces.Clear();
		}

		/// <summary>
		/// Check if Steam is loaded and accessible.
		/// </summary>		
		internal static bool IsValid => initialized;

		internal static void Cleanup()
		{
			Dispatch.ShutdownClient();

			initialized = false;
			ShutdownInterfaces();
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

		/// <summary>
		/// returns the appID of the current process
		/// </summary>
		internal static AppId AppId;

	}
}
