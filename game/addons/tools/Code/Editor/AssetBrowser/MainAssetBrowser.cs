using System.Threading;

namespace Editor;

[Dock( "Editor", "Asset Browser", "folder_open" )]
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
	/// This constructor should only get called by the Docked version created by the editor.
	/// </summary>
	public MainAssetBrowser( Widget parent ) : base( parent, null )
	{
		Instance ??= this;

		Local.OnAssetHighlight = a => EditorUtility.InspectorObject = a;
		Local.OnAssetsHighlight = a => EditorUtility.InspectorObject = a;
		Local.OnAssetSelected = a => a.OpenInEditor();
		Local.OnFileSelected = f => EditorUtility.OpenFile( f );

		Cloud.OnPackageHighlight = p => _ = InspectPackage( p );

		Mounts.OnAssetHighlight = a => EditorUtility.InspectorObject = a;
		Mounts.OnAssetsHighlight = a => EditorUtility.InspectorObject = a;
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
		menu.AddOption( "New Asset Browser", "create_new_folder", () => EditorWindow.DockManager.Create<MainAssetBrowser>() );
		menu.AddOption( "Go to coordinates", "control_camera", () => Dialog.AskString( GoToCoordinates, "Where do you want to go?", "Go", title: "Go to coordinates", minLength: 5 ) );
	}

	static void GoToCoordinates( string coordinates )
	{
		var parsedCoordinates = Vector3.Parse( coordinates );
		SceneViewportWidget.LastSelected?.cameraTargetPosition = parsedCoordinates;
	}
}
