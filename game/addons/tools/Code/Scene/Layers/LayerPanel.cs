using Sandbox;

namespace Editor;

/// <summary>
/// Dockable panel for managing scene layers.
/// Similar to Photoshop's Layers panel.
/// </summary>
[Dock( "Editor", "Layers", "layers" )]
public partial class LayerPanel : Widget
{
	public static LayerPanel Current { get; private set; }

	private Layout Header;
	private Layout ToolbarLayout;
	private ScrollArea ScrollArea;
	private Widget LayerList;
	private LineEdit SearchBox;

	private LayerManager _manager;
	private Scene _lastScene;
	private bool _isDirty = true;

	public LayerPanel( Widget parent ) : base( parent )
	{
		Current = this;
		Layout = Layout.Column();
		Layout.Spacing = 0;
		Layout.Margin = 0;

		BuildUI();
	}

	private void BuildUI()
	{
		Layout.Clear( true );

		// Header with search and add button
		Header = Layout.AddRow();
		Header.Spacing = 2;
		Header.Margin = 4;

		var addButton = Header.Add( new LayerAddButton() );
		addButton.OnClicked = CreateNewLayer;

		SearchBox = Header.Add( new LineEdit(), 1 );
		SearchBox.PlaceholderText = "Search layers...";
		SearchBox.FixedHeight = Theme.RowHeight;
		SearchBox.TextChanged += _ => _isDirty = true;

		// Toolbar with layer actions
		ToolbarLayout = Layout.AddRow();
		ToolbarLayout.Spacing = 2;
		ToolbarLayout.Margin = new Margin( 4, 0, 4, 4 );

		AddToolbarButton( "visibility", "Show All", () => _manager?.ShowAll() );
		AddToolbarButton( "visibility_off", "Hide All", () => _manager?.HideAll() );
		ToolbarLayout.AddStretchCell( 1 );
		AddToolbarButton( "create_new_folder", "New Group", CreateNewGroup );
		AddToolbarButton( "delete", "Delete Layer", DeleteSelectedLayer );

		// Layer list scroll area
		ScrollArea = new ScrollArea( this );
		ScrollArea.Canvas = new Widget( ScrollArea );
		ScrollArea.Canvas.Layout = Layout.Column();
		ScrollArea.Canvas.Layout.Spacing = 1;
		ScrollArea.Canvas.Layout.Margin = 4;
		LayerList = ScrollArea.Canvas;

		Layout.Add( ScrollArea, 1 );

		// Footer with opacity slider for selected layer
		var footer = Layout.AddRow();
		footer.Spacing = 4;
		footer.Margin = 4;

		var opacityLabel = footer.Add( new Label( "Opacity:" ) );
		opacityLabel.FixedWidth = 50;

		var opacitySlider = footer.Add( new SliderEntry(), 1 );
		opacitySlider.MinValue = 0;
		opacitySlider.MaxValue = 1;
		opacitySlider.Value = 1;
		opacitySlider.Step = 0.01f;
		opacitySlider.OnValueEdited += v =>
		{
			if ( SelectedLayer is not null )
			{
				using ( GetUndoScope( "Change Layer Opacity" ) )
				{
					SelectedLayer.Opacity = v;
				}
			}
		};

		_opacitySlider = opacitySlider;
	}

	private SliderEntry _opacitySlider;

	private void AddToolbarButton( string icon, string tooltip, Action onClick )
	{
		var btn = ToolbarLayout.Add( new IconButton( icon ) );
		btn.ToolTip = tooltip;
		btn.OnClick += onClick;
		btn.FixedSize = new Vector2( Theme.RowHeight );
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		var session = SceneEditorSession.Active;
		var scene = session?.Scene;

		// Check if scene changed
		if ( scene != _lastScene )
		{
			_lastScene = scene;
			_manager = scene is not null ? LayerManager.Get( scene ) : null;
			_isDirty = true;

			// Subscribe to layer events
			if ( _manager is not null )
			{
				_manager.OnLayerCreated -= OnLayerCreated;
				_manager.OnLayerCreated += OnLayerCreated;
				_manager.OnLayerDeleted -= OnLayerDeleted;
				_manager.OnLayerDeleted += OnLayerDeleted;
				_manager.OnLayerVisibilityChanged -= OnLayerChanged;
				_manager.OnLayerVisibilityChanged += OnLayerChanged;
				_manager.OnLayerOpacityChanged -= OnLayerChanged;
				_manager.OnLayerOpacityChanged += OnLayerChanged;
			}
		}

		if ( _isDirty )
		{
			_isDirty = false;
			RebuildLayerList();
		}

		// Update opacity slider
		if ( _opacitySlider is not null && SelectedLayer is not null )
		{
			_opacitySlider.Value = SelectedLayer.Opacity;
		}
	}

	private void OnLayerCreated( Layer layer ) => _isDirty = true;
	private void OnLayerDeleted( Layer layer ) => _isDirty = true;
	private void OnLayerChanged( Layer layer ) => _isDirty = true;

	private void RebuildLayerList()
	{
		LayerList.Layout.Clear( true );

		if ( _manager is null )
		{
			var label = LayerList.Layout.Add( new Label( "No scene loaded" ) );
			label.Color = Theme.ControlText.WithAlpha( 0.5f );
			LayerList.Layout.AddStretchCell( 1 );
			return;
		}

		var searchText = SearchBox?.Text ?? "";
		var layers = _manager.GetRootLayers();

		foreach ( var layer in layers.Reverse() ) // Reverse so higher sort order is at top
		{
			AddLayerItem( layer, 0, searchText );
		}

		LayerList.Layout.AddStretchCell( 1 );
	}

	private void AddLayerItem( Layer layer, int indent, string searchText )
	{
		// Filter by search
		if ( !string.IsNullOrEmpty( searchText ) &&
			 !layer.Name.Contains( searchText, StringComparison.OrdinalIgnoreCase ) )
		{
			// Still check children for groups
			if ( layer.IsGroup )
			{
				foreach ( var child in layer.Children.Reverse() )
				{
					AddLayerItem( child, indent, searchText );
				}
			}
			return;
		}

		var item = new LayerPanelItem( layer, this );
		item.Indent = indent;
		item.OnSelected = () => SelectLayer( layer );
		item.OnVisibilityToggled = () => ToggleLayerVisibility( layer );
		item.OnLockToggled = () => ToggleLayerLock( layer );
		item.OnDeleted = () => DeleteLayer( layer );
		item.OnRenamed = newName => RenameLayer( layer, newName );
		item.OnColorChanged = color => ChangeLayerColor( layer, color );
		item.IsSelected = SelectedLayer == layer;

		LayerList.Layout.Add( item );

		// Add children if this is an expanded group
		if ( layer.IsGroup && layer.IsExpanded )
		{
			foreach ( var child in layer.Children.Reverse() )
			{
				AddLayerItem( child, indent + 1, searchText );
			}
		}
	}

	#region Layer Selection

	public Layer SelectedLayer { get; private set; }

	private void SelectLayer( Layer layer )
	{
		SelectedLayer = layer;
		_isDirty = true;
	}

	#endregion

	#region Layer Operations

	private void CreateNewLayer()
	{
		if ( _manager is null ) return;

		using ( GetUndoScope( "Create Layer" ) )
		{
			var layer = _manager.CreateLayer();
			SelectLayer( layer );
		}
	}

	private void CreateNewGroup()
	{
		if ( _manager is null ) return;

		using ( GetUndoScope( "Create Layer Group" ) )
		{
			var layer = _manager.CreateGroup();
			SelectLayer( layer );
		}
	}

	private void DeleteSelectedLayer()
	{
		if ( SelectedLayer is null || _manager is null ) return;
		DeleteLayer( SelectedLayer );
	}

	private void DeleteLayer( Layer layer )
	{
		if ( layer is null || _manager is null ) return;
		if ( layer == _manager.DefaultLayer ) return;

		using ( GetUndoScope( "Delete Layer" ) )
		{
			_manager.DeleteLayer( layer );
			if ( SelectedLayer == layer )
			{
				SelectedLayer = null;
			}
		}
	}

	private void ToggleLayerVisibility( Layer layer )
	{
		if ( layer is null ) return;

		using ( GetUndoScope( "Toggle Layer Visibility" ) )
		{
			layer.Visible = !layer.Visible;
		}
	}

	private void ToggleLayerLock( Layer layer )
	{
		if ( layer is null ) return;

		using ( GetUndoScope( "Toggle Layer Lock" ) )
		{
			layer.Locked = !layer.Locked;
		}
	}

	private void RenameLayer( Layer layer, string newName )
	{
		if ( layer is null || string.IsNullOrWhiteSpace( newName ) ) return;

		using ( GetUndoScope( "Rename Layer" ) )
		{
			layer.Name = newName;
		}
	}

	private void ChangeLayerColor( Layer layer, Color color )
	{
		if ( layer is null ) return;

		using ( GetUndoScope( "Change Layer Color" ) )
		{
			layer.Color = color;
		}
	}

	private IDisposable GetUndoScope( string name )
	{
		var session = SceneEditorSession.Active;
		if ( session is null ) return null;

		return session.UndoScope( name ).Push();
	}

	#endregion

	#region Context Menu

	public void OpenContextMenu( Layer layer )
	{
		var menu = new ContextMenu( this );

		menu.AddOption( "Duplicate", "content_copy", () => DuplicateLayer( layer ) );
		menu.AddSeparator();

		if ( layer != _manager?.DefaultLayer )
		{
			menu.AddOption( "Delete", "delete", () => DeleteLayer( layer ) );
		}

		menu.AddSeparator();
		menu.AddOption( "Select All in Layer", "select_all", () => SelectAllInLayer( layer ) );

		if ( layer.IsGroup )
		{
			menu.AddSeparator();
			menu.AddOption( layer.IsExpanded ? "Collapse" : "Expand", layer.IsExpanded ? "expand_less" : "expand_more",
				() =>
				{
					layer.IsExpanded = !layer.IsExpanded;
					_isDirty = true;
				} );
		}

		menu.OpenAtCursor( false );
	}

	private void DuplicateLayer( Layer layer )
	{
		if ( layer is null || _manager is null ) return;

		using ( GetUndoScope( "Duplicate Layer" ) )
		{
			var newLayer = _manager.CreateLayer( layer.Name + " Copy" );
			newLayer.Color = layer.Color;
			newLayer.Visible = layer.Visible;
			newLayer.Locked = layer.Locked;
			newLayer.Opacity = layer.Opacity;
			newLayer.BlendMode = layer.BlendMode;
			newLayer.IsGroup = layer.IsGroup;
			newLayer.ParentLayerId = layer.ParentLayerId;

			SelectLayer( newLayer );
		}
	}

	private void SelectAllInLayer( Layer layer )
	{
		if ( layer is null ) return;

		var session = SceneEditorSession.Active;
		if ( session is null ) return;

		var members = layer.GetMembers();
		session.Selection.Clear();
		foreach ( var go in members )
		{
			session.Selection.Add( go );
		}
	}

	#endregion
}

file class LayerAddButton : Widget
{
	public Action OnClicked;

	public LayerAddButton() : base( null )
	{
		Cursor = CursorShape.Finger;
		FixedSize = new Vector2( Theme.RowHeight );
	}

	protected override void OnPaint()
	{
		var color = Theme.ControlBackground;
		if ( Paint.HasMouseOver )
		{
			color = color.Lighten( 0.1f );
		}

		Paint.ClearPen();
		Paint.SetBrush( color );
		Paint.DrawRect( LocalRect, Theme.ControlRadius );

		Paint.SetPen( Theme.Primary );
		Paint.DrawIcon( LocalRect, "add", 14, TextFlag.Center );
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LeftMouseButton )
		{
			OnClicked?.Invoke();
		}
	}
}
