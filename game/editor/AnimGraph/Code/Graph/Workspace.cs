using Editor.AnimGraph.Nodes;

namespace Editor.AnimGraph;

internal readonly record struct SecondaryGraphInfo(
	Data Graph,
	string Title,
	string Kind,
	string Icon,
	Color Tint,
	string Key );

internal sealed class Workspace : Widget
{
	private sealed class Handle : Widget
	{
		private readonly Workspace _workspace;
		private bool _dragging;
		private bool _startedCollapsed;
		private float _startY;
		private float _startHeight;

		public Handle( Workspace workspace ) : base( workspace )
		{
			_workspace = workspace;
			Cursor = CursorShape.SizeV;
		}

		protected override void OnMousePress( MouseEvent e )
		{
			if ( !e.LeftMouseButton )
				return;

			_dragging = true;
			_startY = e.ScreenPosition.y;
			_startHeight = _workspace.SecondaryHeight;
			_startedCollapsed = _workspace.IsCollapsed;
			_workspace.BeginResize();
			e.Accepted = true;
		}

		protected override void OnMouseMove( MouseEvent e )
		{
			if ( !_dragging )
				return;

			var height = _startHeight - (e.ScreenPosition.y - _startY);
			if ( _startedCollapsed && height > SecondaryHeaderHeight )
				height = Math.Max( height, MinimumExpandedHeight );
			else if ( !_startedCollapsed && height < MinimumExpandedHeight )
				height = SecondaryHeaderHeight;

			_workspace.SecondaryHeight = height;
			e.Accepted = true;
		}

		protected override void OnMouseReleased( MouseEvent e )
		{
			_dragging = false;
			_workspace.EndResize();
			e.Accepted = true;
		}

		protected override void OnPaint()
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.WidgetBackground );
			Paint.DrawRect( LocalRect );
		}
	}

	private const float HandleHeight = 4;
	private const float MinimumPrimaryHeight = 100;
	private const float MinimumExpandedHeight = 96;
	private static float SecondaryHeaderHeight => Theme.RowHeight + 2;

	private readonly Window _window;
	private readonly Handle _handle;
	private readonly Widget _secondaryPanel;
	private readonly View _secondaryView;
	private readonly Widget _secondaryHeader;
	private readonly BreadcrumbButton _secondaryOwner;
	private readonly Label _secondaryChevron;
	private readonly BreadcrumbButton _secondaryGraphType;
	private readonly IconButton _secondaryFit;
	private readonly IconButton _secondaryOpen;
	private readonly IconButton _secondaryToggle;
	private readonly Widget _empty;
	private readonly Dictionary<string, (Vector2 Center, float Scale)> _viewMemory = new();

	private Widget _primary;
	private SecondaryGraphInfo? _secondary;
	private string _shownGraph;
	private string _pendingFrameGraph;
	private bool _rebuilding;
	private bool _collapsed;
	private bool _preserveCenter;
	private bool _finishResize;
	private Vector2 _secondaryCenter;
	private float _lastExpandedHeight;
	private float _requestedSecondaryHeight;
	private float _actualSecondaryHeight;
	private bool _secondaryViewEnabled = true;

	internal View SecondaryView => _secondaryView;
	internal bool IsCollapsed => _actualSecondaryHeight <= SecondaryHeaderHeight + 1;
	internal bool SecondaryViewEnabled
	{
		get => _secondaryViewEnabled;
		set
		{
			if ( _secondaryViewEnabled == value )
				return;

			_secondaryViewEnabled = value;
			_handle.Visible = value;
			_secondaryPanel.Visible = value;
			LayoutChildren();
		}
	}

	private float SecondaryHeight
	{
		get => _actualSecondaryHeight;
		set
		{
			_requestedSecondaryHeight = Math.Max( SecondaryHeaderHeight, value );
			LayoutChildren();
		}
	}

	internal Workspace( Widget parent, Window window ) : base( parent )
	{
		_window = window;
		_handle = new Handle( this );

		_secondaryPanel = new Widget( this ) { MinimumHeight = SecondaryHeaderHeight };
		_secondaryPanel.Layout = Layout.Column();

		_secondaryHeader = new Widget( _secondaryPanel ) { FixedHeight = SecondaryHeaderHeight };
		_secondaryHeader.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.SurfaceBackground );
			Paint.DrawRect( _secondaryHeader.LocalRect );
			return true;
		};

		var header = _secondaryHeader.Layout = Layout.Row();
		header.Margin = new Sandbox.UI.Margin( 4, 0 );
		header.Spacing = 4;

		_secondaryOwner = header.Add( new BreadcrumbButton( "", "", null, null, false ) );
		_secondaryChevron = header.Add( new Label( "›", _secondaryHeader ) );
		_secondaryChevron.Color = Theme.TextControl.WithAlpha( 0.5f );
		_secondaryGraphType = header.Add( new BreadcrumbButton( "", "", null, null, true ) );
		header.AddStretchCell();
		_secondaryFit = header.Add( new IconButton( "fit_screen", () => _secondaryView.FitView(), _secondaryHeader )
		{
			Background = Color.Transparent,
			Foreground = Theme.TextControl,
			IconSize = 14,
			ToolTip = "Fit View",
			Visible = false
		} );
		_secondaryOpen = header.Add( new IconButton( "open_in_full", OpenSecondaryGraph, _secondaryHeader )
		{
			Background = Color.Transparent,
			Foreground = Theme.TextControl,
			IconSize = 14,
			ToolTip = "Open Full Graph",
			Visible = false
		} );
		_secondaryToggle = header.Add( new IconButton( "expand_more", ToggleSecondaryView, _secondaryHeader )
		{
			Background = Color.Transparent,
			Foreground = Theme.TextControl,
			IconSize = 14,
			ToolTip = "Collapse Secondary View"
		} );

		_secondaryView = new View( window, _secondaryPanel ) { Visible = false };
		_secondaryView.OnSelectionChanged += RestorePrimarySelection;

		_empty = new Widget( _secondaryPanel );
		_empty.OnPaintOverride = () =>
		{
			var width = _empty.Width;
			var centerY = _empty.Height * 0.5f;

			Paint.SetPen( Theme.TextControl.WithAlpha( 0.12f ) );
			Paint.DrawIcon( new Rect( 0, centerY - 62, width, 48 ), "ads_click", 44, TextFlag.Center );

			Paint.SetPen( Theme.TextControl.WithAlpha( 0.55f ) );
			Paint.SetFont( Theme.DefaultFont, 11, 600 );
			Paint.DrawText( new Rect( 0, centerY - 8, width, 20 ), "Nothing Selected", TextFlag.Center );

			Paint.SetPen( Theme.TextControl.WithAlpha( 0.3f ) );
			Paint.SetFont( Theme.DefaultFont, 9, 400 );
			Paint.DrawText( new Rect( 0, centerY + 14, width, 18 ), "Select something with a secondary graph to edit it here", TextFlag.Center );

			return true;
		};

		_secondaryPanel.Layout.Add( _secondaryHeader );
		_secondaryPanel.Layout.Add( _secondaryView, 1 );
		_secondaryPanel.Layout.Add( _empty, 1 );

		_lastExpandedHeight = ProjectCookie.Get(
			"AnimGraph.SecondaryHeight",
			ProjectCookie.Get( "AnimGraph.StateMachineSecondaryHeight", 160f ) );
		_collapsed = ProjectCookie.Get(
			"AnimGraph.SecondaryCollapsed",
			ProjectCookie.Get( "AnimGraph.StateMachineSecondaryCollapsed", false ) );
		_requestedSecondaryHeight = _collapsed ? SecondaryHeaderHeight : _lastExpandedHeight;

		UpdateCollapsedState();
	}

	internal void SetPrimary( Widget primary )
	{
		_primary = primary;
		LayoutChildren();
	}

	internal void Show( object context, object selected )
	{
		using var _ = SuspendUpdates.For( _secondaryPanel );

		if ( _window.ResolveSecondaryGraph( context, selected ) is not { } secondary )
		{
			RememberView();
			_secondary = null;
			_shownGraph = null;
			_pendingFrameGraph = null;
			_secondaryOwner.SetContent( "Secondary View", "account_tree" );
			_secondaryChevron.Visible = false;
			_secondaryGraphType.Visible = false;
			_secondaryFit.Visible = false;
			_secondaryOpen.Visible = false;
			UpdateVisibility();
			return;
		}

		_secondary = secondary;
		_secondaryOwner.SetContent( secondary.Title, secondary.Icon, secondary.Tint );
		_secondaryChevron.Visible = true;
		_secondaryGraphType.Visible = true;
		_secondaryFit.Visible = !_collapsed;
		_secondaryOpen.Visible = true;
		_secondaryGraphType.SetContent( secondary.Kind, "" );

		if ( _shownGraph != secondary.Key || !ReferenceEquals( _secondaryView.Graph, secondary.Graph ) )
		{
			RememberView();
			_shownGraph = secondary.Key;

			_rebuilding = true;
			try
			{
				using ( _secondaryView.SuppressSelectionForwarding() )
				{
					if ( ReferenceEquals( _secondaryView.Graph, secondary.Graph ) )
						_secondaryView.RebuildFromGraph();
					else
						_secondaryView.Graph = secondary.Graph;
				}
			}
			finally
			{
				_rebuilding = false;
			}

			if ( _viewMemory.TryGetValue( secondary.Key, out var view ) )
			{
				_pendingFrameGraph = null;
				_secondaryView.SetView( view.Center, view.Scale );
			}
			else
			{
				_pendingFrameGraph = secondary.Key;
			}
		}

		UpdateVisibility();
		TryFrameSecondaryView();
	}

	internal void FocusSecondaryNode( Node node )
	{
		if ( _collapsed )
			ToggleSecondaryView();

		_pendingFrameGraph = null;
		_preserveCenter = false;
		_finishResize = false;
		_secondaryView.SelectNode( node );
		_secondaryView.CenterOnSelection();
		_secondaryView.Focus();
	}

	private void OpenSecondaryGraph()
	{
		if ( _secondary is not { } secondary )
			return;

		_window.NavigateInto( secondary.Graph, secondary.Title, secondary.Icon );
	}

	private void ToggleSecondaryView()
	{
		PreserveCenter();

		if ( !_collapsed )
			_lastExpandedHeight = _secondaryPanel.Height;

		SecondaryHeight = _collapsed ? _lastExpandedHeight : SecondaryHeaderHeight;
		FinishResize();
	}

	private void UpdateCollapsedState()
	{
		_secondaryToggle.Icon = _collapsed ? "expand_less" : "expand_more";
		_secondaryToggle.ToolTip = _collapsed ? "Expand Secondary View" : "Collapse Secondary View";
		_secondaryFit.Visible = _secondary is not null && !_collapsed;
		UpdateVisibility();
	}

	private void UpdateVisibility()
	{
		var hasGraph = _shownGraph is not null;
		_secondaryView.Visible = hasGraph && !_collapsed;
		_empty.Visible = !hasGraph && !_collapsed;
	}

	private void RestorePrimarySelection()
	{
		if ( !_rebuilding && !_secondaryView.SelectedItems.OfType<NodeEditor.NodeUI>().Any() )
			_window.RestorePrimarySelection();
	}

	private void RememberView()
	{
		if ( _shownGraph is not null && _secondaryView.Scale.x > 0 )
			_viewMemory[_shownGraph] = (_secondaryView.Center, _secondaryView.Scale.x);
	}

	private void PreserveCenter()
	{
		if ( !_secondaryView.Visible || _secondaryView.Graph is null )
			return;

		_secondaryCenter = _secondaryView.Center;
		_preserveCenter = true;
	}

	private void BeginResize() => PreserveCenter();

	private void EndResize() => FinishResize();

	private void FinishResize()
	{
		_finishResize = _preserveCenter;
		SaveState();
	}

	private void SaveState()
	{
		ProjectCookie.Set( "AnimGraph.SecondaryHeight", _lastExpandedHeight );
		ProjectCookie.Set( "AnimGraph.SecondaryCollapsed", _collapsed );
	}

	protected override void OnResize()
	{
		base.OnResize();
		LayoutChildren();
	}

	public override void OnDestroyed()
	{
		SaveState();
		base.OnDestroyed();
	}

	private void LayoutChildren()
	{
		if ( _primary is null || Width <= 0 || Height <= 0 )
			return;

		if ( !_secondaryViewEnabled )
		{
			_primary.Position = Vector2.Zero;
			_primary.Size = Size;
			return;
		}

		var available = Math.Max( 0, Height - HandleHeight );
		var minimumPrimaryHeight = Math.Max( MinimumPrimaryHeight, _primary.MinimumHeight );
		var maximumSecondaryHeight = Math.Max( SecondaryHeaderHeight, available - minimumPrimaryHeight );
		var desiredHeight = _requestedSecondaryHeight;

		if ( maximumSecondaryHeight < MinimumExpandedHeight
			|| desiredHeight > SecondaryHeaderHeight && desiredHeight < MinimumExpandedHeight )
			desiredHeight = SecondaryHeaderHeight;

		var secondaryHeight = Math.Clamp( desiredHeight, SecondaryHeaderHeight, maximumSecondaryHeight );
		var primaryHeight = Math.Max( 0, available - secondaryHeight );

		_primary.Position = Vector2.Zero;
		_primary.Size = new Vector2( Width, primaryHeight );
		_handle.Position = new Vector2( 0, primaryHeight );
		_handle.Size = new Vector2( Width, HandleHeight );
		_secondaryPanel.Position = new Vector2( 0, primaryHeight + HandleHeight );
		_secondaryPanel.Size = new Vector2( Width, secondaryHeight );

		if ( _actualSecondaryHeight == secondaryHeight )
			return;

		_actualSecondaryHeight = secondaryHeight;
		var collapsed = IsCollapsed;
		if ( _collapsed != collapsed )
		{
			_collapsed = collapsed;
			UpdateCollapsedState();
		}

		if ( !collapsed )
			_lastExpandedHeight = secondaryHeight;
	}

	[EditorEvent.Frame]
	private void TryFrameSecondaryView()
	{
		if ( _preserveCenter )
		{
			_secondaryView.CenterOn( _secondaryCenter );

			if ( _finishResize )
			{
				_preserveCenter = false;
				_finishResize = false;
			}
		}

		if ( _pendingFrameGraph is null
			|| _pendingFrameGraph != _shownGraph
			|| _collapsed
			|| _secondaryView.Width <= 0
			|| _secondaryView.Height <= 0 )
			return;

		var nodes = _secondaryView.Items.OfType<NodeEditor.NodeUI>().ToArray();
		if ( nodes.Length == 0 )
		{
			_pendingFrameGraph = null;
			return;
		}

		var bounds = nodes[0].SceneRect;
		foreach ( var node in nodes.Skip( 1 ) )
			bounds.Add( node.SceneRect );

		var viewport = Vector2.Max( new Vector2( _secondaryView.Width, _secondaryView.Height ) - 32, new Vector2( 1 ) );
		var span = Vector2.Max( bounds.Size, new Vector2( 1 ) );
		var scale = Math.Clamp( MathF.Min( viewport.x / span.x, viewport.y / span.y ), 0.65f, 1 );

		_secondaryView.SetView( bounds.Center, scale );
		_pendingFrameGraph = null;
	}
}
