
namespace Editor;

public partial class SceneViewWidget : Widget
{
	public enum ViewportLayoutMode
	{
		[Title( "One" ), Icon( "check_box_outline_blank" )]
		One,
		[Title( "Two (Horizontal)" ), Icon( "view_column" )]
		TwoHorizontal,
		[Title( "Two (Vertical)" ), Icon( "view_agenda" )]
		TwoVertical,
		[Title( "Three (Left)" ), Icon( "space_dashboard" )]
		ThreeLeft,
		[Title( "Three (Right)" ), Icon( "space_dashboard" )]
		ThreeRight,
		[Title( "Three (Top)" ), Icon( "space_dashboard" )]
		ThreeTop,
		[Title( "Three (Bottom)" ), Icon( "space_dashboard" )]
		ThreeBottom,
		[Title( "Four" ), Icon( "grid_view" )]
		Four
	}

	private ViewportLayoutMode _viewportLayout;

	public ViewportLayoutMode ViewportLayout
	{
		get => _viewportLayout;
		set => SetLayout( value );
	}

	public static SceneViewWidget Current { get; private set; }

	public EditorToolManager Tools { get; private set; }

	public SceneEditorSession Session { get; private set; }

	private List<LinkableSplitter> _splitters;
	public Dictionary<int, SceneViewportWidget> _viewports;

	ViewportTools _viewportTools;

	public SceneViewWidget( SceneEditorSession session, Widget parent ) : base( parent )
	{
		Session = session;
		Tools = new EditorToolManager();

		Layout = Layout.Column();

		_splitters = new List<LinkableSplitter>();
		_viewports = new();

		FocusMode = FocusMode.None;

		RestoreState();
	}

	public override void OnDestroyed()
	{
		SaveState();
		base.OnDestroyed();
	}

	static int selectionHash = 0;

	[EditorEvent.Frame]
	public void Frame()
	{
		var session = Session;
		if ( session is null ) return;
		if ( !session.Scene.IsValid() ) return;

		using var scope = session.Scene.Push();

		bool isActive = session == SceneEditorSession.Active;

		// Update inspector with current selection, if changed
		if ( isActive && selectionHash != session.Selection.GetHashCode() )
		{
			// todo - multiselect
			EditorUtility.InspectorObject = session.Selection.ToArray();
			selectionHash = session.Selection.GetHashCode();
		}

		// All this shit below is scene specific
		if ( CurrentView != ViewMode.Game )
		{
			//
			// Ticks the deferred undo buffer, periodically saves camera state
			//
			session.Tick();

			bool shouldUpdate = Visible; // Update if the scene window is visible

			if ( !shouldUpdate )
				return;

			if ( isActive )
			{
				Current = this;
			}

			session.Scene.EditorTick( RealTime.Now, RealTime.Delta );
		}

		if ( lastView != CurrentView )
		{
			_viewportTools.Update();
			lastView = CurrentView;
		}
	}

	public bool TryGetViewport( int id, out SceneViewportWidget viewport )
	{
		return _viewports.TryGetValue( id, out viewport );
	}

	private SceneViewportWidget CreateViewport( int id = -1 )
	{
		SceneViewportWidget widget = new SceneViewportWidget( this, id == -1 ? _viewports.Count : id );
		_viewports.Add( widget.Id, widget );
		return widget;
	}

	public void SetLayout( ViewportLayoutMode Preset )
	{
		bool isRefresh = _viewportLayout == Preset;
		_viewportLayout = Preset;

		RebuildLayout();
		if ( isRefresh )
		{
			RestoreSplitterState();
		}

		SaveState();
	}

	/// <summary>
	/// Side panel widget to hide when in game view.
	/// </summary>
	Widget _sidePanel;

	public void RebuildLayout()
	{
		Layout.Clear( true );

		_viewportTools = Layout.Add( new ViewportTools( this ) );

		var sideLayout = Layout.AddRow( 1 );
		_sidePanel = sideLayout.Add( new ViewportToolBar( this ) );

		var viewportLayout = sideLayout.AddRow( 1 );

		_splitters.Clear();
		_viewports.Clear();

		switch ( ViewportLayout )
		{
			case ViewportLayoutMode.TwoHorizontal:
				{
					var horizontal = new LinkableSplitter( Orientation.Horizontal, this );
					horizontal.AddWidget( CreateViewport() );
					horizontal.AddWidget( CreateViewport() );
					_splitters.Add( horizontal );
					viewportLayout.Add( horizontal );
					break;
				}
			case ViewportLayoutMode.TwoVertical:
				{
					var vertical = new LinkableSplitter( Orientation.Vertical, this );
					vertical.IsVertical = true;
					vertical.AddWidget( CreateViewport() );
					vertical.AddWidget( CreateViewport() );
					_splitters.Add( vertical );
					viewportLayout.Add( vertical );
					break;
				}
			case ViewportLayoutMode.ThreeLeft:
				{
					var vertical = new LinkableSplitter( Orientation.Vertical, this );
					vertical.IsVertical = true;
					vertical.AddWidget( CreateViewport( 1 ) );
					vertical.AddWidget( CreateViewport( 2 ) );
					_splitters.Add( vertical );

					var horizontal = new LinkableSplitter( Orientation.Horizontal, this );
					horizontal.AddWidget( CreateViewport( 0 ) );
					horizontal.AddWidget( vertical );
					_splitters.Add( horizontal );
					viewportLayout.Add( horizontal );
					break;
				}
			case ViewportLayoutMode.ThreeRight:
				{
					var vertical = new LinkableSplitter( Orientation.Vertical, this );
					vertical.IsVertical = true;
					vertical.AddWidget( CreateViewport( 1 ) );
					vertical.AddWidget( CreateViewport( 2 ) );
					_splitters.Add( vertical );

					var horizontal = new LinkableSplitter( Orientation.Horizontal, this );
					horizontal.AddWidget( vertical );
					horizontal.AddWidget( CreateViewport( 0 ) );
					_splitters.Add( horizontal );
					viewportLayout.Add( horizontal );
					break;
				}
			case ViewportLayoutMode.ThreeTop:
				{
					var vertical = new LinkableSplitter( Orientation.Vertical, this );
					vertical.IsVertical = true;
					vertical.AddWidget( CreateViewport() );
					_splitters.Add( vertical );

					var horizontal = new LinkableSplitter( Orientation.Horizontal, this );
					horizontal.AddWidget( CreateViewport() );
					horizontal.AddWidget( CreateViewport() );
					vertical.AddWidget( horizontal );
					_splitters.Add( horizontal );
					viewportLayout.Add( vertical );
					break;
				}
			case ViewportLayoutMode.ThreeBottom:
				{
					var horizontal = new LinkableSplitter( Orientation.Horizontal, this );
					horizontal.AddWidget( CreateViewport( 1 ) );
					horizontal.AddWidget( CreateViewport( 2 ) );
					_splitters.Add( horizontal );

					var vertical = new LinkableSplitter( Orientation.Vertical, this );
					vertical.IsVertical = true;
					vertical.AddWidget( horizontal );
					vertical.AddWidget( CreateViewport( 0 ) );
					_splitters.Add( vertical );
					viewportLayout.Add( vertical );
					break;
				}
			case ViewportLayoutMode.Four:
				{
					var verticalA = new LinkableSplitter( Orientation.Vertical, this );
					verticalA.AddWidget( CreateViewport() );
					verticalA.AddWidget( CreateViewport() );
					_splitters.Add( verticalA );

					var verticalB = new LinkableSplitter( Orientation.Vertical, this );
					verticalB.AddWidget( CreateViewport() );
					verticalB.AddWidget( CreateViewport() );
					_splitters.Add( verticalB );

					var horizontal = new LinkableSplitter( Orientation.Horizontal, this );
					horizontal.AddWidget( verticalA );
					horizontal.AddWidget( verticalB );
					_splitters.Add( horizontal );
					viewportLayout.Add( horizontal );

					verticalA.LinkWith( verticalB );
					verticalB.LinkWith( verticalA );

					break;
				}
			default:
			case ViewportLayoutMode.One:
				Layout.AddStretchCell();
				viewportLayout.Add( CreateViewport() );
				Layout.AddStretchCell();
				break;
		}
	}
}

file class ViewportToolBar : Widget
{
	public ViewportToolBar( Widget parent ) : base( parent )
	{
		Layout = Layout.Row();
		Layout.Spacing = 2;

		var sidebarWidget = Layout.Add( new ScrollArea( this ) );
		sidebarWidget.Name = "ViewportSidebar";
		sidebarWidget.Canvas = new Widget();
		sidebarWidget.HorizontalScrollbarMode = ScrollbarMode.Off;

		var sidebar = sidebarWidget.Canvas.Layout = Layout.Column();
		sidebar.Margin = 2;

		void PostCreate( GameObject go )
		{
			var tv = SceneTreeWidget.Current.TreeView;
			tv.Open( this );
			tv.SelectItem( go, skipEvents: true );
			tv.BeginRename();
		}

		sidebar.Add( new EditorSubToolBarWidget( this, Layout.AddColumn() ), 0 );
		sidebar.AddStretchCell();

		sidebar.Add( new ViewportMainCreateButton( "add", "Create Empty", () =>
		{
			var menu = new Menu();
			GameObjectNode.CreateObjectMenu( menu, null, PostCreate );
			menu.OpenAtCursor();
		} ) );
	}
}

file class EditorSubToolBarWidget : VerticalToolbarGroup
{
	EditorTool _parentTool;
	readonly Layout _toolPanel;

	public EditorSubToolBarWidget( Widget parent, Layout toolPanel ) : base( parent, null, null )
	{
		_toolPanel = toolPanel;
		FixedWidth = 32;
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect, Theme.ControlRadius );
	}

	public override void Build()
	{
		Layout.Clear( true );

		_toolPanel?.Clear( true );

		Visible = false;

		if ( _parentTool is null )
			return;

		// Create and add tool widget if the tool provides one.
		var toolWidget = _parentTool.CreateToolWidget();
		if ( toolWidget.IsValid() )
		{
			_toolPanel.Add( toolWidget );
		}

		if ( _parentTool.Tools is null || !_parentTool.Tools.Any() )
			return;

		Visible = true;

		Title = DisplayInfo.For( _parentTool ).Name.ToUpperInvariant() + " MODE";

		foreach ( var subtool in _parentTool.Tools.Select( x => EditorTypeLibrary.GetType( x.GetType() ) ) )
		{
			if ( subtool is null )
				continue;

			var toolButton = new EditorSubToolButton( subtool, _parentTool );
			Layout.Add( toolButton );
		}
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		SetContentHash( HashCode.Combine( _parentTool?.CurrentTool ) );

		var tool = SceneViewWidget.Current?.Tools?.CurrentTool;
		if ( _parentTool != tool )
		{
			_parentTool = tool;
			Build();
		}
	}
}

file class EditorSubToolButton : Widget
{
	public TypeDescription Type { get; }

	private readonly EditorTool _parent;

	public EditorSubToolButton( TypeDescription type, EditorTool parent = null ) : base( null )
	{
		_parent = parent;

		Type = type;
		FixedSize = 32;
		Cursor = CursorShape.Finger;

		var title = Type.Title;
		if ( Type.GetAttribute<AliasAttribute>() is AliasAttribute alias && !string.IsNullOrEmpty( alias.Value.FirstOrDefault() ) )
		{
			var keys = EditorShortcuts.GetKeys( alias.Value.FirstOrDefault() );
			if ( !string.IsNullOrEmpty( keys ) )
			{
				title += $" ({keys.Trim().ToUpperInvariant()})";
			}
		}
		ToolTip = $"<b>{title}</b><br>{Type.Description}";
	}

	public bool IsSelectedMode => _parent?.CurrentTool is not null && Type == EditorTypeLibrary.GetType( _parent?.CurrentTool.GetType() );

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LeftMouseButton )
		{
			Activate();
		}
	}

	public void Activate()
	{
		if ( _parent is null || _parent.Tools is null )
			return;

		var tool = _parent.Tools.FirstOrDefault( x => x.GetType() == Type.TargetType );
		_parent.CurrentTool = tool == _parent.CurrentTool ? null : tool;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		if ( IsSelectedMode )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Blue );

			Paint.DrawRect( LocalRect.Shrink( 1 ), 4 );

			Paint.SetPen( Theme.TextButton );
		}
		else
		{
			Paint.ClearPen();
			Paint.SetPen( Theme.TextLight );
		}

		Paint.DrawIcon( LocalRect, Type.Icon, HeaderBarStyle.IconSize, TextFlag.Center );
	}
}
