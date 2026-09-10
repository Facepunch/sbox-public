namespace Editor.SpriteEditor;

public class SpritesheetImporter : Dialog
{
	public string ImagePath { get; set; }
	public Action<string, List<Rect>> OnImport { get; set; }
	public SpritesheetImportSettings Settings { get; set; } = new();
	public int Antialiasing { get; set; } = 2;

	public int SelectionCount => _selection.Count;
	private readonly Dictionary<Vector2Int, int> _selection = new();

	private ScrollArea _scrollArea;
	private SpritesheetPreview _preview;
	private Button _btnImport;
	private Button _btnClear;

	public SpritesheetImporter( Widget parent, string imagePath ) : base( parent, false )
	{
		ImagePath = imagePath;

		Window.Title = "Spritesheet Importer";
		Window.WindowTitle = "Spritesheet Importer";
		Window.Size = new Vector2( 800, 540 );
		Window.SetModal( true );
		Window.MinimumSize = 200;
		Window.MaximumSize = 10000;

		// Restore settings from last session
		var saved = EditorCookie.Get<SpritesheetImportSettings>( "SpriteEditor.SpritesheetImporterSettings", null );
		if ( saved is not null )
			Settings = saved;

		SetupCallbacks();
		BuildLayout();
	}

	private void BuildLayout()
	{
		Layout = Layout.Row();
		Layout.Spacing = 4;

		var leftContent = new Widget();
		leftContent.FixedWidth = 300;
		leftContent.Layout = Layout.Column();
		leftContent.Layout.Spacing = 4;

		_scrollArea = new ScrollArea( this );
		_scrollArea.ContentMargins = 8f;
		_scrollArea.Canvas = new Widget();
		_scrollArea.Canvas.Layout = Layout.Column();
		_scrollArea.Canvas.Layout.Margin = 4f;
		_scrollArea.Canvas.VerticalSizeMode = SizeMode.CanGrow;
		_scrollArea.Canvas.MaximumWidth = 300;
		RebuildControlSheet();
		leftContent.Layout.Add( _scrollArea );

		var leftButtons = new Widget();
		leftButtons.Layout = Layout.Column();
		leftButtons.Layout.Spacing = 4;
		leftButtons.Layout.Margin = 4;

		var btnReset = new Button( "Reset All Settings", "refresh", this );
		btnReset.Clicked += () =>
		{
			Settings = new SpritesheetImportSettings();
			SetupCallbacks();
			RebuildControlSheet();
		};
		leftButtons.Layout.Add( btnReset );

		_btnClear = new Button( "Clear Selection", "deselect", this );
		_btnClear.Enabled = false;
		_btnClear.Clicked += () =>
		{
			ClearSelection();
			UpdateImportButton();
		};
		leftButtons.Layout.Add( _btnClear );

		_btnImport = new Button.Primary( "No Frames Selected", "download", this );
		_btnImport.Enabled = false;
		_btnImport.Clicked += ImportSpritesheet;
		leftButtons.Layout.Add( _btnImport );

		leftContent.Layout.Add( leftButtons );

		Layout.Add( leftContent );

		_preview = new SpritesheetPreview( this );
		Layout.Add( _preview );
	}

	private void ImportSpritesheet()
	{
		var texSize = _preview.Rendering.TextureSize;
		var allFrames = Settings.GetFrames( (int)texSize.x, (int)texSize.y );
		var orderedFrames = _selection
			.OrderBy( kv => kv.Value )
			.Select( kv => allFrames[kv.Key.y * Settings.HorizontalFrames + kv.Key.x] )
			.ToList();

		OnImport?.Invoke( ImagePath, orderedFrames );
		EditorCookie.Set( "SpriteEditor.SpritesheetImporterSettings", Settings );
		Close();
	}

	/// <summary>
	/// Will automatically detect whether the image is a horizontal or vertical strip of frames, so the frames are automatically populated in the grid.
	/// </summary>
	internal void TryAutoDetect( int w, int h )
	{
		int maxSide = Math.Max( w, h );
		int minSide = Math.Min( w, h );

		// Only trigger for strips (2:1 ratio or more elongated)
		if ( maxSide < 2 * minSide ) return;

		int frameCount = maxSide / minSide;
		bool isPerfect = maxSide % minSide == 0;

		if ( w >= h )
		{
			Settings.HorizontalFrames = frameCount;
			Settings.VerticalFrames = 1;
		}
		else
		{
			Settings.HorizontalFrames = 1;
			Settings.VerticalFrames = frameCount;
		}

		if ( isPerfect )
		{
			Settings.PaddingLeft = 0;
			Settings.PaddingRight = 0;
			Settings.PaddingTop = 0;
			Settings.PaddingBottom = 0;
			Settings.HorizontalSeparation = 0;
			Settings.VerticalSeparation = 0;
		}

		RebuildControlSheet();
		SelectAllCells();
	}

	internal bool IsSelected( Vector2Int cell ) => _selection.ContainsKey( cell );
	internal int GetSelectionIndex( Vector2Int cell ) => _selection.GetValueOrDefault( cell, -1 );

	internal void SelectCell( Vector2Int cell )
	{
		_selection[cell] = _selection.Count;
	}

	internal void DeselectCell( Vector2Int cell )
	{
		if ( !_selection.TryGetValue( cell, out int removedIdx ) ) return;
		_selection.Remove( cell );

		// Decrement the index of every entry that came after the removed one
		var toUpdate = _selection.Where( kv => kv.Value > removedIdx ).Select( kv => kv.Key ).ToList();
		foreach ( var key in toUpdate )
			_selection[key]--;
	}

	internal void ClearSelection()
	{
		_selection.Clear();
	}

	private void RebuildControlSheet()
	{
		ClearSelection();
		UpdateImportButton();
		_scrollArea.Canvas.Layout.Clear( true );
		var sheet = new ControlSheet();
		sheet.AddObject( Settings.GetSerialized() );
		_scrollArea.Canvas.Layout.Add( sheet );
		_scrollArea.Canvas.Layout.AddStretchCell();
	}

	internal void SelectAllCells()
	{
		ClearSelection();
		for ( int row = 0; row < Settings.VerticalFrames; row++ )
			for ( int col = 0; col < Settings.HorizontalFrames; col++ )
				SelectCell( new Vector2Int( col, row ) );
		UpdateImportButton();
	}

	internal void UpdateImportButton()
	{
		if ( _btnImport is null ) return;
		_btnImport.Enabled = _selection.Count > 0;
		_btnImport.Text = _selection.Count > 0 ? "Import Spritesheet" : "No Frames Selected";
		if ( _btnClear is not null )
			_btnClear.Enabled = _selection.Count > 0;
	}

	private void SetupCallbacks()
	{
		Settings.OnFrameCountChanged = () =>
		{
			ClearSelection();
			UpdateImportButton();
		};
	}
}
