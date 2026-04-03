using Sandbox.Engine;
using Sandbox.Engine.Settings;
using Sandbox.Modals;
using Sandbox.Services;
using System;
using System.Net;

namespace Sandbox;


[Hide]
public static partial class MenuUtility
{
	public static Action Tick { get; set; }

	public static void SetModalSystem( IModalSystem system )
	{
		IModalSystem.Current = system;
	}

	public static void AddLogger( Action<LogEvent> logger )
	{
		Sandbox.Diagnostics.Logging.OnMessage += logger;
	}

	public static void RemoveLogger( Action<LogEvent> logger )
	{
		Sandbox.Diagnostics.Logging.OnMessage -= logger;
	}

	public static ConCmdAttribute.AutoCompleteResult[] AutoComplete( string text, int maxCount )
	{
		return ConVarSystem.GetAutoComplete( text, maxCount );
	}

	public static void SkipAllTransitions()
	{
		IMenuDll.Current?.RunEvent( "ui.skiptransitions" );
	}

	/// <summary>
	/// If current game is active, return the package
	/// </summary>
	public static Package GamePackage => Application.GamePackage;

	/// <summary>
	/// Init a stream service
	/// </summary>
	public static async Task<bool> ConnectStream( StreamService service )
	{
		return await Sandbox.Engine.Streamer.Init( service );
	}

	/// <summary>
	/// Init a stream service
	/// </summary>
	public static void DisconnectStream()
	{
		Sandbox.Engine.Streamer.Shutdown();
	}

	public static SceneWorld CreateSceneWorld()
	{
		return new SceneWorld { IsTransient = false };
	}

#nullable enable
	/// <summary>
	/// Open an 'open file' dialog
	/// </summary>
	public static string? OpenFileDialog()
	{
		var r = NativeEngine.WindowsGlue.FindFile();
		if ( string.IsNullOrEmpty( r ) ) return null;
		return r;
	}
#nullable disable

	/// <summary>
	/// Open a folder 
	/// </summary>
	public static void OpenFolder( string path )
	{
		System.Diagnostics.Process.Start( new System.Diagnostics.ProcessStartInfo()
		{
			FileName = path,
			UseShellExecute = true,
			Verb = "open"
		} );
	}

	/// <summary>
	/// Open a url
	/// </summary>
	public static void OpenUrl( string path )
	{
		System.Diagnostics.Process.Start( new System.Diagnostics.ProcessStartInfo()
		{
			FileName = path,
			UseShellExecute = true,
			Verb = "open"
		} );
	}

	static List<Friend> _friendList;

	/// <summary>
	/// Get all friends.
	/// </summary>
	public static IEnumerable<Friend> Friends
	{
		get
		{
			//
			// querying this once should be enough, unless they add a new friend or something
			//
			if ( _friendList is null )
			{
				_friendList = Steamworks.SteamFriends.GetFriends().Select( x => new Friend( x ) ).ToList();
			}

			return _friendList;
		}
	}


	/// <summary>
	/// Number of seconds escape has been held down
	/// </summary>
	public static float EscapeTime => InputRouter.EscapeTime;

	/// <summary>
	/// Join the game a friend is in
	/// </summary>
	public static void JoinFriendGame( Friend friend )
	{
		var connectString = friend.GetRichPresence( "connect" );
		if ( string.IsNullOrWhiteSpace( connectString ) ) return;

		connectString = connectString.Replace( "+connect", "" );
		connectString = connectString.Replace( " ", "" );

		// Should be left with a Steam Id but otherwise try connecting by IP.
		if ( ulong.TryParse( connectString, out ulong lobbySteamId ) )
		{
			ConsoleSystem.Run( $"connect {lobbySteamId}" );
		}
		else
		{
			var ipAddress = IPEndPoint.Parse( connectString );
			ConsoleSystem.Run( $"connect {ipAddress}" );
		}
	}

	/// <summary>
	/// We might be running the game from sbox.game, so we want the menu system to open the game immediately
	/// </summary>
	public static string StartupGameIdent => Utility.CommandLine.GetSwitch( "-rungame", null );

	/// <summary>
	/// This is called when the cancel button is pressed when loading. 
	/// We should disconnect and leave the game.
	/// </summary>
	public static void CancelLoading()
	{
		// Close the game
		CloseGame();

		// Close the loading screen
		LoadingScreen.IsVisible = false;
	}

	/// <summary>
	/// Set a console variable. Unlike ConsoleSystem.*, this is unprotected and allows any console variable to be changed.
	/// </summary>
	public static void SetConsoleVariable( string name, object value )
	{
		ConVarSystem.SetValue( name, value?.ToString(), true );
	}

	/// <summary>
	/// Access to the client's render settings
	/// </summary>
	public static RenderSettings RenderSettings => Sandbox.Engine.Settings.RenderSettings.Instance;

	/// <summary>
	/// Listen to the voice
	/// </summary>
	public static void SetVoiceListen( bool b )
	{
		if ( b )
		{
			PartyRoom.Current?.SetBroadcastVoice();
		}
	}

	/// <summary>
	/// Connect to a lobby and close all open modals.
	/// </summary>
	public static void Connect( ulong lobbyId )
	{
		CloseAllModals();
		Networking.Connect( lobbyId );
	}

	/// <summary>
	/// Close every open modal
	/// </summary>
	public static void CloseAllModals()
	{
		IModalSystem.Current?.CloseAll();
	}

	/// <summary>
	/// Get the player's friend activity feed
	/// </summary>
	public static Task<Feed[]> GetPlayerFeed( int take = 20 ) => Feed.GetFeed( take );

	/// <summary>
	/// How many notifications does the player have?
	/// </summary>
	public static Task<int> GetNotificationCount() => Notification.GetCount();

	/// <summary>
	/// Mark the player's notifications as all read. Call when viewing notifications.
	/// </summary>
	public static Task<int> MarkNotificationsRead() => Notification.MarkRead();

	/// <summary>
	/// Get a list of notifications
	/// </summary>
	public static Task<Notification[]> GetNotifications( int count ) => Notification.Get( count );

	/// <summary>
	/// Get a list of recent achievement progress
	/// </summary>
	public static Task<AchievementOverview[]> GetAchievementOverviews( int count ) => AchievementOverview.GetFeed( count );

	/// <summary>
	/// Get a list of recent achievement progress
	/// </summary>
	public static Task SaveAvatar( ClothingContainer container, bool isActive, int slot ) => container.Store( isActive, slot );

	/// <summary>
	/// Delete avatar in slot x
	/// </summary>
	public static Task DeleteAvatar( int slot )
	{
		return Backend.Storage.Delete( (long)Utility.Steam.SteamId, "facepunch.avatar", "avatar", $"{slot}" );
	}

	/// <summary>
	/// Delete all avatars, return to default
	/// </summary>
	public static async Task DeleteAvatars()
	{
		await Backend.Storage.Drop( (long)Utility.Steam.SteamId, "facepunch.avatar", "avatar" );
		await Backend.Storage.Drop( (long)Utility.Steam.SteamId, "facepunch.avatar", "avatar.active" );
	}

	/// <summary>
	/// Invite someone to the current party. If one exists
	/// </summary>
	public static void InviteToParty( SteamId steamid )
	{
		PartyRoom.Current?.InviteFriend( steamid );
	}

	/// <summary>
	/// Opens the invite overlay
	/// </summary>
	public static void InviteOverlayToParty()
	{
		PartyRoom.Current?.InviteFriend();
	}

	/// <summary>
	/// Post a review for a package
	/// </summary>
	public static Task PostReview( string packageIdent, Sandbox.Services.Review.ReviewScore score, string content )
	{
		return Sandbox.Services.Review.Post( packageIdent, score, content );
	}

	/// <summary>
	/// Allows async tasks to wait to be executed in the menu context
	/// </summary>
	public static void RunTask( Func<Task> func )
	{
		if ( Application.IsEditor ) func();

		// Post the *whole* function into the target context
		MenuDll.AsyncContext.Post( async _ =>
		{
			await func().ConfigureAwait( true );

		}, null );
	}

}

public class StoragePublish
{
	Ugc.UgcPublisher item;

	public ulong ItemId => item?.ItemId ?? 0;

	public string Title { get; set; }
	public string Description { get; set; }
	public string Metadata { get; set; }
	public Storage.Visibility Visibility { get; set; } = Storage.Visibility.Public;
	public Dictionary<string, string> KeyValues { get; set; }
	public HashSet<string> Tags { get; set; }
	public Bitmap Thumbnail { get; set; }
	public BaseFileSystem FileSystem { get; set; }

	/// <summary>
	/// If set, update this existing workshop item instead of creating a new one.
	/// </summary>
	public ulong PublishedFileId { get; set; }

	public async Task Submit()
	{
		if ( PublishedFileId != 0 )
		{
			item = Sandbox.Services.Ugc.OpenItem( PublishedFileId );
		}
		else
		{
			item = await Sandbox.Services.Ugc.CreateCommunityItem();
		}

		string _imagePath = null;
		string _dataPath = null;

		item.SetTitle( Title );
		item.SetDescription( Description );
		item.SetMetaData( Metadata );
		item.SetVisibility( Visibility );

		if ( KeyValues != null )
		{
			foreach ( var kv in KeyValues )
			{
				item.SetKeyValue( kv.Key, kv.Value );
			}
		}

		if ( Tags != null )
		{
			foreach ( var tag in Tags )
			{
				item.SetTag( tag );
			}
		}

		// Save thumbnail to a temp file
		if ( Thumbnail != null )
		{
			_imagePath = System.IO.Path.GetTempFileName() + ".png";
			System.IO.File.WriteAllBytes( _imagePath, Thumbnail.ToPng() );
			item.SetPreviewImage( _imagePath );
		}

		// Copy files from virtual filesystem to a temp folder
		if ( FileSystem != null )
		{
			_dataPath = System.IO.Path.GetTempPath() + "/" + System.Guid.NewGuid().ToString();
			var files = FileSystem.FindFile( "/", "*", true );

			foreach ( var file in files )
			{
				var srcPath = System.IO.Path.Combine( "/", file );
				var destPath = System.IO.Path.Combine( _dataPath, file );

				System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( destPath ) );
				System.IO.File.WriteAllBytes( destPath, FileSystem.ReadAllBytes( srcPath ) );
			}

			item.SetContentFile( _dataPath );
		}

		item.SetKeyValue( "package", Game.Ident ); // TODO - walk the stack to determine what called it?
		item.SetKeyValue( "version", Application.Version );

		await item.Submit();

		if ( _imagePath != null ) System.IO.File.Delete( _imagePath );
		if ( _dataPath != null ) System.IO.Directory.Delete( _dataPath, true );
	}

	public float PercentComplete => item?.PercentComplete ?? 0f;
	public bool IsPublishing => item?.IsPublishing ?? false;
	public bool IsFinished => item?.IsFinished ?? false;
}
