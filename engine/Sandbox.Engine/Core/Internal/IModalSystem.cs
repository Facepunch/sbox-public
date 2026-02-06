namespace Sandbox.Modals;

public interface IModalSystem
{
	internal static IModalSystem Current { get; set; }

	public bool HasModalsOpen();
	public void CloseAll( bool immediate = false );
	public void Game( string packageIdent );
	public void Map( string packageIdent );
	public void Package( string packageIdent, string page );
	public void Organization( Package.Organization org );
	public void Review( Package package );
	public void PackageSelect( string query, Action<Package> onPackageSelected, Action<string> onFilterChanged = null );
	public void FriendsList( in FriendsListModalOptions options );
	public void ServerList( in ServerListConfig config );
	public void Settings( string page = "" );
	public void CreateGame( in CreateGameOptions options );
	public void Player( SteamId steamid, string page = "" );
	public void News( Sandbox.Services.News newsitem );
	public void PlayerList();
	public void WorkshopPublish( in WorkshopPublishOptions options );

	/// <summary>
	/// The menu that is shown when escape is pressed while playing.
	/// </summary>
	public void PauseMenu();

	public bool IsModalOpen { get; }
	public bool IsPauseMenuOpen { get; }
}


public struct FriendsListModalOptions
{
	public FriendsListModalOptions()
	{
	}

	/// <summary>
	/// Show offline members
	/// </summary>
	public bool ShowOfflineMembers { get; set; } = true;

	/// <summary>
	/// Show online (but not in-game) members
	/// </summary>
	public bool ShowOnlineMembers { get; set; } = true;
}


public struct ServerListConfig
{
	public ServerListConfig( string game = null, string map = null )
	{
		GamePackageFilter = game;
		MapPackageFilter = map;
	}

	public string GamePackageFilter { get; set; }
	public string MapPackageFilter { get; set; }
}

/// <summary>
/// Passed to IModalSystem.CreateGame
/// </summary>
public struct CreateGameOptions
{
	public CreateGameOptions( Package package, Action<CreateGameResults> onComplete = null )
	{
		Package = package;
		OnComplete = onComplete;
	}

	public Package Package { get; set; }
	public Action<CreateGameResults> OnComplete { get; set; }
}

public struct CreateGameResults
{
	public CreateGameResults()
	{
	}

	public Dictionary<string, string> GameSettings { get; set; } = new();

	public string MapIdent { get; set; }

	public int MaxPlayers { get; set; }

	public string ServerName { get; set; }

	public Sandbox.Network.LobbyPrivacy Privacy { get; set; }
}

/// <summary>
/// Passed to IModalSystem.WorkshopPublish
/// </summary>
public struct WorkshopPublishOptions
{
	public WorkshopPublishOptions()
	{

	}

	/// <summary>
	/// The default title of this item. The user will be able to change it.
	/// </summary>
	public string Title { get; set; }

	/// <summary>
	/// The description of this item. The user will be able to change it.
	/// </summary>
	public string Description { get; set; }

	/// <summary>
	/// 512x512 thumbnail image, no transparency
	/// </summary>
	public Bitmap Thumbnail { get; set; }

	/// <summary>
	/// The filesystem containing the files to publish
	/// </summary>
	public Storage.Entry StorageEntry { get; set; }

	/// <summary>
	/// Keyvalues to store on the item. You can search and filter by these later.
	/// </summary>
	public Dictionary<string, string> KeyValues { get; set; }

	/// <summary>
	/// Tags to set on the item. You can search and filter by these later.
	/// </summary>
	public HashSet<string> Tags { get; set; }

	/// <summary>
	/// You can store metadata on the item, which is just a string. This can be read when querying items before
	/// downloading them - so it can be useful for storing extra info you want to store.
	/// </summary>
	public string Metadata { get; set; }

	/// <summary>
	/// The visibility of the item
	/// </summary>
	public Storage.Visibility Visibility { get; set; } = Storage.Visibility.Public;

	/// <summary>
	/// Can the client select the visibility for this item
	/// </summary>
	public bool CanSelectVisibility { get; set; } = true;

	/// <summary>
	/// Called when done. The ulong is the published item id. You can access it via url
	/// https://steamcommunity.com/sharedfiles/filedetails/?id=######
	/// </summary>
	public Action<ulong> OnComplete { get; set; }

	/// <summary>
	/// Defined categories to show in the workshop publish modal
	/// </summary>
	public Dictionary<string, SerializedProperty> Categories { get; } = [];

	/// <summary>
	/// Adds a new category associated with the specified enum type to the collection. 
	/// The user will be prompted to select one of the enum values when publishing.
	/// This will be set on the file as keyvalues[name] = enum.ToString()
	/// </summary>
	public void AddCategory<TEnum>( string name ) where TEnum : struct, Enum
	{
		var t = Game.TypeLibrary.GetEnumDescription( typeof( TEnum ) );
		if ( t == null )
			throw new Exception( $"Type {typeof( TEnum ).FullName} is not registered in the TypeLibrary" );

		KeyValues ??= [];
		var kv = KeyValues;
		var get = () => kv.TryGetValue( name, out string val ) ? (Enum.TryParse( val, true, out TEnum r ) ? r : default) : default;
		var set = ( TEnum x ) => { kv[name] = x.ToString(); };

		var prop = Game.TypeLibrary.CreateProperty( name, get, set );

		Categories[name] = prop;
	}
}
