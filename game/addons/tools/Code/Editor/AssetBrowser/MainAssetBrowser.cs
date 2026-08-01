using System.Threading;

namespace Editor;

[Dock( "Editor", "Asset Browser", "folder_open", DockArea.Bottom )]
public class MainAssetBrowser : WrappedAssetBrowser
{
	private static WrappedAssetBrowser _instance;
	public static WrappedAssetBrowser Instance
	{
		get
		{
			if ( !_instance.IsValid() ) return null;
			return _instance;
		}
		private set => _instance = value;
	}

	/// <summary>
	/// Creates the primary editor asset browser.
	/// </summary>
	public MainAssetBrowser( Widget parent ) : this( parent, true )
	{
	}

	private MainAssetBrowser( Widget parent, bool isPrimary ) : base( parent, null )
	{
		if ( isPrimary )
			Instance ??= this;

		Local.OnAssetHighlight = a => EditorUtility.InspectorObject = a;
		Local.OnAssetsHighlight = a => EditorUtility.InspectorObject = a;
		Local.OnAssetSelected = a => a.OpenInEditor();
		Local.OnFileSelected = f => EditorUtility.OpenFile( f );

		Cloud.OnPackageHighlight = p => _ = InspectPackage( p );

		Mounts.OnAssetHighlight = a => EditorUtility.InspectorObject = a;
		Mounts.OnAssetsHighlight = a => EditorUtility.InspectorObject = a;
		Mounts.OnAssetSelected = a => { if ( a.CanOpenInEditor ) a.OpenInEditor(); };
	}

	public static MainAssetBrowser CreateFloating()
	{
		var (browser, dock) = CreateDock();
		EditorWindow.DockManager.AddDockFloating( dock );
		return browser;
	}

	public static MainAssetBrowser Create( Widget relativeTo, DockArea area )
	{
		var (browser, dock) = CreateDock( area );
		var relativeDock = EditorWindow.DockManager.FindDockWidget( relativeTo );

		EditorWindow.DockManager.AddDock( dock, area, relativeDock );
		return browser;
	}

	private static (MainAssetBrowser Browser, DockWidget Dock) CreateDock( DockArea area = DockArea.Bottom )
	{
		const string title = "Asset Browser";
		var manager = EditorWindow.DockManager;
		var name = $"{title} 2";

		bool IsTaken( string candidate )
		{
			if ( manager.FindDockWidget( candidate ) is not null )
				return true;

			return manager.DockTypes.Any( x => x.Title == candidate );
		}

		for ( var index = 3; IsTaken( name ); index++ )
			name = $"{title} {index}";

		var browser = new MainAssetBrowser( EditorWindow, false );
		manager.RegisterDockType( new DockManager.DockInfo
		{
			Title = name,
			Icon = "folder_open",
			Area = area,
			CreateAction = () => new MainAssetBrowser( EditorWindow, false )
		} );

		var dock = manager.CreateDockWidget( name, "folder_open", browser );
		dock.DeleteOnClose = true;

		return (browser, dock);
	}

	CancellationTokenSource packageCTS;
	private async Task InspectPackage( Package package )
	{
		packageCTS?.Cancel();

		packageCTS = new CancellationTokenSource();

		// Get the full package info
		package = await Package.FetchAsync( package.FullIdent, false );

		if ( await TryInspectPrimaryAsset( package, packageCTS.Token ) )
			return;

		// Show package info
	}

	async Task<bool> TryInspectPrimaryAsset( Package package, CancellationToken cancel )
	{
		if ( package.TypeName == "map" ) return false;
		if ( package.TypeName == "game" ) return false;
		if ( package.TypeName == "collection" ) return false;
		if ( package.TypeName == "addon" ) return false;
		if ( package.TypeName == "library" ) return false;

		if ( package.GetMeta<string>( "PrimaryAsset" ) is not string assetPath )
			return false;

		if ( cancel.IsCancellationRequested )
			return false;

		var asset = await AssetSystem.InstallAsync( package.FullIdent, true, null, cancel );

		if ( asset is null )
			return false;

		EditorUtility.PlayAssetSound( asset );

		if ( cancel.IsCancellationRequested )
			return false;

		EditorUtility.InspectorObject = asset;
		return true;
	}

	[Event( "tools.editorwindow.postcreateview" )]
	private static void AddViewMenuButtons( Menu menu )
	{
		menu.AddSeparator();
		menu.AddOption( "New Asset Browser", "create_new_folder", () => CreateFloating() );
	}
}
