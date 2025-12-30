using Sandbox.Helpers;

namespace Editor.RectEditor;

[EditorForAssetType( "rect" )]
[EditorApp( "Hotspot Editor", "dashboard", "For defining hotspot materials" )]
public class HotspotEditorWindow : Window
{
	// This is separate so that classes that derive from Window don't show up in Editor Apps
	public HotspotEditorWindow() : base()
	{
		WindowTitle = "Hotspot Editor";
	}
}

public partial class Window : DockWindow, IAssetEditor
{
	public bool CanOpenMultipleAssets => false;

	public Document Document { get; private set; } = new();
	public Settings Settings { get; private set; } = new();

	protected Asset Asset;
	protected Asset[] MaterialReferences;

	protected ToolBar ToolBar;
	protected RectView RectView;
	protected Properties Properties;
	protected MaterialReference MaterialReference;

	private UndoSystem UndoSystem;

	private string DefaultDockState;

	public int GridPower { get; set; } = 4;
	public bool GridEnabled { get; set; } = true;

	public Window()
	{
		Name = "RectEditor";
		DeleteOnClose = true;
		Size = new Vector2( 1000, 700 );

		if ( AssetType.All.FirstOrDefault( x => x.FileExtension == "rect" ) is AssetType assetType )
			SetWindowIcon( assetType.Icon128 );

		InitUndo();
		SetupSettingsCallbacks();
		NewDocument();
		CreateUI();
	}

	private void InitUndo()
	{
		UndoSystem = new UndoSystem();
		UndoSystem.Initialize();
	}

	private void SetupSettingsCallbacks()
	{
		Settings.FastTextureSettings.OnMappingChanged = OnMappingModeChanged;
		Settings.FastTextureSettings.OnSettingsChanged = OnFastTextureSettingsChanged;
	}

	public virtual void OnFastTextureSettingsChanged()
	{
	}



	[Shortcut( "editor.quit", "CTRL+Q" )]
	void Quit()
	{
		Close();
	}

	[Shortcut( "editor.undo", "CTRL+Z" )]
	private bool Undo()
	{
		return UndoSystem.Undo();
	}

	[Shortcut( "editor.redo", "CTRL+Y" )]
	private bool Redo()
	{
		return UndoSystem.Redo();
	}

	public void ExecuteUndoableAction( string title, Action action )
	{
		var preState = Json.Serialize( Document );

		action.Invoke();

		var postState = Json.Serialize( Document );

		UndoSystem.Insert( title,
			() =>
			{
				Document = Json.Deserialize<Document>( preState );
				Document.OnModified = OnDocumentModified;
				OnDocumentModified();
			},
			() =>
			{
				Document = Json.Deserialize<Document>( postState );
				Document.OnModified = OnDocumentModified;
				OnDocumentModified();
			} );
	}

	[Shortcut( "editor.delete", "DEL" )]
	private void DeleteSelected()
	{
		if ( !Document.SelectedRectangles.Any( x => x.CanDelete ) )
			return;

		ExecuteUndoableAction( "Delete Rectangles", () => Document.DeleteRectangles( Document.SelectedRectangles.ToArray() ) );
	}

	[Shortcut( "editor.clear-selection", "ESC" )]
	private void ClearSelection()
	{
		if ( !Document.SelectedRectangles.Any() )
			return;

		ExecuteUndoableAction( "Clear Selection", () => Document.ClearSelection() );
	}

	[Shortcut( "editor.select-all", "CTRL+A" )]
	private void SelectAll()
	{
		ExecuteUndoableAction( "Select All", () => Document.SelectAll() );
	}

	private void CreateToolBar()
	{
		ToolBar = new ToolBar( this, "RectToolbar" );
		ToolBar.MinimumSize = 24;
		ToolBar.SetIconSize( 24 );

		AddToolBar( ToolBar, ToolbarPosition.Top );

		ToolBar.AddOption( new Option( "New", "common/new.png", New ) { ShortcutName = "editor.new" } );
		ToolBar.AddOption( new Option( "Open", "common/open.png", Open ) { ShortcutName = "editor.open" } );
		ToolBar.AddOption( new Option( "Save", "common/save.png", Save ) { ShortcutName = "editor.save" } );

		ToolBar.AddSeparator();

		ToolBar.AddOption( new Option( "Undo", "undo", () => Undo() ) { ShortcutName = "editor.undo" } );
		ToolBar.AddOption( new Option( "Redo", "redo", () => Redo() ) { ShortcutName = "editor.redo" } );

		ToolBar.AddSeparator();

		ToolBar.AddOption( new Option( "Grid Enabled", "grid_4x4" )
		{
			ShortcutName = "grid.toggle-grid",
			Checkable = true,
			Checked = GridEnabled,
			Toggled = SetGridEnabled
		} );

		ToolBar.AddOption( new Option( "Decrease Grid Size", "keyboard_arrow_down", SmallerGrid ) { ShortcutName = "grid.decrease-grid-size" } );
		ToolBar.AddOption( new Option( "Increase Grid Size", "keyboard_arrow_up", BiggerGrid ) { ShortcutName = "grid.increase-grid-size" } );

		ToolBar.AddSeparator();

		ToolBar.AddOption( new Option( "Normalized Values", "photo_size_select_small" )
		{
			Checkable = true,
			Checked = Settings.ShowNormalizedValues,
			Toggled = SetNormalizedValues
		} );

		ToolBar.AddSeparator();
	}

	protected virtual void BuildDock()
	{
		DockManager.RegisterDockType( "Rect View", "space_dashboard", null, false );
		RectView = new RectView( this );
		DockManager.AddDock( null, RectView, DockArea.Right, DockManager.DockProperty.HideOnClose, 0.0f );

		DockManager.RegisterDockType( "Properties", "edit", null, false );
		Properties = new Properties( this );
		UpdateProperties();
		DockManager.AddDock( null, Properties, DockArea.Left, DockManager.DockProperty.HideOnClose, 0.4f );

		DockManager.RegisterDockType( "Material Reference", "texture", null, false );
		MaterialReference = new MaterialReference( this, OnReferenceChanged );
		MaterialReference.SetReferences( MaterialReferences );
		DockManager.AddDock( Properties, MaterialReference, DockArea.Bottom, DockManager.DockProperty.HideOnClose, 0.4f );
	}

	protected virtual void InitRectanglesFromMeshFaces()
	{
	}

	private void SetNormalizedValues( bool enabled )
	{
		Settings.ShowNormalizedValues = enabled;
		UpdateProperties();
		Update();
	}

	[Shortcut( "grid.toggle-grid", "CTRL+G" )]
	private void SetGridEnabled( bool enabled )
	{
		GridEnabled = enabled;

		Update();
	}

	[Shortcut( "grid.increase-grid-size", "]" )]
	private void BiggerGrid()
	{
		GridPower = Math.Max( GridPower - 1, 1 );

		UpdateGridSize();
		Update();
	}

	[Shortcut( "grid.decrease-grid-size", "[" )]
	private void SmallerGrid()
	{
		GridPower = Math.Min( GridPower + 1, 8 );

		UpdateGridSize();
		Update();
	}

	private void UpdateGridSize()
	{
		var size = GetImageSize();
		var imageSize = MathF.Max( size.x, size.y );
		Settings.GridSize = (int)MathF.Round( imageSize / (1 << GridPower) );
	}

	internal Vector2 GetImageSize()
	{
		if ( RectView?.SourceImage is null )
			return new Vector2( 512, 512 );
		return new Vector2( RectView.SourceImage.Width, RectView.SourceImage.Height );
	}

	private void CreateUI()
	{
		CreateToolBar();
		CreateMenu();
		UpdateTitle();
		BuildDock();

		var previousMat = AssetSystem.FindByPath( Settings.ReferenceMaterial );
		if ( previousMat is not null )
		{
			MaterialReference.Select( previousMat );
		}

		DockManager.Update();
		DefaultDockState = DockManager.State;

		if ( StateCookie != Name )
		{
			StateCookie = Name;
		}
		else
		{
			RestoreFromStateCookie();
		}
	}

	protected void OnDocumentModified()
	{
		UpdateTitle();
		UpdateProperties();
		UpdateMeshFaces();
		Update();
	}

	protected virtual void UpdateMeshFaces()
	{
	}

	public virtual void OnMappingModeChanged()
	{
	}
	protected virtual void UpdateProperties()
	{
		if ( !Properties.IsValid() )
			return;

		if ( Document.SelectedRectangles.Count > 1 )
		{
			var me = new MultiSerializedObject();

			foreach ( var rectangle in Document.SelectedRectangles )
			{
				var so = rectangle.GetSerialized();
				if ( so is not null )
				{
					me.Add( so );
				}
			}

			me.Rebuild();

			Properties.SerializedObject = me;
		}
		else if ( Document.SelectedRectangles.Count > 0 )
		{
			Properties.SerializedObject = Document.SelectedRectangles.FirstOrDefault()?.GetSerialized();
		}
		else
		{
			Properties.SerializedObject = Settings.GetSerialized();
		}
	}

	protected virtual void UpdateTitle()
	{
		var title = Asset is null ? "Untitled" : Asset.Path;
		WindowTitle = Document is not null && Document.Modified ? $"{title}*" : title;
	}

	protected virtual void OnReferenceChanged( Asset asset )
	{
		var material = asset?.LoadResource<Material>();
		Settings.ReferenceMaterial = material?.ResourcePath;
		RectView.SetMaterial( material );
	}

	public void CreateMenu()
	{
		var edit = MenuBar.AddMenu( "Edit" );
		edit.AddOption( new Option( "Select All", null, SelectAll ) { ShortcutName = "editor.select-all" } );
		edit.AddOption( new Option( "Clear Selection", null, ClearSelection ) { ShortcutName = "editor.clear-selection" } );
		edit.AddOption( new Option( "Delete Selected", null, DeleteSelected ) { ShortcutName = "editor.delete" } );

		var view = MenuBar.AddMenu( "View" );
		view.AboutToShow += () => OnViewMenu( view );
	}

	private void OnViewMenu( Menu view )
	{
		view.Clear();
		view.AddOption( "Restore To Default", "settings_backup_restore", RestoreDefaultDockLayout );
		view.AddSeparator();

		foreach ( var dock in DockManager.DockTypes )
		{
			var o = view.AddOption( dock.Title, dock.Icon );
			o.Checkable = true;
			o.Checked = DockManager.IsDockOpen( dock.Title );
			o.Toggled += ( b ) => DockManager.SetDockState( dock.Title, b );
		}
	}

	public void AssetOpen( Asset asset )
	{
		Show();

		var assetData = RectAssetData.Find( asset );
		if ( assetData is null )
			return;

		Asset = asset;
		Document = new Document( this, assetData, OnDocumentModified );
		Settings = assetData.Settings ?? new Settings();
		SetupSettingsCallbacks();

		MaterialReferences = AssetSystem.All.Where( x => x.AssetType == AssetType.Material && x.GetAdditionalRelatedFiles()
			.Any( x => x.Contains( asset.Name ) ) )
			.ToArray();
		MaterialReference.SetReferences( MaterialReferences );

		var previousMat = AssetSystem.FindByPath( Settings.ReferenceMaterial );
		if ( previousMat is not null )
		{
			MaterialReference.Select( previousMat );
		}

		UndoSystem.Initialize();
		OnDocumentModified();
	}

	protected override void OnClosed()
	{
		base.OnClosed();
	}

	[Shortcut( "editor.new", "CTRL+N" )]
	private void New()
	{
		NewDocument();
	}

	private void NewDocument()
	{
		Document = new Document();
		Document.OnModified = OnDocumentModified;
		Asset = null;
		MaterialReferences = null;
		MaterialReference?.SetReferences( null );

		UndoSystem.Initialize();

		OnDocumentModified();
	}

	[Shortcut( "editor.open", "CTRL+O" )]
	private void Open()
	{
		var fd = new FileDialog( null )
		{
			Title = "Open Rect",
			DefaultSuffix = $".rect"
		};

		fd.SetNameFilter( "Rect (*.rect)" );

		if ( !fd.Execute() )
			return;

		Open( fd.SelectedFile );
	}

	public void Open( string path )
	{
		var asset = AssetSystem.FindByPath( path );
		if ( asset is null )
			return;

		if ( asset.AssetType.FileExtension != "rect" )
			return;

		AssetOpen( asset );
	}


	[Shortcut( "editor.save", "CTRL+S" )]
	private void Save()
	{
		if ( Document is null )
			return;

		string savePath = null;

		if ( Asset is null )
		{
			var fd = new FileDialog( null )
			{
				Title = $"Save Rect",
				DefaultSuffix = $".rect"
			};

			fd.SelectFile( $"untitled.rect" );
			fd.SetFindFile();
			fd.SetModeSave();
			fd.SetNameFilter( "Rect (*.rect)" );
			if ( !fd.Execute() )
				return;

			savePath = fd.SelectedFile;
			if ( string.IsNullOrWhiteSpace( savePath ) )
				return;
		}
		else if ( AssetSystem.IsCloudInstalled( Asset.Package ) )
		{
			return;
		}

		var assetData = new RectAssetData
		{
			RectangleSets = new List<RectAssetData.SubrectSet>(),
			Settings = Settings
		};

		var subrectSet = new RectAssetData.SubrectSet
		{
			Rectangles = new List<RectAssetData.Subrect>()
		};

		foreach ( var rectangle in Document.Rectangles )
		{
			subrectSet.Rectangles.Add( new RectAssetData.Subrect
			{
				Min = new int[] { (int)(rectangle.Min.x.Clamp( 0, 1 ) * 32768), (int)(rectangle.Min.y.Clamp( 0, 1 ) * 32768) },
				Max = new int[] { (int)(rectangle.Max.x.Clamp( 0, 1 ) * 32768), (int)(rectangle.Max.y.Clamp( 0, 1 ) * 32768) },
				Properties = new RectAssetData.Properties
				{
					AllowRotation = rectangle.AllowRotation,
					AllowTiling = rectangle.AllowTiling,
				}
			} );
		}

		assetData.RectangleSets.Add( subrectSet );

		var path = Asset is null ? savePath : Asset.GetSourceFile( true );
		System.IO.File.WriteAllText( path, Json.Serialize( assetData ) );
		Asset ??= AssetSystem.RegisterFile( savePath );

		MainAssetBrowser.Instance?.Local.UpdateAssetList();

		Document.Modified = false;

		OnDocumentModified();
	}

	public void SelectMember( string memberName )
	{
		throw new NotImplementedException();
	}

	protected override void RestoreDefaultDockLayout()
	{
		DockManager.State = DefaultDockState;

		SaveToStateCookie();
	}

	[EditorEvent.Hotload]
	public void OnHotload()
	{
		SaveToStateCookie();

		RemoveToolBar( ToolBar );
		ToolBar.Destroy();
		ToolBar = null;

		DockManager.Clear();
		MenuBar.Clear();

		CreateUI();
	}
}
