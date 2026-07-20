using Sandbox.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Editor.SpriteSheetEditor;

/// <summary>
/// Cuts a sheet of artwork into named, individually-pivoted slices.
/// <para>
/// The pivot is the part that matters beyond just chopping an image up: putting a thigh's pivot on
/// the hip and a calf's on the knee is what lets those parts be parented together into a character
/// that bends in the right places.
/// </para>
/// </summary>
[EditorForAssetType( "sheet" )]
[EditorApp( "Sprite Sheet Editor", "grid_view", "Slice sprite sheets into parts" )]
public class Window : DockWindow, IAssetEditor
{
	public bool CanOpenMultipleAssets => true;

	public SpriteSheet Sheet { get; private set; }

	/// <summary>The asset being edited, or null if it has never been saved.</summary>
	public Asset Asset => _asset;

	/// <summary>
	/// What is selected, held by id rather than by reference. Undo swaps the whole resource out from
	/// under us, so anything holding <see cref="SpriteSheet.Slice"/> objects would be pointing at
	/// detached copies the moment someone hit ctrl-Z.
	/// </summary>
	public List<Guid> Selection { get; } = new();

	public IEnumerable<SpriteSheet.Slice> SelectedSlices
		=> Selection.Select( id => Sheet?.GetSlice( id ) ).Where( x => x is not null );

	public SpriteSheet.SliceSettings SliceSettings { get; set; } = new();

	public Action OnSheetLoaded { get; set; }
	public Action OnSheetModified { get; set; }
	public Action OnSelectionChanged { get; set; }

	public UndoSystem UndoStack;

	Asset _asset;
	bool _isModified;

	Pixmap _sourcePixmap;
	Color32[] _sourcePixels;
	int _sourceWidth;
	int _sourceHeight;
	string _loadedImagePath;

	ToolBar _toolBar;
	SheetCanvas _canvas;
	Option _undoMenuOption;
	Option _redoMenuOption;
	Option _undoOption;
	Option _redoOption;

	public Window()
	{
		DeleteOnClose = true;
		Size = new Vector2( 1280, 800 );

		Sheet = new SpriteSheet();

		var saved = EditorCookie.Get<SpriteSheet.SliceSettings>( "SpriteSheetEditor.SliceSettings", null );
		if ( saved is not null ) SliceSettings = saved;

		UndoStack = new UndoSystem();
		UndoStack.Initialize();
		UndoStack.OnUndo += _ => AfterUndoRedo();
		UndoStack.OnRedo += _ => AfterUndoRedo();

		SetWindowIcon( "grid_view" );

		CreateDocks();
		RebuildUI();
		Show();
		StateCookie = "SpriteSheetEditor";
	}

	public void AssetOpen( Asset asset )
	{
		Raise();
		Open( null, asset );
	}

	public void SelectMember( string memberName ) { }

	void CreateDocks()
	{
		_canvas = new SheetCanvas( this );

		DockManager.AddDock( "Sheet", "grid_view", _canvas, DockArea.Center );
		DockManager.AddDock( "Slice", "edit", new SliceInspector( this ), DockArea.Right );
		DockManager.AddDock( "Slices", "list", new SliceListPanel( this ), DockArea.Bottom );
	}

	protected override void BuildDefaultLayout()
	{
		var sheet = DockManager.OpenDock( "Sheet", DockArea.Center );
		var inspector = DockManager.OpenDock( "Slice", DockArea.Right );
		var list = DockManager.OpenDock( "Slices", DockArea.Bottom );

		DockManager.SetSplitterProportions( sheet, 0.78f, 0.22f );
		DockManager.SetSplitterProportions( list, 0.75f, 0.25f );
	}

	//
	// The source image
	//

	/// <summary>The sheet's image ready to paint onto the canvas.</summary>
	public Pixmap SourcePixmap { get { EnsureImageLoaded(); return _sourcePixmap; } }

	/// <summary>
	/// The image's pixels, for slicing and trimming. Held onto because a 4096x4096 sheet is 67MB of
	/// Color32 and re-reading it per interaction would make trimming feel broken.
	/// </summary>
	public Color32[] SourcePixels { get { EnsureImageLoaded(); return _sourcePixels; } }

	public int SourceWidth { get { EnsureImageLoaded(); return _sourceWidth; } }
	public int SourceHeight { get { EnsureImageLoaded(); return _sourceHeight; } }

	/// <summary>
	/// Load the sheet's image, taking the size, the picture and the pixels from one decode of the
	/// source file.
	/// </summary>
	/// <remarks>
	/// All three have to come from the same place. A <see cref="Texture"/> is not necessarily the size
	/// of the file it came from - it can be resized or padded on the way in - so measuring the canvas
	/// from a texture while slicing pixels from the file puts the rects in a different space to the
	/// picture they are drawn over. The bitmap is the file, so it is the one to trust.
	/// </remarks>
	void EnsureImageLoaded()
	{
		var path = Sheet?.ImagePath;

		if ( string.IsNullOrWhiteSpace( path ) )
		{
			ClearImage();
			_loadedImagePath = null;
			return;
		}

		if ( path == _loadedImagePath ) return;

		_loadedImagePath = path;
		ClearImage();

		try
		{
			var bytes = FileSystem.Mounted.ReadAllBytes( path ).ToArray();
			using var bitmap = Bitmap.CreateFromBytes( bytes );

			if ( bitmap is null )
			{
				Log.Warning( $"Sprite Sheet Editor could not read image: {path}" );
				return;
			}

			_sourceWidth = bitmap.Width;
			_sourceHeight = bitmap.Height;
			_sourcePixels = bitmap.GetPixels32();
			_sourcePixmap = Pixmap.FromBitmap( bitmap );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Sprite Sheet Editor could not read image {path}: {e.Message}" );
			ClearImage();
		}
	}

	void ClearImage()
	{
		_sourcePixmap = null;
		_sourcePixels = null;
		_sourceWidth = 0;
		_sourceHeight = 0;
	}

	/// <summary>Forget the cached image, so the next use picks up a changed file or path.</summary>
	public void InvalidateImage()
	{
		_loadedImagePath = null;
		EnsureImageLoaded();
	}

	public void PickSourceImage()
	{
		var picker = AssetPicker.Create( this, AssetType.ImageFile, new()
		{
			EnableMultiselect = false,
			EnableCloud = false
		} );

		picker.Window.StateCookie = "SpriteSheetEditor.PickImage";
		picker.Window.RestoreFromStateCookie();
		picker.Window.Title = "Pick a sheet image";

		picker.OnAssetPicked = assets =>
		{
			var asset = assets.FirstOrDefault();
			if ( asset is null ) return;

			SetSourceImage( asset.RelativePath );
		};

		picker.Window.Show();
	}

	void SetSourceImage( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return;
		if ( Sheet is null ) return;
		if ( Sheet.ImagePath == path ) return;

		ExecuteUndoableAction( "Set Sheet Image", () => Sheet.ImagePath = path );
		InvalidateImage();

		DetectBackground();

		OnSheetLoaded?.Invoke();
	}

	/// <summary>
	/// Work out whether this sheet has a solid background colour behind the artwork, and set it up to
	/// be treated as empty space if so.
	/// </summary>
	/// <remarks>
	/// Done on the way in rather than left as a setting to find. Artwork very often arrives as a JPEG
	/// or a scan with flat white behind it, and with no alpha to go on the whole image reads as one
	/// solid lump - automatic slicing returns a single rect covering everything, which looks like the
	/// feature is broken rather than like the image needs configuring.
	/// </remarks>
	public void DetectBackground()
	{
		var pixels = SourcePixels;
		if ( pixels is null || Sheet is null ) return;

		var detected = SpriteSheet.DetectBackgroundColor( pixels, SourceWidth, SourceHeight );
		if ( detected is null ) return;

		ExecuteUndoableAction( "Detect Background", () =>
		{
			Sheet.KeyBackground = true;
			Sheet.BackgroundColor = detected.Value;
		} );
	}

	//
	// Selection
	//

	public void Select( SpriteSheet.Slice slice, bool add = false )
	{
		if ( !add ) Selection.Clear();

		if ( slice is not null )
		{
			if ( add && Selection.Contains( slice.Id ) ) Selection.Remove( slice.Id );
			else if ( !Selection.Contains( slice.Id ) ) Selection.Add( slice.Id );
		}

		OnSelectionChanged?.Invoke();
	}

	public void SelectAll()
	{
		Selection.Clear();
		if ( Sheet?.Slices is not null )
		{
			Selection.AddRange( Sheet.Slices.Select( s => s.Id ) );
		}
		OnSelectionChanged?.Invoke();
	}

	public void ClearSelection()
	{
		if ( Selection.Count == 0 ) return;
		Selection.Clear();
		OnSelectionChanged?.Invoke();
	}

	//
	// Editing slices
	//

	public void AddSlice( Rect rect )
	{
		if ( Sheet is null ) return;

		SpriteSheet.Slice created = null;

		ExecuteUndoableAction( "Add Slice", () =>
		{
			created = new SpriteSheet.Slice
			{
				Rect = rect,
				Alignment = SliceSettings.Alignment,
				Pivot = SliceSettings.Alignment == SpriteSheet.PivotAlignment.Custom
					? SliceSettings.CustomPivot
					: SpriteSheet.Slice.GetPivot( SliceSettings.Alignment )
			};

			created.Name = Sheet.GetUniqueName( $"{_asset?.Name ?? "slice"}_{Sheet.Slices.Count}" );
			Sheet.Slices.Add( created );
		} );

		if ( created is not null ) Select( created );
	}

	public void DeleteSelected()
	{
		var doomed = SelectedSlices.Select( s => s.Id ).ToList();
		if ( doomed.Count == 0 ) return;

		ExecuteUndoableAction( doomed.Count == 1 ? "Delete Slice" : $"Delete {doomed.Count} Slices",
			() => Sheet.Slices.RemoveAll( s => doomed.Contains( s.Id ) ) );

		ClearSelection();
	}

	public void DuplicateSelected()
	{
		var source = SelectedSlices.ToList();
		if ( source.Count == 0 ) return;

		var created = new List<Guid>();

		ExecuteUndoableAction( source.Count == 1 ? "Duplicate Slice" : $"Duplicate {source.Count} Slices", () =>
		{
			foreach ( var slice in source )
			{
				// Offset a little so the copy is visibly its own thing rather than hidden underneath.
				var copy = new SpriteSheet.Slice
				{
					Rect = new Rect( slice.Rect.Left + 8, slice.Rect.Top + 8, slice.Rect.Width, slice.Rect.Height ),
					Pivot = slice.Pivot,
					Alignment = slice.Alignment,
					Border = slice.Border
				};

				copy.Name = Sheet.GetUniqueName( slice.Name );
				Sheet.Slices.Add( copy );
				created.Add( copy.Id );
			}
		} );

		Selection.Clear();
		Selection.AddRange( created );
		OnSelectionChanged?.Invoke();
	}

	/// <summary>
	/// Shrink the selected slices to the artwork inside them. A slice with nothing in it is removed,
	/// because an empty rect is not something anyone meant to keep.
	/// </summary>
	public void TrimSelected()
	{
		var pixels = SourcePixels;
		if ( pixels is null ) return;

		var slices = SelectedSlices.ToList();
		if ( slices.Count == 0 ) return;

		ExecuteUndoableAction( slices.Count == 1 ? "Trim Slice" : $"Trim {slices.Count} Slices", () =>
		{
			foreach ( var slice in slices )
			{
				var trimmed = SpriteSheet.Trim( pixels, SourceWidth, SourceHeight, slice.Rect,
					SliceSettings.AlphaTolerance, Sheet.Background );

				if ( trimmed.Width < 1 || trimmed.Height < 1 )
				{
					Sheet.Slices.Remove( slice );
					continue;
				}

				slice.Rect = trimmed;
			}
		} );

		Selection.RemoveAll( id => Sheet.GetSlice( id ) is null );
		OnSelectionChanged?.Invoke();
	}

	/// <summary>
	/// Run a slicing pass with the current settings.
	/// </summary>
	public void ApplySlicing()
	{
		var pixels = SourcePixels;
		if ( pixels is null )
		{
			Log.Warning( "Sprite Sheet Editor: no image to slice." );
			return;
		}

		var prefix = _asset?.Name ?? "slice";

		// The sheet owns what counts as empty space; the settings just carry it into the algorithms.
		SliceSettings.Background = Sheet.Background;

		ExecuteUndoableAction( $"Slice ({SliceSettings.Type})", () =>
		{
			Sheet.Slices = SpriteSheet.CreateSlices( pixels, SourceWidth, SourceHeight,
				SliceSettings, Sheet.Slices, prefix );
		} );

		EditorCookie.Set( "SpriteSheetEditor.SliceSettings", SliceSettings );

		Selection.RemoveAll( id => Sheet.GetSlice( id ) is null );
		OnSelectionChanged?.Invoke();
	}

	/// <summary>
	/// Whether re-slicing would throw away work. Used to decide if the destructive method needs
	/// confirming - there is no point nagging about a sheet nobody has touched yet.
	/// </summary>
	public bool HasNamedSlices()
	{
		if ( Sheet?.Slices is null ) return false;

		var prefix = _asset?.Name ?? "slice";
		return Sheet.Slices.Any( s => !s.Name.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) );
	}

	//
	// Asset plumbing
	//

	void RebuildUI()
	{
		MenuBar.Clear();

		{
			var file = MenuBar.AddMenu( "File" );
			file.AddOption( "New", "common/new.png", () => New(), "editor.new" ).StatusTip = "New Sprite Sheet";
			file.AddOption( "Open", "common/open.png", () => Open(), "editor.open" ).StatusTip = "Open Sprite Sheet";
			file.AddOption( "Save", "common/save.png", () => Save(), "editor.save" ).StatusTip = "Save Sprite Sheet";
			file.AddOption( "Save As...", "common/save.png", () => Save( true ), "editor.save-as" );
			file.AddSeparator();
			file.AddOption( new Option( "Exit" ) { Triggered = Close } );
		}

		{
			var edit = MenuBar.AddMenu( "Edit" );
			_undoMenuOption = edit.AddOption( "Undo", "undo", () => Undo(), "editor.undo" );
			_redoMenuOption = edit.AddOption( "Redo", "redo", () => Redo(), "editor.redo" );
			edit.AddSeparator();
			edit.AddOption( "Select All", "select_all", SelectAll );
			edit.AddOption( "Duplicate", "content_copy", DuplicateSelected );
			edit.AddOption( "Delete", "delete", DeleteSelected );
		}

		{
			var view = MenuBar.AddMenu( "View" );
			view.AddOption( "Fit to Window", "fit_screen", () => _canvas?.FitToWindow() );
			view.AddOption( "Actual Size", "crop_original", () => _canvas?.ZoomToActualSize() );
			view.AddSeparator();
			view.AboutToShow += () => { };
		}

		CreateToolBar();
		UpdateUndoRedoOptions();
	}

	void CreateToolBar()
	{
		_toolBar?.Destroy();
		_toolBar = new ToolBar( this, "SpriteSheetEditor.Toolbar" );
		AddToolBar( _toolBar, ToolbarPosition.Top );

		_toolBar.AddOption( "New", "common/new.png", () => New() ).StatusTip = "New Sprite Sheet";
		_toolBar.AddOption( "Open", "common/open.png", () => Open() ).StatusTip = "Open Sprite Sheet";
		_toolBar.AddOption( "Save", "common/save.png", () => Save() ).StatusTip = "Save Sprite Sheet";

		_toolBar.AddSeparator();

		_undoOption = _toolBar.AddOption( "Undo", "undo", () => Undo() );
		_redoOption = _toolBar.AddOption( "Redo", "redo", () => Redo() );

		_toolBar.AddSeparator();

		_toolBar.AddOption( "Image", "image", PickSourceImage ).StatusTip = "Choose the image this sheet cuts up";

		var slice = _toolBar.AddOption( "Slice", "grid_view", OpenSlicePopup );
		slice.StatusTip = "Cut the sheet into slices";

		var trim = _toolBar.AddOption( "Trim", "crop_free", TrimSelected );
		trim.StatusTip = "Shrink the selected slices to their artwork (T)";

		_toolBar.AddSeparator();

		var rig = _toolBar.AddOption( "Build Rig", "accessibility_new", OpenRigBuilder );
		rig.StatusTip = "Turn the slices into sprites and a parented character in the open scene";

		_toolBar.AddSeparator();

		_toolBar.AddOption( "Fit", "fit_screen", () => _canvas?.FitToWindow() ).StatusTip = "Fit the sheet to the window (F)";
	}

	void OpenRigBuilder()
	{
		if ( Sheet is null || Sheet.Slices.Count == 0 )
		{
			Log.Warning( "Sprite Sheet Editor: slice the sheet before building a rig." );
			return;
		}

		new RigBuilder( this ).Show();
	}

	void OpenSlicePopup()
	{
		if ( SourcePixels is null )
		{
			Log.Warning( "Sprite Sheet Editor: pick an image before slicing." );
			return;
		}

		var popup = new SlicePopup( this );
		popup.Position = Editor.Application.CursorPosition;
		popup.Visible = true;
	}

	void New()
	{
		var savePath = GetSavePath( "New Sprite Sheet" );
		if ( string.IsNullOrWhiteSpace( savePath ) ) return;

		_asset = AssetSystem.CreateResource( "sheet", savePath );
		Sheet = _asset.LoadResource<SpriteSheet>();
		_isModified = false;

		Selection.Clear();
		UndoStack.Initialize();
		InvalidateImage();

		OnSheetLoaded?.Invoke();
		OnSelectionChanged?.Invoke();
		UpdateWindowTitle();
	}

	[Shortcut( "editor.open", "CTRL+O", ShortcutType.Window )]
	void Open()
	{
		var fd = new FileDialog( null )
		{
			Title = "Open Sprite Sheet",
			DefaultSuffix = ".sheet"
		};

		fd.SetNameFilter( "Sprite Sheet (*.sheet)" );
		fd.SetFindFile();
		if ( !fd.Execute() ) return;

		Open( fd.SelectedFile );
	}

	void Open( string path, Asset asset = null )
	{
		if ( !string.IsNullOrEmpty( path ) ) asset ??= AssetSystem.FindByPath( path );
		if ( asset is null ) return;

		if ( asset == _asset )
		{
			Focus();
			return;
		}

		var sheet = asset.LoadResource<SpriteSheet>();
		if ( sheet is null )
		{
			Log.Warning( $"Failed to load sprite sheet from asset {asset.Name}" );
			return;
		}

		Sheet = sheet;
		_asset = asset;
		_isModified = false;

		Selection.Clear();
		UndoStack.Initialize();
		InvalidateImage();

		OnSheetLoaded?.Invoke();
		OnSelectionChanged?.Invoke();
		UpdateWindowTitle();

		_canvas?.FitToWindow();
	}

	[Shortcut( "editor.save", "CTRL+S", ShortcutType.Window )]
	bool Save( bool saveAs = false )
	{
		if ( saveAs || _asset is null )
		{
			var savePath = GetSavePath();
			if ( string.IsNullOrWhiteSpace( savePath ) ) return false;

			_asset = AssetSystem.CreateResource( "sheet", savePath );
		}

		_asset.SaveToDisk( Sheet );
		_isModified = false;
		UpdateWindowTitle();
		return true;
	}

	static string GetSavePath( string title = "Save Sprite Sheet" )
	{
		var fd = new FileDialog( null )
		{
			Title = title,
			DefaultSuffix = ".sheet"
		};

		fd.SelectFile( "untitled.sheet" );
		fd.SetFindFile();
		fd.SetModeSave();
		fd.SetNameFilter( "Sprite Sheet (*.sheet)" );
		if ( !fd.Execute() ) return null;

		return fd.SelectedFile;
	}

	[Shortcut( "editor.undo", "CTRL+Z", ShortcutType.Window )]
	void Undo() => UndoStack.Undo();

	[Shortcut( "editor.redo", "CTRL+Y", ShortcutType.Window )]
	void Redo() => UndoStack.Redo();

	void AfterUndoRedo()
	{
		// Undo swaps the slices for fresh objects, so anything selected that no longer exists has to
		// go rather than linger as a dangling id.
		Selection.RemoveAll( id => Sheet?.GetSlice( id ) is null );

		UpdateUndoRedoOptions();
		OnSheetModified?.Invoke();
		OnSelectionChanged?.Invoke();
	}

	/// <summary>
	/// Run something that changes the sheet, recording it as one undo step.
	/// </summary>
	public void ExecuteUndoableAction( string title, Action action )
	{
		var before = Sheet.Serialize();
		action.Invoke();
		var after = Sheet.Serialize();

		UndoStack.Insert( title,
			() => { Sheet.Deserialize( before ); OnSheetModified?.Invoke(); },
			() => { Sheet.Deserialize( after ); OnSheetModified?.Invoke(); } );

		SetModified();
		OnSheetModified?.Invoke();
	}

	/// <summary>
	/// Close off a drag as a single undo step, given the sheet's state from before it started.
	/// </summary>
	/// <remarks>
	/// Dragging cannot go through <see cref="ExecuteUndoableAction"/>: that fires OnSheetModified,
	/// which rebuilds the canvas, which would destroy the very item being dragged. So a drag mutates
	/// the slice directly and settles up here, on release.
	/// </remarks>
	/// <param name="title">What to call it in the undo history</param>
	/// <param name="before">The sheet as it was when the drag began</param>
	public void CommitDrag( string title, JsonObject before )
	{
		var after = Sheet.Serialize();

		UndoStack.Insert( title,
			() => { Sheet.Deserialize( before ); OnSheetModified?.Invoke(); },
			() => { Sheet.Deserialize( after ); OnSheetModified?.Invoke(); } );

		SetModified();
		OnSheetModified?.Invoke();
	}

	public void SetModified()
	{
		_isModified = true;
		UpdateUndoRedoOptions();
		UpdateWindowTitle();
	}

	void UpdateUndoRedoOptions()
	{
		if ( _undoMenuOption is null ) return;

		_undoMenuOption.Enabled = UndoStack.Back.Count > 0;
		_redoMenuOption.Enabled = UndoStack.Forward.Count > 0;
		_undoMenuOption.Text = $"Undo {(UndoStack.Back.Count > 0 ? $"\"{UndoStack.Back.Peek().Name}\"" : "")}";
		_redoMenuOption.Text = $"Redo {(UndoStack.Forward.Count > 0 ? $"\"{UndoStack.Forward.Peek().Name}\"" : "")}";

		if ( _undoOption is null ) return;

		_undoOption.Enabled = UndoStack.Back.Count > 0;
		_redoOption.Enabled = UndoStack.Forward.Count > 0;
		_undoOption.ToolTip = _undoMenuOption.Text;
		_redoOption.ToolTip = _redoMenuOption.Text;
	}

	void UpdateWindowTitle()
	{
		Title = $"Sprite Sheet Editor - {(_asset?.Name ?? "untitled")}{(_isModified ? "*" : "")}";
	}

	protected override bool OnClose()
	{
		if ( !_isModified ) return true;

		var confirm = new PopupWindow(
			"Save Sprite Sheet", "This sprite sheet has unsaved changes. Save now?", "Cancel",
			new Dictionary<string, Action>()
			{
				{ "No", () => { _isModified = false; Close(); } },
				{ "Yes", () => { if ( Save() ) Close(); } }
			} );

		confirm.Show();
		return false;
	}
}
