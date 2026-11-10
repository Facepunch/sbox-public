using System.Threading;
using Editor.MeshEditor;

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
		{
			Instance ??= this;

			EditorWindow.DockManager.OnStateRestoring -= RegisterSavedDockTypes;
			EditorWindow.DockManager.OnStateRestoring += RegisterSavedDockTypes;
		}

		Local.OnAssetHighlight = InspectAsset;
		Local.OnAssetsHighlight = InspectAssets;
		Local.OnAssetSelected = a => a.OpenInEditor();
		Local.OnFileSelected = f => EditorUtility.OpenFile( f );

		Cloud.OnPackageHighlight = p => _ = InspectPackage( p );

		Mounts.OnAssetHighlight = InspectAsset;
		Mounts.OnAssetsHighlight = InspectAssets;
		Mounts.OnAssetSelected = a => { if ( a.CanOpenInEditor ) a.OpenInEditor(); };
	}

	public static MainAssetBrowser CreateFloating()
	{
		var manager = EditorWindow.DockManager;
		var disabled = manager.DockTypes
			.Select( x => (Info: x, Index: GetSecondaryIndex( x.Title )) )
			.Where( x => x.Index is not null && !manager.IsDockOpen( x.Info.Title ) )
			.OrderBy( x => x.Index )
			.FirstOrDefault();

		if ( disabled.Info is not null )
		{
			manager.SetDockState( disabled.Info.Title, true );
			var existing = manager.FindDockWidget( disabled.Info.Title );
			existing.DeleteOnClose = true;
			return existing.Widget as MainAssetBrowser;
		}

		var (browser, dock) = CreateDock();
		manager.AddDockFloating( dock );
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
		manager.RegisterDockType( CreateDockInfo( name, area ) );

		var dock = manager.CreateDockWidget( name, "folder_open", browser );
		dock.DeleteOnClose = true;

		return (browser, dock);
	}

	private static DockManager.DockInfo CreateDockInfo( string name, DockArea area ) => new()
	{
		Title = name,
		Icon = "folder_open",
		Area = area,
		CreateAction = () => new MainAssetBrowser( EditorWindow, false )
	};

	private static void RegisterSavedDockTypes( IReadOnlyCollection<string> dockNames )
	{
		var manager = EditorWindow.DockManager;

		foreach ( var name in dockNames )
		{
			if ( GetSecondaryIndex( name ) is null )
				continue;

			manager.RegisterDock( CreateDockInfo( name, DockArea.Bottom ) );
			manager.FindDockWidget( name ).DeleteOnClose = true;
		}
	}

	private static int? GetSecondaryIndex( string name )
	{
		const string prefix = "Asset Browser ";
		if ( !name.StartsWith( prefix, StringComparison.Ordinal ) )
			return null;

		return int.TryParse( name.AsSpan( prefix.Length ), out var index ) && index >= 2 ? index : null;
	}

	CancellationTokenSource packageCTS;
	private void InspectAsset( Asset asset )
	{
		packageCTS?.Cancel();
		MaterialSelection.BeginSelection();
		EditorUtility.InspectorObject = asset;
		EditorEvent.Run( "asset.highlighted", asset );
	}

	private void InspectAssets( Asset[] assets )
	{
		packageCTS?.Cancel();
		MaterialSelection.BeginSelection();
		EditorUtility.InspectorObject = assets;
	}

	public override void OnDestroyed()
	{
		packageCTS?.Cancel();
		base.OnDestroyed();
	}

	private async Task InspectPackage( Package package )
	{
		packageCTS?.Cancel();

		using var request = new CancellationTokenSource();
		packageCTS = request;
		var cancel = request.Token;
		var generation = MaterialSelection.BeginSelection();

		try
		{
			// Get the full package info
			package = await Package.FetchAsync( package.FullIdent, false );
			if ( package is null || cancel.IsCancellationRequested || !MaterialSelection.IsCurrent( generation ) ) return;

			await TryInspectPrimaryAsset( package, cancel, generation );
		}
		catch ( OperationCanceledException ) when ( cancel.IsCancellationRequested )
		{
		}
		catch ( Exception e )
		{
			Log.Warning( e, "Couldn't inspect cloud asset" );
		}
		finally
		{
			if ( ReferenceEquals( packageCTS, request ) )
				packageCTS = null;
		}
	}

	/// <summary>
	/// Installs and inspects the primary asset unless the selection has been superseded.
	/// </summary>
	/// <returns>Whether the asset was inspected, not whether the captured generation is still current.</returns>
	async Task<bool> TryInspectPrimaryAsset( Package package, CancellationToken cancel, long generation )
	{
		if ( package.TypeName == "map" ) return false;
		if ( package.TypeName == "game" ) return false;
		if ( package.TypeName == "collection" ) return false;
		if ( package.TypeName == "addon" ) return false;
		if ( package.TypeName == "library" ) return false;

		if ( package.GetMeta<string>( "PrimaryAsset" ) is not string )
			return false;

		if ( cancel.IsCancellationRequested || !MaterialSelection.IsCurrent( generation ) )
			return false;

		var asset = await AssetSystem.InstallAsync( package.FullIdent, true, null, cancel );

		if ( asset is null || cancel.IsCancellationRequested || !MaterialSelection.IsCurrent( generation ) )
			return false;

		EditorUtility.PlayAssetSound( asset );

		// This cancels our own request and advances the generation; no async work remains.
		InspectAsset( asset );
		return true;
	}

	[Event( "tools.editorwindow.postcreateview" )]
	private static void AddViewMenuButtons( Menu menu )
	{
		menu.AddSeparator();
		menu.AddOption( "New Asset Browser", "create_new_folder", () => CreateFloating() );
	}
}
