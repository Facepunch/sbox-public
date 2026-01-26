using Editor.MeshEditor;
using Sandbox;
using Sandbox.UI;

namespace Editor.RectEditor;

public class FastTextureWindow : Window
{
	public MeshFace[] MeshFaces { get; private set; }
	private List<Vector2[]> OriginalUVs { get; set; } = new();
	private List<Material> OriginalMaterials { get; set; } = new();
	public RectView _RectView { get; private set; }

	private IDisposable _undoScope;

	public FastTextureWindow() : base()
	{
		Size = new Vector2( 700, 850 );
		Settings.IsFastTextureTool = true;
	}

	protected override void BuildDock()
	{
		_RectView = new RectView( this );
		_RectView.Layout = Layout.Column();
		_RectView.Layout.Margin = 0;
		_RectView.Layout.Spacing = 0;

		var toolbar = new RectViewToolbar( this );
		_RectView.Layout.Add( toolbar );

		_RectView.Layout.AddStretchCell();

		DockManager.RegisterDockType( "Rect View", "space_dashboard", null, false );
		DockManager.AddDock( null, _RectView, DockArea.Right, DockManager.DockProperty.HideOnClose, 0.0f );

		ToolBar.Visible = false;
		MenuBar.Visible = false;

		RestoreDefaultDockLayout();
	}

	public static void OpenWith( MeshFace[] faces, Material material = null )
	{
		var window = new FastTextureWindow();
		window.MeshFaces = faces;
		window.Parent = EditorWindow;

		window.InitializeWithFaces( faces, material );
		window.WindowFlags = WindowFlags.Dialog | WindowFlags.Customized | WindowFlags.CloseButton | WindowFlags.WindowSystemMenuHint | WindowFlags.WindowTitle | WindowFlags.MaximizeButton;
		window.WindowTitle = "Fast Texturing Tool";
		window.Show();
	}

	private void InitializeWithFaces( MeshFace[] faces, Material material )
	{
		MeshFaces = faces;

		OriginalUVs.Clear();
		OriginalMaterials.Clear();

		List<MeshComponent> components = new();
		foreach ( var face in faces )
		{
			OriginalUVs.Add( face.TextureCoordinates.ToArray() );
			OriginalMaterials.Add( face.Material );
			if ( face.Component.IsValid() && !components.Contains( face.Component ) )
			{
				components.Add( face.Component );
			}
		}

		if ( _undoScope == null )
		{
			_undoScope = SceneEditorSession.Active.UndoScope( "Fast Texture Tool" )
				.WithComponentChanges( components )
				.Push();
		}

		if ( material != null )
		{
			foreach ( var face in faces )
			{
				face.Material = material;
			}
		}

		Settings.FastTextureSettings.Load();

		InitRectanglesFromMeshFaces();

		Settings.ReferenceMaterial = material?.ResourcePath;
		_RectView?.SetMaterial( material );
	}

	protected override void InitRectanglesFromMeshFaces()
	{
		if ( MeshFaces is null || MeshFaces.Length == 0 )
			return;

		var meshRect = Document.AddMeshRectangle( this, MeshFaces, Settings.FastTextureSettings );
		Document.SelectRectangle( meshRect, SelectionOperation.Set );
		OnDocumentModified();
	}

	protected override void UpdateMeshFaces()
	{
		if ( MeshFaces is null || MeshFaces.Length == 0 )
			return;

		var meshRect = Document.Rectangles.OfType<Document.MeshRectangle>().FirstOrDefault();
		if ( meshRect != null && meshRect.FaceVertexIndices.Count == MeshFaces.Length )
		{
			var rectangleRelativePositions = meshRect.GetRectangleRelativePositions();

			for ( int faceIndex = 0; faceIndex < MeshFaces.Length; faceIndex++ )
			{
				var face = MeshFaces[faceIndex];
				var vertexIndices = meshRect.FaceVertexIndices[faceIndex];

				var uvs = new Vector2[vertexIndices.Count];
				for ( int i = 0; i < vertexIndices.Count; i++ )
				{
					var vertexIndex = vertexIndices[i];
					if ( vertexIndex < rectangleRelativePositions.Count )
					{
						uvs[i] = rectangleRelativePositions[vertexIndex];
					}
				}

				face.TextureCoordinates = uvs;
			}
		}
	}

	protected override void UpdateTitle()
	{
		WindowTitle = "Fast Texturing Tool";
	}

	protected override void UpdateProperties()
	{
		if ( !Properties.IsValid() )
			return;

		Properties.SerializedObject = Settings.GetSerialized();
	}

	protected override void OnReferenceChanged( Asset asset )
	{
		var material = asset?.LoadResource<Material>();
		Settings.ReferenceMaterial = material?.ResourcePath;
		_RectView?.SetMaterial( material );

		if ( MeshFaces != null )
		{
			foreach ( var face in MeshFaces )
			{
				face.Material = material;
			}
		}
	}

	public override void OnFastTextureSettingsChanged()
	{
		if ( MeshFaces == null )
			return;

		var meshRect = Document.Rectangles.OfType<Document.MeshRectangle>().FirstOrDefault();
		if ( meshRect != null )
		{
			meshRect.ApplyMapping( Settings.FastTextureSettings, false );
			UpdateMeshFaces();
			Update();
		}
	}

	public override void OnMappingModeChanged()
	{
		if ( MeshFaces == null )
			return;

		var meshRect = Document.Rectangles.OfType<Document.MeshRectangle>().FirstOrDefault();
		if ( meshRect != null )
		{
			bool shouldResetBounds = meshRect.PreviousMappingMode == MappingMode.UseExisting
								  && Settings.FastTextureSettings.Mapping != MappingMode.UseExisting;

			meshRect.ApplyMapping( Settings.FastTextureSettings, shouldResetBounds );
			UpdateMeshFaces();
			Update();
		}
	}

	[EditorEvent.Frame]
	private void OnFrame()
	{
		if ( MeshFaces == null || SceneEditorSession.Active == null )
			return;

		var selectedFaces = SceneEditorSession.Active.Selection.OfType<MeshFace>().ToArray();
		if ( selectedFaces.Length != MeshFaces.Length || !selectedFaces.All( x => MeshFaces.Contains( x ) ) )
		{
			Close();
		}
	}

	[Shortcut( "editor.cancel", "ESC" )]
	public void Cancel()
	{
		if ( MeshFaces != null && OriginalUVs != null && MeshFaces.Length == OriginalUVs.Count )
		{
			for ( int i = 0; i < MeshFaces.Length; i++ )
			{
				MeshFaces[i].TextureCoordinates = OriginalUVs[i];

				if ( i < OriginalMaterials.Count )
				{
					MeshFaces[i].Material = OriginalMaterials[i];
				}
			}
		}

		MeshFaces = null;

		Close();
	}

	[Shortcut( "editor.confirm", "ENTER" )]
	public void Confirm()
	{
		Close();
	}

	[Shortcut( "editor.fasttexture.resetuvs", "Shift+R" )]
	public void ResetUVs()
	{
		_RectView?.ResetUV();
		Update();
	}

	protected override bool OnClose()
	{
		var meshRect = Document?.Rectangles.OfType<Document.MeshRectangle>().FirstOrDefault();
		if ( meshRect != null )
		{
			Settings.FastTextureSettings.SavedRectMin = meshRect.Min;
			Settings.FastTextureSettings.SavedRectMax = meshRect.Max;
		}

		Settings.FastTextureSettings.Save();

		_undoScope?.Dispose();
		_undoScope = null;

		return base.OnClose();
	}
}

public class RectViewToolbar : Widget
{
	private FastTextureSettings Settings => Window?.Settings?.FastTextureSettings;
	private FastTextureWindow Window;
	private ScaleMode _lastScaleMode;
	private TileMode _lastTiledMode;

	public RectViewToolbar( FastTextureWindow window ) : base( null )
	{
		Window = window;
		Layout = Layout.Row();
		Layout.Margin = 2;
		Layout.Spacing = 2;
		FixedHeight = 140;

		TransparentForMouseEvents = false;
		SetModal( true );

		OnPaintOverride = PaintMenuBackground;

		if ( Settings != null )
		{
			_lastScaleMode = Settings.ScaleMode;
			Settings.OnSettingsChanged += OnSettingsChanged;
			Settings.OnMappingChanged += OnSettingsChanged;
		}

		BuildUI();
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();
		if ( Settings != null )
		{
			Settings.OnSettingsChanged -= OnSettingsChanged;
			Settings.OnMappingChanged -= OnSettingsChanged;
		}
	}

	private void OnSettingsChanged()
	{
		if ( Settings.ScaleMode != _lastScaleMode )
		{
			_lastScaleMode = Settings.ScaleMode;
			BuildUI();
		}

		if ( Settings.TileMode != _lastTiledMode )
		{
			_lastTiledMode = Settings.TileMode;
			BuildUI();
		}

		Update();
	}
	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );
		if ( !e.Accepted ) e.Accepted = true;
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );
		if ( !e.Accepted ) e.Accepted = true;
	}
	private void BuildUI()
	{
		Layout.Clear( true );
		if ( Settings == null ) return;

		// --- Mapping & Alignment ---
		{
			var mappingCol = Layout.AddColumn();
			mappingCol.Margin = 2;

			AddGroup( mappingCol, "Mapping", layout =>
			{
				var mappingSeg = new SegmentedControl();
				mappingSeg.AddOption( "Square", "auto_awesome_mosaic" );
				mappingSeg.AddOption( "Conform", "texture" );
				mappingSeg.AddOption( "Planar", "video_camera_back" );
				mappingSeg.AddOption( "Exist", "view_in_ar" );

				mappingSeg.SelectedIndex = Settings.Mapping switch
				{
					MappingMode.UnwrapSquare => 0,
					MappingMode.UnwrapConforming => 1,
					MappingMode.Planar => 2,
					MappingMode.UseExisting => 3,
					_ => 0
				};

				mappingSeg.OnSelectedChanged = ( val ) =>
				{
					Settings.Mapping = val switch
					{
						"Square" => MappingMode.UnwrapSquare,
						"Conform" => MappingMode.UnwrapConforming,
						"Planar" => MappingMode.Planar,
						"Exist" => MappingMode.UseExisting,
						_ => Settings.Mapping
					};
				};
				layout.Add( mappingSeg );
			} );

			AddGroup( mappingCol, "Alignment", layout =>
			{
				var alignCol = layout.AddColumn();
				alignCol.Spacing = 4;

				// Axis Segment
				var alignSeg = new SegmentedControl();
				alignSeg.AddOption( "U Axis", "keyboard_tab" );
				alignSeg.AddOption( "V Axis", "vertical_align_bottom" );
				alignSeg.SelectedIndex = Settings.Alignment == AlignmentMode.UAxis ? 0 : 1;
				alignSeg.OnSelectedChanged = ( val ) =>
				{
					Settings.Alignment = val == "U Axis" ? AlignmentMode.UAxis : AlignmentMode.VAxis;
				};
				alignCol.Add( alignSeg );

				// Toggle Buttons
				var toggleRow = alignCol.AddRow();
				toggleRow.Spacing = 2;
				CreateModeButton( toggleRow, "swap_horiz", "Flip H", () => false, () => Settings.IsFlippedHorizontal = !Settings.IsFlippedHorizontal );
				CreateModeButton( toggleRow, "swap_vert", "Flip V", () => false, () => Settings.IsFlippedVertical = !Settings.IsFlippedVertical );
				CreateModeButton( toggleRow, "border_vertical", "Pick", () => Settings.IsPickingEdge, () => Settings.PickEdge() );
			} );
		}

		// --- Tiling & Scale ---
		{
			var tilingCol = Layout.AddColumn();
			tilingCol.Margin = 2;

			AddGroup( tilingCol, "Tiling / Scale", layout =>
			{
				var scaleSeg = new SegmentedControl();
				scaleSeg.AddOption( "Fit", "aspect_ratio" );
				scaleSeg.AddOption( "World Scale", "public" );
				scaleSeg.AddOption( "Tile U", "view_column" );
				scaleSeg.AddOption( "Tile V", "view_stream" );

				scaleSeg.SelectedIndex = Settings.ScaleMode switch
				{
					ScaleMode.Fit => 0,
					ScaleMode.WorldScale => 1,
					ScaleMode.TileU => 2,
					ScaleMode.TileV => 3,
					_ => 0
				};

				scaleSeg.OnSelectedChanged = ( val ) =>
				{
					Settings.ScaleMode = val switch
					{
						"Fit" => ScaleMode.Fit,
						"World Scale" => ScaleMode.WorldScale,
						"Tile U" => ScaleMode.TileU,
						"Tile V" => ScaleMode.TileV,
						_ => ScaleMode.Fit
					};
				};
				layout.Add( scaleSeg );
			} );

			AddGroup( tilingCol, "Repeat", layout =>
			{
				var col = layout.AddColumn();
				col.Spacing = 4;

				// Repeat Mode Segment
				var repeatSeg = new SegmentedControl();
				repeatSeg.Enabled = Settings.ScaleMode != ScaleMode.Fit || Settings.ScaleMode != ScaleMode.WorldScale;
				repeatSeg.AddOption( "Aspect", "crop_free" );
				repeatSeg.AddOption( "Repeat", "repeat" );

				repeatSeg.SelectedIndex = Settings.TileMode == TileMode.MaintainAspect ? 0 : 1;
				repeatSeg.OnSelectedChanged = ( val ) =>
				{
					Settings.TileMode = val == "Aspect" ? TileMode.MaintainAspect : TileMode.Repeat;
				};

				bool isFit = Settings.ScaleMode == ScaleMode.Fit || Settings.ScaleMode == ScaleMode.WorldScale;
				repeatSeg.Enabled = !isFit;

				col.Add( repeatSeg );

				// Repeat Slider
				var so = Settings.GetSerialized();
				var repeatWidget = new FloatControlWidget( so.GetProperty( nameof( FastTextureSettings.Repeat ) ) );
				repeatWidget.Label = null;
				repeatWidget.FixedHeight = Theme.RowHeight;
				repeatWidget.Icon = "settings_ethernet";
				repeatWidget.HighlightColor = Theme.Blue;
				repeatWidget.MinimumWidth = 160;

				repeatWidget.Enabled = !isFit && Settings.TileMode == TileMode.Repeat;

				col.Add( repeatWidget );
			} );
		}

		// --- Inset ---
		{
			var insetCol = Layout.AddColumn();
			insetCol.Margin = 2;
			AddGroupToLayout( insetCol, "Inset", layout =>
			{
				var col = layout.AddColumn();
				var so = Settings.GetSerialized();

				var xInset = col.Add( new FloatControlWidget( so.GetProperty( nameof( FastTextureSettings.InsetX ) ) ) );
				xInset.FixedHeight = Theme.RowHeight;
				xInset.HighlightColor = Theme.Red;
				xInset.Label = null;
				xInset.Icon = "swap_horiz";
				xInset.MinimumWidth = 160;
				xInset.Enabled = Settings.ScaleMode != ScaleMode.WorldScale;
				col.AddSpacingCell( 8 );

				var yInset = col.Add( new FloatControlWidget( so.GetProperty( nameof( FastTextureSettings.InsetY ) ) ) );
				yInset.FixedHeight = Theme.RowHeight;
				yInset.HighlightColor = Theme.Green;
				yInset.Label = null;
				yInset.Icon = "import_export";
				yInset.MinimumWidth = 160;
				yInset.Enabled = Settings.ScaleMode != ScaleMode.WorldScale;
			} );

			AddGroup( insetCol, "View Mode", layout =>
			{
				var debugview = new ComboBox();
				debugview.AddItem( "Default", "texture" );
				debugview.AddItem( "Roughness", "grain" );
				debugview.AddItem( "Normals", "waves" );
				debugview.CurrentIndex = ((int)Window.Settings.FastTextureSettings.DebugMode);
				debugview.ItemChanged += () =>
				{
					Settings.DebugMode = debugview.CurrentIndex switch
					{
						0 => DebugMode.Default,
						1 => DebugMode.Roughness,
						2 => DebugMode.Normals,
						_ => DebugMode.Default
					};
					Window._RectView?.SetMaterial( Material.Load( Window.Settings.ReferenceMaterial ) );
				};

				layout.Add( debugview );
			} );
		}

		// --- View / Actions ---
		{
			var viewCol = Layout.AddColumn();
			viewCol.Margin = 2;

			AddGroup( viewCol, "View Options", layout =>
			{
				CreateModeButton( layout, "grid_4x4", "Toggle Tile View", () => Settings.IsTileView, () => Settings.IsTileView = !Settings.IsTileView );
				CreateModeButton( layout, "rectangle", "Show Atlas Rects", () => Settings.ShowRects, () => Settings.ShowRects = !Settings.ShowRects );
			} );

			AddGroup( viewCol, "Actions", layout =>
			{
				CreateModeButton( layout, "undo", "Reset UVs (Shift+R)", () => false, () => Window.ResetUVs() );
				CreateModeButton( layout, "fit_screen", "Focus (F)", () => false, () => Window._RectView?.FocusOnUV() );
			} );
		}

		Layout.AddStretchCell();
	}
	bool PaintMenuBackground()
	{
		Paint.SetBrushAndPen( Theme.TabBackground );
		Paint.DrawRect( Paint.LocalRect, 0 );
		return true;
	}

	private void AddGroup( Layout parent, string title, Action<Layout> build )
	{
		AddGroupToLayout( parent, title, build );
	}

	private void AddGroupToLayout( Layout parentLayout, string title, Action<Layout> build )
	{
		var groupContainer = new Widget( this );
		groupContainer.Layout = Layout.Column();

		groupContainer.MaximumHeight = 164;
		groupContainer.MaximumWidth = 268;

		var group = new Widget( groupContainer );
		group.Layout = Layout.Row();
		group.Layout.Spacing = 4;
		group.Layout.Margin = new Margin( 12, 16, 12, 12 );
		group.OnPaintOverride += () =>
		{
			var controlRect = Paint.LocalRect;
			controlRect.Top += 6;
			controlRect = controlRect.Shrink( 0, 0, 1, 1 );

			Paint.Antialiasing = true;
			Paint.TextAntialiasing = true;
			Paint.SetBrushAndPen( Theme.Text.WithAlpha( 0.01f ), Theme.Text.WithAlpha( 0.1f ) );
			Paint.DrawRect( controlRect, 4 );

			Paint.SetPen( Theme.TextControl.WithAlpha( 0.6f ) );
			Paint.SetDefaultFont( 7, 500 );
			Paint.DrawText( new Vector2( 12, 0 ), title );
			return true;
		};

		build( group.Layout );
		groupContainer.Layout.Add( group );

		parentLayout.Add( groupContainer );
	}

	private Widget CreateModeButton( Layout layout, string Icon, string text, Func<bool> isActive, Action onClick )
	{
		var btn = new ToolbarIcon( Icon, text, isActive, onClick );
		layout.Add( btn );
		return btn; // Return the button
	}
}

public class ToolbarIcon : Widget
{
	private string _icon;
	private Func<bool> _isActive;
	private Action _onClick;

	// Track if we are holding the mouse down
	private bool _isPressed;

	public ToolbarIcon( string icon, string tooltip, Func<bool> isActive, Action onClick ) : base( null )
	{
		_icon = icon;
		_isActive = isActive;
		_onClick = onClick;
		ToolTip = tooltip;

		FixedSize = 24;
		Cursor = CursorShape.Finger;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		if ( e.LeftMouseButton )
		{
			_isPressed = true;
			_onClick?.Invoke();
			Update();
		}
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );

		if ( _isPressed )
		{
			_isPressed = false;
			Update();
		}
	}

	protected override void OnMouseLeave()
	{
		base.OnMouseLeave();

		if ( _isPressed )
		{
			_isPressed = false;
			Update();
		}
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		// 1. Is it toggled on? (Like the "Tile View" button)
		bool isToggled = _isActive != null && _isActive();

		// 2. Is the user holding it down right now?
		bool isClicking = _isPressed && IsUnderMouse;

		// 3. Show Blue if EITHER is true
		bool showBlue = (isToggled || isClicking) && Enabled;

		if ( showBlue )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Blue );
			Paint.DrawRect( LocalRect.Shrink( 2 ), 4 );
			Paint.SetPen( Theme.TextButton );
		}
		else if ( !Enabled )
		{
			Paint.ClearPen();
			Paint.SetBrush( Color.White.WithAlpha( 0.02f ) );
			Paint.DrawRect( LocalRect.Shrink( 2 ), 4 );
			Paint.SetPen( Theme.TextLight.WithAlpha( 0.4f ) );
			Cursor = CursorShape.Arrow;
		}
		else if ( IsUnderMouse )
		{
			Paint.ClearPen();
			Paint.SetBrush( Color.White.WithAlpha( 0.05f ) );
			Paint.DrawRect( LocalRect.Shrink( 2 ), 4 );
			Paint.SetPen( Theme.TextLight );
		}
		else
		{
			Paint.ClearPen();
			Paint.SetPen( Theme.TextLight.WithAlpha( 0.8f ) );
		}

		Paint.DrawIcon( LocalRect, _icon, 16, TextFlag.Center );
	}
}
