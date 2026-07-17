using Sandbox.Helpers;
using System.Text.Json.Nodes;

namespace Editor.SpriteEditor;

[EditorForAssetType( "sprite" )]
[EditorApp( "Sprite Editor", "emoji_emotions", "Edit 2D Sprites" )]
public class Window : DockWindow, IAssetEditor
{
	public bool CanOpenMultipleAssets => true;

	public Sprite Sprite { get; private set; }
	public Sprite.Animation SelectedAnimation { get; set; }
	public bool IsPlaying { get; set; } = true;
	public int Antialiasing { get; set; } = 2;
	public int CurrentFrameIndex
	{
		get => _preview?.Renderer?.CurrentFrameIndex ?? 0;
		set
		{
			if ( _preview?.Renderer is not null )
			{
				_preview.Renderer.CurrentFrameIndex = value;
			}
		}
	}

	public Action OnAssetLoaded { get; set; }
	public Action OnSpriteModified { get; set; }
	public Action OnAnimationSelected { get; set; }
	public Action OnFramesChanged { get; set; }
	public Action OnPlayPause { get; set; }
	public UndoSystem UndoStack;

	private Asset Asset;
	private bool IsModified;

	private string _lastAnimationName;
	private JsonObject _lastUnsavedState;
	ToolBar _toolBar;
	Preview _preview;
	Option _undoMenuOption;
	Option _redoMenuOption;
	Option _undoOption;
	Option _redoOption;

	public Window()
	{
		DeleteOnClose = true;

		Antialiasing = EditorCookie.Get( "SpriteEditor.Antialiasing", 2 );

		Size = new Vector2( 1280, 720 );
		Sprite = new Sprite();

		UndoStack = new UndoSystem();
		UndoStack.Initialize();
		UndoStack.OnUndo += _ => UpdateUndoRedoOptions();
		UndoStack.OnRedo += _ => UpdateUndoRedoOptions();

		OnSpriteModified += UpdateCurrentAnimation;
		OnAnimationSelected += () => _lastAnimationName = SelectedAnimation?.Name;

		SetWindowIcon( "emoji_emotions" );

		CreateDocks();
		RebuildUI();
		Show();
		StateCookie = "SpriteEditor";
	}

	public void AssetOpen( Asset asset )
	{
		Raise();
		Open( null, asset );
	}

	public void SelectMember( string memberName )
	{
		throw new NotImplementedException();
	}

	private void CreateDocks()
	{
		_preview = new Preview( this );

		DockManager.AddDock( "Preview", "emoji_emotions", _preview, DockArea.Center );
		DockManager.AddDock( "Inspector", "edit", new Inspector( this ), DockArea.Left );
		DockManager.AddDock( "Animations", "directions_walk", new AnimationList( this ), DockArea.Bottom );
		DockManager.AddDock( "Timeline", "view_column", new Timeline( this ), DockArea.Bottom );
	}

	protected override void BuildDefaultLayout()
	{
		var preview = DockManager.OpenDock( "Preview", DockArea.Center );
		var inspector = DockManager.OpenDock( "Inspector", DockArea.Left );
		var animations = DockManager.OpenDock( "Animations", DockArea.Bottom, inspector );
		var timeline = DockManager.OpenDock( "Timeline", DockArea.Bottom );

		DockManager.SetSplitterProportions( animations, 0.68f, 0.32f );
		DockManager.SetSplitterProportions( preview, 0.25f, 0.75f );
		DockManager.SetSplitterProportions( timeline, 0.75f, 0.25f );
	}

	private void RebuildUI()
	{
		MenuBar.Clear();

		{
			var file = MenuBar.AddMenu( "File" );
			file.AddOption( "New", "common/new.png", () => New(), "editor.new" ).StatusTip = "New Sprite";
			file.AddOption( "Open", "common/open.png", () => Open(), "editor.open" ).StatusTip = "Open Sprite";
			file.AddOption( "Save", "common/save.png", () => Save(), "editor.save" ).StatusTip = "Save Sprite";
			file.AddOption( "Save As...", "common/save.png", () => Save( true ), "editor.save-as" ).StatusTip = "Save Sprite As...";
			file.AddSeparator();
			file.AddOption( new Option( "Exit" ) { Triggered = Close } );
		}

		{
			var edit = MenuBar.AddMenu( "Edit" );
			_undoMenuOption = edit.AddOption( "Undo", "undo", () => Undo(), "editor.undo" );
			_redoMenuOption = edit.AddOption( "Redo", "redo", () => Redo(), "editor.redo" );

			//edit.AddSeparator();
			//edit.AddOption( "Cut", "common/cut.png", CutSelection, "Ctrl+X" );
			//edit.AddOption( "Copy", "common/copy.png", CopySelection, "Ctrl+C" );
			//edit.AddOption( "Paste", "common/paste.png", PasteSelection, "Ctrl+V" );
			//edit.AddOption( "Select All", "select_all", SelectAll, "Ctrl+A" );
		}

		{
			var view = MenuBar.AddMenu( "View" );

			view.AboutToShow += () => OnViewMenu( view );
		}

		CreateToolBar();
		UpdateUndoRedoOptions();
	}

	private void CreateToolBar()
	{
		_toolBar?.Destroy();
		_toolBar = new ToolBar( this, "SpriteEditor.Toolbar" );
		AddToolBar( _toolBar, ToolbarPosition.Top );

		_toolBar.AddOption( "New", "common/new.png", New ).StatusTip = "New Sprite";
		_toolBar.AddOption( "Open", "common/open.png", Open ).StatusTip = "Open Sprite";
		_toolBar.AddOption( "Save", "common/save.png", () => Save() ).StatusTip = "Save Sprite";

		_toolBar.AddSeparator();

		_undoOption = _toolBar.AddOption( "Undo", "undo", () => Undo() );
		_redoOption = _toolBar.AddOption( "Redo", "redo", () => Redo() );

		_toolBar.AddSeparator();
	}

	private void OnViewMenu( Menu view )
	{
		view.Clear();

		var menuAA = view.AddMenu( "MSAA" );
		var optionAA0 = menuAA.AddOption( "Off" );
		menuAA.AddSeparator();
		var optionAA1 = menuAA.AddOption( "Bilinear" );
		var optionAA2 = menuAA.AddOption( "Trilinear" );
		var optionAA3 = menuAA.AddOption( "Anisotropic" );

		void SetAA( int level )
		{
			Antialiasing = level;
			optionAA0.Checked = Antialiasing == 0;
			optionAA1.Checked = Antialiasing == 1;
			optionAA2.Checked = Antialiasing == 2;
			optionAA3.Checked = Antialiasing == 3;
		}

		optionAA0.Checkable = true;
		optionAA0.Triggered += () => SetAA( 0 );
		optionAA1.Checkable = true;
		optionAA1.Triggered += () => SetAA( 1 );
		optionAA2.Checkable = true;
		optionAA2.Triggered += () => SetAA( 2 );
		optionAA3.Checkable = true;
		optionAA3.Triggered += () => SetAA( 3 );

		SetAA( Antialiasing );

		view.AddOption( "Restore To Default", "settings_backup_restore", ResetLayout );
		view.AddSeparator();

		foreach ( var dock in DockManager.DockTypes )
		{
			var o = view.AddOption( dock.Title, dock.Icon );
			o.Checkable = true;
			o.Checked = DockManager.IsDockOpen( dock.Title );
			o.Toggled += ( b ) => DockManager.SetDockState( dock.Title, b );
		}
	}

	private void UpdateCurrentAnimation()
	{
		if ( !string.IsNullOrEmpty( _lastAnimationName ) )
		{
			SelectedAnimation = Sprite.Animations.FirstOrDefault( x => x.Name == _lastAnimationName );
		}
		else
		{
			SelectedAnimation = Sprite.Animations.FirstOrDefault();
		}
		OnAnimationSelected?.Invoke();
	}

	[Shortcut( "editor.new", "CTRL+N", ShortcutType.Window )]
	private void New()
	{
		PromptSave( () => CreateNew() );
	}

	private void CreateNew( string savePath = null )
	{
		if ( string.IsNullOrEmpty( savePath ) )
		{
			savePath = GetSavePath( "New Sprite" );
		}

		Asset = AssetSystem.CreateResource( "sprite", savePath );
		Sprite = Asset.LoadResource<Sprite>();
		SelectedAnimation = Sprite.Animations[0];
		IsModified = false;
		UndoStack.Initialize();

		OnAssetLoaded?.Invoke();
		OnAnimationSelected?.Invoke();
		UpdateWindowTitle();
	}

	[Shortcut( "editor.open", "CTRL+O", ShortcutType.Window )]
	private void Open()
	{
		var fd = new FileDialog( null )
		{
			Title = "Open Sprite",
			DefaultSuffix = ".sprite"
		};

		fd.SetNameFilter( "Sprite (*.sprite)" );

		if ( !fd.Execute() ) return;

		PromptSave( () => Open( fd.SelectedFile ) );
	}

	private void Open( string path, Asset asset = null )
	{
		if ( !string.IsNullOrEmpty( path ) )
		{
			asset ??= AssetSystem.FindByPath( path );
		}
		if ( asset == null ) return;
		if ( asset == Asset )
		{
			Focus();
			return;
		}

		var sprite = asset.LoadResource<Sprite>();
		if ( sprite is null )
		{
			Log.Warning( $"Failed to load sprite resource from asset {asset.Name}" );
			return;
		}

		Sprite = sprite;
		Asset = asset;
		IsModified = false;
		OnAssetLoaded?.Invoke();
		_lastUnsavedState = Sprite.Serialize();

		if ( (Sprite?.Animations?.Count ?? 0) > 0 )
		{
			SelectedAnimation = Sprite.Animations[0];
			OnAnimationSelected?.Invoke();
		}

		UndoStack.Initialize();
		UpdateWindowTitle();
	}

	[Shortcut( "editor.save", "CTRL+S", ShortcutType.Window )]
	private bool Save( bool saveAs = false )
	{
		if ( saveAs || Asset is null )
		{
			var savePath = GetSavePath( "Save Sprite" );

			if ( string.IsNullOrWhiteSpace( savePath ) )
				return false;

			Asset = AssetSystem.CreateResource( "sprite", savePath );
		}

		Asset.SaveToDisk( Sprite );
		IsModified = false;
		UpdateWindowTitle();
		return true;
	}

	[Shortcut( "editor.save-as", "CTRL+SHIFT+S", ShortcutType.Window )]
	private void SaveAs()
	{
		Save( true );
	}

	private void Restore()
	{
		if ( _lastUnsavedState is null )
		{
			IsModified = false;
			return;
		}

		Sprite.Deserialize( _lastUnsavedState );
		Asset.SaveToDisk( Sprite );
		IsModified = false;
	}

	private void PromptSave( Action action )
	{
		if ( !IsModified )
		{
			action?.Invoke();
			return;
		}

		var confirm = new PopupWindow(
			"Save Current Sprite", "The open sprite has unsaved changes. Would you like to save before continuing?", "Cancel",
			new Dictionary<string, Action>
			{
				{ "No", () => {
					action?.Invoke();
				} },
				{ "Yes", () => {
					if (Save()) action?.Invoke();
				}}
			} );
		confirm.Show();
	}

	static string GetSavePath( string title = "Save Sprite" )
	{
		var fd = new FileDialog( null )
		{
			Title = title,
			DefaultSuffix = $".sprite"
		};

		fd.SelectFile( "untitled.sprite" );
		fd.SetFindFile();
		fd.SetModeSave();
		fd.SetNameFilter( "Sprite (*.sprite)" );
		if ( !fd.Execute() ) return null;

		return fd.SelectedFile;
	}

	private void UpdateWindowTitle()
	{
		Title = $"Sprite Editor - {(Asset?.Name ?? "untitled")}{(IsModified ? "*" : "")}";
	}

	private void UpdateUndoRedoOptions()
	{
		_undoMenuOption.Enabled = UndoStack.Back.Count > 0;
		_redoMenuOption.Enabled = UndoStack.Forward.Count > 0;
		_undoMenuOption.Text = $"Undo {(UndoStack.Back.Count > 0 ? $"\"{UndoStack.Back.Peek().Name}\"" : "")}";
		_redoMenuOption.Text = $"Redo {(UndoStack.Forward.Count > 0 ? $"\"{UndoStack.Forward.Peek().Name}\"" : "")}";

		_undoOption.Enabled = UndoStack.Back.Count > 0;
		_redoOption.Enabled = UndoStack.Forward.Count > 0;
		_undoOption.ToolTip = _undoMenuOption.Text;
		_redoOption.ToolTip = _redoMenuOption.Text;
		_undoOption.StatusTip = _undoMenuOption.Text;
		_redoOption.StatusTip = _redoMenuOption.Text;
	}

	protected override bool OnClose()
	{
		EditorCookie.Set( "SpriteEditor.Antialiasing", Antialiasing );

		if ( IsModified )
		{
			var confirm = new PopupWindow(
				"Save Current Sprite", "The open sprite has unsaved changes. Would you like to save now?", "Cancel",
				new Dictionary<string, System.Action>()
				{
					{ "No", () => { Restore(); Close(); } },
					{ "Yes", () => { Save(); Close(); } }
				}
			);

			confirm.Show();

			return false;
		}

		return true;
	}

	[Shortcut( "editor.quit", "CTRL+Q" )]
	void Quit()
	{
		Close();
	}

	[Shortcut( "editor.undo", "CTRL+Z" )]
	private bool Undo()
	{
		return UndoStack.Undo();
	}

	[Shortcut( "editor.redo", "CTRL+Y" )]
	private bool Redo()
	{
		return UndoStack.Redo();
	}

	public void ExecuteUndoableAction( string title, Action action )
	{
		var preState = Sprite.Serialize();
		action.Invoke();
		var postState = Sprite.Serialize();

		UndoStack.Insert( title,
			() =>
			{
				Sprite.Deserialize( preState );
				OnSpriteModified?.Invoke();
			},
			() =>
			{
				Sprite.Deserialize( postState );
				OnSpriteModified?.Invoke();
			} );

		SetModified();
	}

	public void SetModified()
	{
		IsModified = true;
		UpdateUndoRedoOptions();
		UpdateWindowTitle();
	}

	public void TogglePlayPause()
	{
		// When pressing play on the last frame with LoopMode.None, restart from the first frame
		if ( !IsPlaying && SelectedAnimation is not null && SelectedAnimation.LoopMode == Sprite.LoopMode.None )
		{
			if ( CurrentFrameIndex >= SelectedAnimation.Frames.Count - 1 )
			{
				CurrentFrameIndex = 0;
			}
		}

		IsPlaying = !IsPlaying;
		OnPlayPause?.Invoke();
	}

	public void GotoFirstFrame()
	{
		CurrentFrameIndex = 0;
	}

	public void GotoLastFrame()
	{
		if ( SelectedAnimation is null ) return;
		CurrentFrameIndex = SelectedAnimation.Frames.Count - 1;
	}

	public void GotoNextFrame()
	{
		var frame = CurrentFrameIndex + 1;
		if ( frame >= (SelectedAnimation?.Frames.Count ?? 0) )
			frame = 0;
		CurrentFrameIndex = frame;
	}

	public void GotoPreviousFrame()
	{
		var frame = CurrentFrameIndex - 1;
		if ( frame < 0 )
			frame = (SelectedAnimation?.Frames.Count ?? 0) - 1;
		CurrentFrameIndex = frame;
	}

	internal static void ShowNamingError( string name )
	{
		if ( string.IsNullOrEmpty( name ) )
		{
			var confirm = new PopupWindow( "Invalid name ''", "You cannot give an animation an empty name", "OK" );
			confirm.Show();
		}
		else
		{
			var confirm = new PopupWindow( $"Invalid name '{name}'", "You cannot give two animations the same name", "OK" );
			confirm.Show();
		}
	}
}
