using Sandbox.Engine;
using Sandbox.Protobuf;
using Sandbox.Services;

namespace Sandbox;

internal class AccountInformation
{
	/// <summary>
	/// A list of services that we have linked
	/// </summary>
	public static List<StreamService> Links { get; set; } = new();

	/// <summary>
	/// A list of organizations of which we're a member
	/// </summary>
	public static List<Package.Organization> Memberships { get; set; } = new();

	/// <summary>
	/// A list of our favourited games
	/// </summary>
	public static List<RemotePackage> Favourites { get; set; } = new();

	/// <summary>
	/// Current client hash (the login session cookie)
	/// </summary>
	public static string Session { get; private set; }

	/// <summary>
	/// The current logged in user's steamid
	/// </summary>
	public static long SteamId { get; private set; }

	/// <summary>
	/// The current logged in user's gamercore
	/// </summary>
	public static long Score { get; set; }

	/// <summary>
	/// The current logged in user's avatar, from the backend
	/// </summary>
	public static string AvatarJson { get; set; }


	static Task<LoginResult> updateTask;

	static AccountInformation()
	{
		// start listening to backend messages
		Sandbox.Services.Messaging.OnMessage += OnMessageFromBackend;
	}

	/// <summary>
	/// Update Current
	/// </summary>
	public static async Task Update()
	{
		if ( updateTask != null )
		{
			await updateTask;
			return;
		}

		try
		{
			Session = null;
			updateTask = Api.GetAccountInformation();

			var login = await updateTask;

			if ( login.Id == 0 )
			{
				Log.Warning( "There was a problem retrieving account information, so we're offline" );
				Api.StartOffline();

				return;
			}

			NativeErrorReporter.SetTag( "session_id", login.Session );
			NativeErrorReporter.SetTag( "launch_guid", Api.LaunchGuid );

			SteamId = login.Id;
			Session = login.Session;
			Links = login.Links?.Select( x => (StreamService)x ).ToList() ?? new();
			Favourites = login.Favourites?.Select( x => RemotePackage.FromDto( x ) ).ToList() ?? new();
			Memberships = login.Memberships?.Select( x => Package.Organization.FromDto( x ) ).ToList() ?? new();
			Score = login.Player?.Score ?? 0;

			if ( !string.IsNullOrWhiteSpace( login.AvatarJson ) )
			{
				AvatarJson = login.AvatarJson;
				Avatar.AvatarJson = AvatarJson;
			}

			foreach ( var favourite in Favourites )
			{
				Package.Cache( favourite, true );
			}

			IMenuDll.Current?.RunEvent( "menu.home.rebuild" );
			IToolsDll.Current?.RunEvent( "account.update" );
		}
		finally
		{
			updateTask = null;
		}
	}

	/// <summary>
	/// Helper - return true if Current.Favourites contains us
	/// </summary>
	internal static bool IsFavourite( string fullIdent )
	{
		return Favourites?.Any( x => x.FullIdent == fullIdent ) ?? false;
	}

	/// <summary>
	/// Returns true if a user is a member of this organization
	/// </summary>
	internal static bool HasOrganization( string ident )
	{
		return Memberships?.Any( x => x.Ident == ident ) ?? false;
	}

	/// <summary>
	/// Returns a URL in which users have the ability to upload files (but not the ability to list or download files).
	/// This is used so users can upload files to our blob storage, and give us the filename - rather than uploading big
	/// ass files to our api.
	/// </summary>
	internal static ValueTask<string> GetUploadEndPointAsync( string filename )
	{
		// todo - get this from the backend
		return ValueTask.FromResult( $"https://facepunchuploads.blob.core.windows.net/file-uploads/{filename}?sv=2020-08-04&ss=b&srt=sco&sp=wtfx&se=2092-01-21T21:32:34Z&st=2022-01-21T13:32:34Z&spr=https&sig=VbM%2B6DUoagd4%2FY3kSX3Zif%2BvkBpkcgjZX8uCRkF%2F1XI%3D" );
	}

	private static async void OnMessageFromBackend( Messaging.Message msg )
	{
		if ( msg.Data is AccountMsg.Edited accountMsg )
		{
			Log.Info( "Account information has changed.. refreshing.." );
			await Update();
		}
	}
}
