using System.IO;

namespace Editor.AssetPickers;

/// <summary>
/// An asset browser allowing the user to pick a single asset.
/// Supports limiting display to only certain asset types.
/// </summary>
public class GenericPicker : AssetPicker
{
	/// <summary>
	/// Internal asset browser.
	/// </summary>
	public LocalAssetBrowser AssetBrowser { get; protected set; }

	/// <summary>
	/// Internal cloud browser.
	/// </summary>
	public CloudAssetBrowser CloudBrowser { get; protected set; }

	/// <summary>
	/// The picked assets.
	/// </summary>
	public List<Asset> Assets { get; protected set; }

	internal Button ConfirmButton;
	Button CancelButton;

	public GenericPicker( Widget parent, List<AssetType> assetTypes, PickerOptions options ) : base( parent, null, options )
	{
		Window.Size = new Vector2( 1280, 720 );
		Window.SetModal( true );
		Window.MinimumSize = 200;
		Window.MaximumSize = 10000;
		Window.Title = $"Select {string.Join( ", ", assetTypes?.Select( x => x.FriendlyName ) ?? new List<string>() )} - Asset Picker";
		Window.StatusBar = new StatusBar( this );

		Window.StateCookie = "AssetPicker";
		Window.RestoreFromStateCookie();

		Layout = Layout.Column();
		CreateUI( assetTypes );
	}

	internal DockManager DockManager;

	void CreateUI( List<AssetType> assetTypes )
	{
		DockManager = new DockManager( this );
		DockManager.DockProperty properties = DockManager.DockProperty.HideCloseButton
			| DockManager.DockProperty.DisallowUserDocking | DockManager.DockProperty.DisableDraggableTab;

		AssetBrowser = new LocalAssetBrowser( DockManager, assetTypes );
		AssetBrowser.WindowTitle = "Asset Browser";
		AssetBrowser.SetWindowIcon( "folder" );

		AssetBrowser.OnAssetHighlight += Highlight;
		AssetBrowser.OnAssetsHighlight += Highlight;
		AssetBrowser.OnAssetSelected += ( a ) => Select();
		AssetBrowser.OnHighlight += Highlight;
		AssetBrowser.MultiSelect = Options.EnableMultiselect;
		DockManager.AddDock( null, AssetBrowser, DockArea.Inside, properties );

		if ( Options.EnableCloud )
		{
			CloudBrowser = new CloudAssetBrowser( DockManager, assetTypes );
			CloudBrowser.WindowTitle = "Cloud Browser";
			CloudBrowser.SetWindowIcon( "cloud_download" );

			CloudBrowser.OnPackageHighlight = HighlightPackage;
			CloudBrowser.OnPackageSelected = ( a ) => Select();
			CloudBrowser.MultiSelect = Options.EnableMultiselect;
			DockManager.AddDock( null, CloudBrowser, DockArea.Inside, properties );
		}

		Layout.Add( DockManager, 1 );
		Layout.AddSeparator();

		var bottom = Layout.AddRow();
		bottom.Spacing = 10;
		bottom.Margin = 10;
		bottom.AddStretchCell();

		ConfirmButton = bottom.Add( new Button.Primary( "Select" ) );
		ConfirmButton.Enabled = false;
		ConfirmButton.Clicked = Select;

		CancelButton = bottom.Add( new Button( "Cancel" ) );
		CancelButton.Enabled = true;
		CancelButton.Clicked = Close;
	}

	public override void SetSelection( Asset asset )
	{
		if ( asset is null )
		{
			DockManager.RaiseDock( AssetBrowser );
			AssetBrowser.NavigateTo( Project.Current.GetAssetsPath() );
			return;
		}

		Package package = asset?.Package;
		if ( package is not null )
		{
			SetSelection( package );
		}
		else
		{
			DockManager.RaiseDock( AssetBrowser );
			AssetBrowser.FocusOnAsset( asset );
		}
	}

	public override void SetSelection( Package package )
	{
		if ( package is null )
			return;

		DockManager.RaiseDock( CloudBrowser );
		CloudBrowser.Search.Value += $" {package.FullIdent}";
		CloudBrowser.FocusOnPackage( package );
	}

	void Select()
	{
		if ( AssetBrowser.Visible )
		{
			var assets = AssetBrowser.GetSelected<AssetEntry>().Select( x => x.Asset ).ToList();

			if ( !IsSelectionValid( assets ) )
				return;

			Submit( assets.ToArray() );
		}
		else if ( CloudBrowser.Visible )
		{
			Package package = CloudBrowser.GetSelected<PackageEntry>().FirstOrDefault().Package;
			if ( package is not null )
			{
				Submit( package );
			}
		}
		else
		{
			Window.Close();
		}
	}


	bool IsSelectionValid( List<Asset> assets ) => assets.TrueForAll( IsSelectionValid );

	bool IsSelectionValid( Asset asset )
	{
		// If no asset types are selected, allow anything to be selected
		if ( AssetBrowser.FilterAssetTypes?.Count == 0 )
		{
			return true;
		}

		if ( asset is null )
		{
			return false;
		}

		if ( !AssetBrowser.FilterAssetTypes?.Contains( asset.AssetType ) ?? false )
		{
			return false;
		}

		return true;
	}

	bool IsSelectionValid( Package package )
	{
		if ( package is null )
			return false;

		if ( package.TypeName == "map" )
		{
			// special case because these can be vpks, not assets
			return AssetBrowser.FilterAssetTypes.Contains( AssetType.MapFile );
		}

		var primaryAssetName = package.GetCachedMeta<string>( "PrimaryAsset" );
		string ext = Path.GetExtension( primaryAssetName );
		if ( !AssetBrowser.FilterAssetTypes?.Any( x => ext == $".{x.FileExtension}" ) ?? false )
		{
			return false;
		}

		return true;
	}

	void Highlight( Asset a )
	{
		bool isValid = IsSelectionValid( a );
		if ( isValid )
		{
			Assets = new List<Asset>() { a };
			OnAssetHighlighted?.Invoke( Assets.ToArray() );
			ConfirmButton.Enabled = isValid;
			BindSystem.Flush();
		}

		EditorUtility.PlayAssetSound( a );

		ConfirmButton.Enabled = isValid;
	}

	void Highlight( Asset[] a )
	{
		var assets = new List<Asset>( a );
		bool isValid = IsSelectionValid( assets );
		if ( isValid )
		{
			Assets = assets;
			OnAssetHighlighted?.Invoke( Assets.ToArray() );
			BindSystem.Flush();
		}

		ConfirmButton.Enabled = isValid;
	}

	void Highlight( IEnumerable<IAssetListEntry> entries )
	{
		if ( entries.Any( x => x is not AssetEntry or PackageEntry ) )
		{
			// not something we can pick
			ConfirmButton.Enabled = false;
		}
	}

	async void HighlightPackage( Package package )
	{
		ConfirmButton.Enabled = false;

		if ( AssetSystem.CanCloudInstall( package ) )
		{
			var a = await AssetSystem.InstallAsync( package.FullIdent );

			if ( !IsValid ) return;

			if ( a is not null )
			{
				EditorUtility.PlayAssetSound( a );

				Assets = new List<Asset>() { a };
				OnAssetHighlighted?.Invoke( Assets.ToArray() );
			}
		}

		package = await Package.FetchAsync( package.FullIdent, false );
		if ( !IsValid ) return;

		ConfirmButton.Enabled = IsSelectionValid( package );
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		base.OnKeyPress( e );

		if ( e.Key == KeyCode.Escape )
		{
			Close();
		}
	}

	/// <summary>
	/// Called from native to open an asset picker
	/// </summary>
	[Event( "assetsystem.openpicker" )]
	static void OpenAssetPicker( AssetSystem.AssetPickerParameters parameters )
	{
		// TODO: Maybe should pass a parent widget which would be the callback listener?
		AssetPicker picker = null;

		var options = new PickerOptions()
		{
			EnableCloud = parameters.ShowCloudAssets,
			AdditionalTypes = parameters.FilterAssetTypes
		};

		if ( parameters.FilterAssetTypes.Select( x => x.ResourceType ).Distinct().Where( x => x != null ).Count() == 1 )
		{
			picker = Create( parameters.ParentWidget, parameters.FilterAssetTypes[0], options );
		}
		else
		{
			picker = new GenericPicker( parameters.ParentWidget, parameters.FilterAssetTypes, options );
		}

		if ( picker is GenericPicker )
		{
			var gp = picker as GenericPicker;

			// parameters.ViewMode is 0 for list, 1 for grid, try to preserve icon size from the asset browser cookie
			if ( (parameters.ViewMode == 0 && gp.AssetBrowser.ViewModeType != AssetListViewMode.List) ||
				(parameters.ViewMode == 1 && gp.AssetBrowser.ViewModeType == AssetListViewMode.List) )
			{
				gp.AssetBrowser.ViewModeType = (AssetListViewMode)parameters.ViewMode;
			}

		}

		picker.OnAssetPicked = parameters.AssetSelectedCallback;

		//
		// Native can pass down a title which it also wants to use as a settings key
		//
		if ( !string.IsNullOrEmpty( parameters.Title ) )
		{
			picker.Title = parameters.Title;
			// TODO: Use the title as the AssetBrowser cookie (does it make sense for the window StateCookie too?)
		}

		picker.Show();

		//
		// Focus on the initial asset if we have one ( e.g whatever the current asset already is )
		// Do this after the window is shown so it can scroll properly
		//
		picker.SetSelection( parameters.InitialSelectedAsset );
		picker.SetSearchText( parameters.InitialSearchText );
	}
}
