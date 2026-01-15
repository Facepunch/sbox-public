using Sandbox.Utility;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace Editor.NodeEditor;

public interface IGridSizeView
{
	float GridSize { get; }
}

public class GraphView : GraphicsView, IGridSizeView
{
	IGraph _graph;
	public IGraph Graph
	{
		get => _graph;
		set
		{
			if ( _graph == value ) return;

			_graph = value;
			RebuildFromGraph();
		}
	}

	protected readonly List<Connection> Connections = new();

	protected virtual INodeType RerouteNodeType { get; }
	protected virtual INodeType CommentNodeType { get; }

	Vector2 lastMouseScenePosition;

	internal Plug DropTarget { get; set; }
	internal Connection Preview { get; set; }

	public virtual ConnectionStyle ConnectionStyle => ConnectionStyle.Default;

	public float GridSize { get; set; } = 12f;
	public virtual bool FadeOutBackground => true;

	protected virtual string ViewCookie { get; }

	Pixmap _backgroundPixmap;
	Pixmap _backgroundPixmapClear;
	NodeUI _nodePreview;
	bool _createReroute;

	public GraphView( Widget parent ) : base( parent )
	{
		Antialiasing = true;
		TextAntialiasing = true;
		BilinearFiltering = true;

		SceneRect = new Rect( -100000, -100000, 200000, 200000 );

		HorizontalScrollbar = ScrollbarMode.Off;
		VerticalScrollbar = ScrollbarMode.Off;
		MouseTracking = true;

		// Init background
		_backgroundPixmapClear = new Pixmap( 1, 1 );
		_backgroundPixmapClear.Clear( Theme.WindowBackground );
		var bgPixmap = CreateBackgroundPixmap();
		if ( bgPixmap != null )
		{
			_backgroundPixmap = bgPixmap;
			SetBackgroundImage( bgPixmap );
		}
	}

	protected virtual Pixmap CreateBackgroundPixmap()
	{
		var pixmap = new Pixmap( (int)GridSize, (int)GridSize );
		pixmap.Clear( Theme.WindowBackground );
		using ( Paint.ToPixmap( pixmap ) )
		{
			var h = pixmap.Size * 0.5f;

			Paint.SetPen( Theme.WindowBackground.Lighten( 0.3f ) );
			Paint.DrawLine( 0, new Vector2( 0, pixmap.Height ) );
			Paint.DrawLine( 0, new Vector2( pixmap.Width, 0 ) );
		}

		return pixmap;
	}

	private Vector2 _lastCenter;
	private Vector2 _lastScale;

	private bool _hasDropped;

	[EditorEvent.Frame]
	private void Frame()
	{
		_hasDropped = false;

		var center = Center;
		var scale = Scale;

		if ( _lastCenter == center && _lastScale == scale )
		{
			return;
		}

		if ( ViewCookie is { } viewCookie )
		{
			if ( _lastCenter != center )
			{
				EditorCookie.Set( $"{viewCookie}.view.center", center );
			}

			if ( _lastScale != scale )
			{
				EditorCookie.Set( $"{viewCookie}.view.scale", scale );
			}
		}

		_lastCenter = center;
		_lastScale = scale;
	}

	public void RestoreViewFromCookie()
	{
		if ( ViewCookie is not { } cookieName )
		{
			return;
		}

		Scale = EditorCookie.Get( $"{cookieName}.view.scale", Scale );
		Center = EditorCookie.Get( $"{cookieName}.view.center", Center );
	}

	internal IDisposable UndoScope( string name )
	{
		PushUndo( name );
		return new Sandbox.Utility.DisposeAction( () => PushRedo() );
	}

	public virtual void PushUndo( string name )
	{
	}

	public virtual void PushRedo()
	{
	}

	private bool _moveablePressed;
	private bool _moveableMoved;

	internal void MoveablePressed()
	{
		_moveablePressed = true;
		_moveableMoved = false;
	}

	internal void MoveableMoved()
	{
		if ( _moveablePressed && !_moveableMoved )
		{
			_moveableMoved = true;

			PushUndo( "Move Item" );
		}
	}

	internal void MoveableReleased()
	{
		if ( _moveablePressed && _moveableMoved )
		{
			PushRedo();
		}

		_moveablePressed = false;
	}

	protected override void OnWheel( WheelEvent e )
	{
		Zoom( e.Delta > 0 ? 1.1f : 0.90f, e.Position );
		if ( FadeOutBackground )
		{
			SetBackgroundImage( Scale.x < 0.5f ? _backgroundPixmapClear : _backgroundPixmap );
		}
		e.Accept();
	}

	public class SelectionBox : GraphicsItem
	{
		private Vector2 _start;
		private Vector2 _end;

		private GraphicsView _view;

		public Vector2 EndScene
		{
			set
			{
				_end = value;

				var start = Vector2.Min( _start, _end );
				var end = Vector2.Max( _start, _end );

				var localStart = _view.FromScene( start );
				var localEnd = _view.FromScene( end );

				_view.SelectionRect = new Rect( localStart, localEnd - localStart );

				Size = end - start;
				Position = start;

				Update();
				PrepareGeometryChange();
			}
		}

		public SelectionBox( Vector2 startScene, GraphicsView view )
		{
			_view = view;
			_start = startScene;
			_end = startScene;

			Position = startScene;
		}

		protected override void OnPaint()
		{
			Paint.ClearPen();
			Paint.ClearBrush();
			Paint.SetPen( Theme.Blue.WithAlpha( 1.0f ), 1.0f, PenStyle.Solid );
			Paint.SetBrush( Theme.Blue.WithAlpha( 0.5f ) );
			Paint.DrawRect( LocalRect, 0 );
		}
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		base.OnKeyPress( e );


		switch ( e.Key )
		{
			case KeyCode.Delete:
				DeleteSelection();
				break;

			case KeyCode.R:
				_createReroute = true;
				break;

			case KeyCode.Space:
				OpenContextMenu( Application.CursorPosition, lastMouseScenePosition );
				break;
		}
	}

	protected override void OnKeyRelease( KeyEvent e )
	{
		base.OnKeyRelease( e );

		if ( e.Key == KeyCode.R )
		{
			_createReroute = false;
		}
	}

	public void DeleteNode( NodeUI node )
	{
		if ( !node.IsValid() )
			return;

		using var undoScope = UndoScope( "Delete Node" );

		RemoveNode( node );
		ClearSelection();
	}

	public void DeleteSelection()
	{
		if ( !SelectedItems.Any() )
			return;

		using var undoScope = UndoScope( "Delete Selection" );

		foreach ( var connection in SelectedItems.OfType<Connection>() )
		{
			RemoveConnection( connection );
			connection.Disconnect();
			connection.Destroy();
		}

		foreach ( var node in SelectedItems.OfType<NodeUI>() )
		{
			RemoveNode( node );
		}
		ClearSelection();
	}

	protected virtual string ClipboardIdent => "graphview";

	public void CopySelection()
	{
		var nodes = SelectedItems.OfType<NodeUI>().ToArray();
		if ( !nodes.Any() )
			return;

		using var ms = new MemoryStream();
		using ( var zs = new GZipStream( ms, CompressionMode.Compress ) )
		{
			var data = Encoding.UTF8.GetBytes( _graph.SerializeNodes( nodes.Select( x => x.Node ) ) );
			zs.Write( data, 0, data.Length );
		}

		var sb = new StringBuilder();
		sb.Append( $"{ClipboardIdent}:" );
		sb.Append( Convert.ToBase64String( ms.ToArray() ) );

		EditorUtility.Clipboard.Copy( sb.ToString() );
	}

	public bool CanPasteSelection()
	{
		var buffer = EditorUtility.Clipboard.Paste();
		if ( string.IsNullOrWhiteSpace( buffer ) )
			return false;
		return buffer.StartsWith( $"{ClipboardIdent}:" );
	}

	public void PasteSelection()
	{
		var buffer = EditorUtility.Clipboard.Paste();
		if ( string.IsNullOrWhiteSpace( buffer ) )
			return;

		var ident = $"{ClipboardIdent}:";
		if ( !buffer.StartsWith( ident ) )
			return;

		buffer = buffer.Substring( ident.Length );

		byte[] decompressedData;

		try
		{
			using var ms = new MemoryStream( Convert.FromBase64String( buffer ) );
			using var zs = new GZipStream( ms, CompressionMode.Decompress );
			using var outStream = new MemoryStream();
			zs.CopyTo( outStream );
			decompressedData = outStream.ToArray();
		}
		catch
		{
			Log.Warning( "Paste is not valid base64" );
			return;
		}

		try
		{
			var decompressed = Encoding.UTF8.GetString( decompressedData );
			var nodes = _graph.DeserializeNodes( decompressed ).ToArray();

			if ( !nodes.Any() )
				return;

			using var undoScope = UndoScope( "Paste Selection" );

			OnPaste( nodes );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Paste is not valid json: {e}" );
		}
	}

	public void DuplicateSelection()
	{
		var selected = SelectedItems.OfType<NodeUI>().Select( x => x.Node ).ToArray();
		if ( !selected.Any() )
			return;

		var pasted = _graph.DeserializeNodes( _graph.SerializeNodes( selected ) ).ToArray();

		using var undoScope = UndoScope( "Duplicate Selection" );

		OnPaste( pasted );
	}

	private void OnPaste( IReadOnlyList<INode> nodes )
	{
		var average = new Vector2( nodes.Average( x => x.Position.x ), nodes.Average( x => x.Position.y ) );

		foreach ( var item in SelectedItems )
		{
			item.Selected = false;
		}

		BuildFromNodes( nodes, true, -average + lastMouseScenePosition, true );
	}

	public void CutSelection()
	{
		if ( !SelectedItems.OfType<NodeUI>().Any() )
			return;

		using var undoScope = UndoScope( "Cut Selection" );

		CopySelection();

		foreach ( var connection in SelectedItems.OfType<Connection>() )
		{
			RemoveConnection( connection );
			connection.Destroy();
		}

		foreach ( var node in SelectedItems.OfType<NodeUI>() )
		{
			RemoveNode( node );
		}
		ClearSelection();
	}

	public void CenterOnSelection()
	{
		var bounds = new Rect();
		var anySelected = false;

		foreach ( var selectedItem in SelectedItems )
		{
			if ( !anySelected )
			{
				bounds = selectedItem.SceneRect;
			}
			else
			{
				bounds.Add( selectedItem.SceneRect );
			}

			anySelected = true;
		}

		if ( !anySelected )
		{
			return;
		}

		CenterOn( bounds.Center );
	}

	public void SelectAll()
	{
		foreach ( var node in Items )
		{
			node.Selected = true;
		}
		OnSelectionChanged?.Invoke();
	}

	public void ClearSelection()
	{
		foreach ( var item in SelectedItems )
		{
			item.Selected = false;
		}
		OnSelectionChanged?.Invoke();
	}

	internal void RemoveNode( NodeUI node )
	{
		var connections = Connections.Where( x => x.IsAttachedTo( node ) ).ToList();

		foreach ( var connection in connections )
		{
			connection.Disconnect();
			connection.Destroy();
		}

		if ( node.Node.CanRemove )
		{
			Graph?.RemoveNode( node.Node );
			node.Destroy();
		}
	}

	/// <summary>
	/// Perform automated fixes / replace obsolete nodes.
	/// </summary>
	public virtual void CleanUp()
	{

	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		var scenePosition = ToScene( e.LocalPosition );

		if ( GetPlugAt( scenePosition ) is { } plug && plug.Inner.CreateContextMenu( plug.Node, plug ) is { } plugMenu )
		{
			e.Accepted = true;

			plugMenu.OpenAt( e.ScreenPosition );
			return;
		}

		OpenContextMenu( e.ScreenPosition, scenePosition );
	}

	protected virtual IEnumerable<INodeType> GetRelevantNodes( NodeQuery query )
	{
		return Enumerable.Empty<INodeType>();
	}

	protected virtual void OnPopulateNodeMenuSpecialOptions( Menu menu, Vector2 clickPos, Plug targetPlug, string filter )
	{
		if ( CanPasteSelection() )
		{
			menu.AddOption( "Paste Node(s)", "content_paste", PasteSelection );
		}

		if ( !targetPlug.IsValid() )
		{
			menu.AddOption( "Add Comment", "notes", () =>
			{
				CreateNewComment( "Untitled", CommentColor.Green, clickPos, 300 );
			} );
		}
		else
		{
			menu.AddOption( "Add Reroute", "route", () =>
			{
				CreateNewNode( RerouteNodeType, clickPos, targetPlug );
			} );
		}
	}

	protected virtual void OnPopulateNodeMenu( Menu menu, Vector2 clickPos, Plug targetPlug, string filter )
	{
		var query = new NodeQuery( Graph, targetPlug is { IsValid: true, Inner: { } inner } ? inner : null, filter );
		var nodes = GetRelevantNodes( query ).ToArray();

		var useFilter = query.Filter.Count > 0;
		var truncated = 0;

		const int maxFilteredResults = 20;

		if ( useFilter && nodes.Length > maxFilteredResults )
		{
			truncated = nodes.Length - maxFilteredResults;
			nodes = nodes.Take( maxFilteredResults ).ToArray();
		}

		PopulateNodeMenu( menu, nodes, useFilter ? query : null, type => CreateNewNode( type, clickPos, targetPlug ) );

		if ( truncated > 0 )
		{
			var w = new Widget( null );
			w.Layout = Layout.Row();
			w.Layout.Margin = 6;
			w.Layout.Spacing = 4;

			w.Layout.Add( new Label( $"... and {truncated} more" ) );

			menu.AddWidget( w );
		}
	}

	private void PopulateNodeMenu( Menu menu, Vector2 clickPos, Plug targetPlug = null, string filter = null )
	{
		var visible = menu.Visible;

		var setFlag = typeof( Widget ).GetMethod( "SetFlag",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );

		if ( visible )
		{
			// Force WA_WState_Visible, this is a hack for updating menus quickly and without flickering.
			// Native tools does this same hack, maybe we can handle it better?
			// Maybe the better way is to create our own menu widget from scratch so I'm not going to bother yet.
			setFlag?.Invoke( menu, new object[] { 15, false } );
		}

		menu.RemoveMenus();
		menu.RemoveOptions();

		foreach ( var widget in menu.Widgets.Skip( 1 ) )
		{
			menu.RemoveWidget( widget );
		}

		if ( string.IsNullOrWhiteSpace( filter ) )
		{
			OnPopulateNodeMenuSpecialOptions( menu, clickPos, targetPlug, filter );
		}

		OnPopulateNodeMenu( menu, clickPos, targetPlug, filter );

		if ( visible )
		{
			setFlag?.Invoke( menu, new object[] { 15, true } );
			menu.AdjustSize();
			menu.Update();
		}
	}

	private static Menu.PathElement[] WithScore( Menu.PathElement[] path, NodeQuery query )
	{
		var copy = new Menu.PathElement[path.Length];

		Array.Copy( path, copy, path.Length );

		copy[^1] = copy[^1] with { Order = -query.GetScore( path ) };

		return copy;
	}

	public static void PopulateNodeMenu( Menu menu, IEnumerable<INodeType> nodes, NodeQuery? query, Action<INodeType> selectedAction )
	{
		menu.AddOptions( nodes, query is { } q ? x => WithScore( x.Path, q ) : x => x.Path,
			action: selectedAction,
			flat: query is not null,
			reduce: true );
	}

	private void OpenContextMenu( Vector2 pos, Vector2 clickPos, Plug targetPlug = null, Action onClose = null )
	{
		var menu = new ContextMenu( this );
		var anySelected = false;

		if ( !targetPlug.IsValid() )
		{
			var selectedNodes = SelectedItems.OfType<NodeUI>().ToArray();
			if ( selectedNodes.Any() )
			{
				anySelected = true;

				menu.AddOption( $"Cut {selectedNodes.Length} nodes", "content_cut", CutSelection );
				menu.AddOption( $"Copy {selectedNodes.Length} nodes", "content_copy", CopySelection );
				menu.AddOption( $"Delete {selectedNodes.Length} nodes", "delete", DeleteSelection );
				menu.AddSeparator();

				menu.AddOption( $"Add Comment for {selectedNodes.Length} nodes", "notes", () =>
				{
					Vector2 min = float.MaxValue;
					Vector2 max = float.MinValue;

					foreach ( var node in selectedNodes )
					{
						min = node.SceneRect.TopLeft.ComponentMin( min );
						max = node.SceneRect.BottomRight.ComponentMax( max );
					}

					min -= new Vector2( 32, 40 + 32 );
					max += 32;

					CreateNewComment( "Untitled", CommentColor.Green, min, max - min );
				} );
			}

			if ( GetNodeAt( clickPos ) is { } node )
			{
				if ( node.Node.GoToDefinition is { } goToDef )
				{
					anySelected = true;
					menu.AddOption( "Go to Definition", "read_more", goToDef );
				}

				if ( node.Node.CreateContextMenu( node ) is { } nodeMenu )
				{
					anySelected = true;
					menu.AddMenu( nodeMenu );
				}
			}
		}

		OnOpenContextMenu( menu, targetPlug );

		if ( !anySelected )
		{
			var nodeMenu = menu.OptionCount == 0 && menu.MenuCount == 0 ? menu : menu.AddMenu( "Create Node" );

			CreateNodeMenu( nodeMenu, pos, clickPos, targetPlug );
		}

		if ( onClose is not null )
		{
			menu.AboutToHide += onClose;
		}

		menu.OpenAt( pos, false );
	}

	protected virtual void OnOpenContextMenu( Menu menu, Plug targetPlug )
	{

	}

	private void CreateNodeMenu( Menu menu, Vector2 pos, Vector2 clickPos, Plug targetPlug = null )
	{
		menu.AboutToShow += () => PopulateNodeMenu( menu, clickPos, targetPlug );

		menu.AddLineEdit( "Filter",
			placeholder: "Filter Nodes..",
			autoFocus: true,
			onChange: s => PopulateNodeMenu( menu, clickPos, targetPlug, s ) );
	}

	public CommentUI CreateNewComment( string text, CommentColor color, Vector2 position, Vector2 size )
	{
		using var undoScope = UndoScope( "Add Comment" );

		var ui = (CommentUI)CreateNewNode( CommentNodeType, node =>
		{
			node.Position = position.SnapToGrid( GridSize );

			var comment = (ICommentNode)node;

			comment.Size = size;
			comment.Color = color;
			comment.Title = text;
		} );

		return ui;
	}

	public void CreateNewReroute( Vector2 position )
	{
		using var undoScope = UndoScope( "Add Reroute" );

		CreateNewNode( RerouteNodeType, position );
	}

	public NodeUI CreateNewNode( INodeType type, Vector2 position )
	{
		return CreateNewNode( type, node =>
			node.Position = position.SnapToGrid( GridSize ) );
	}

	public NodeUI CreateNewNode( INodeType type, Action<INode> onCreated = null )
	{
		if ( type == null )
			return null;

		var node = type.CreateNode( Graph );

		if ( node is null )
			return null;

		onCreated?.Invoke( node );

		Graph?.AddNode( node );

		OnNodeCreated( node );

		var nodeUI = node.CreateUI( this );
		Add( nodeUI );

		return nodeUI;
	}

	protected NodeUI CreateNodeUI( INode node )
	{
		var item = node.CreateUI( this );
		Add( item );

		return item;
	}

	public void CreateNewNode( INodeType type, Vector2 position, Plug targetPlug, bool selected = true )
	{
		using var undoScope = UndoScope( "Add Node" );

		var nodeUI = CreateNewNode( type, position );
		nodeUI.Selected = selected;

		if ( !targetPlug.IsValid() )
			return;

		if ( targetPlug is PlugIn plugIn )
		{
			if ( !type.TryGetOutput( plugIn.Inner.Type, out var targetName ) )
				return;

			if ( nodeUI.Outputs.FirstOrDefault( x => x.Inner.Identifier == targetName ) is { } match )
				CreateConnection( match, plugIn );
		}
		else if ( targetPlug is PlugOut plugOut )
		{
			if ( !type.TryGetInput( plugOut.Inner.Type, out var targetName ) )
				return;

			if ( nodeUI.Inputs.FirstOrDefault( x => x.Inner.Identifier == targetName ) is { } match )
				CreateConnection( plugOut, match );
		}

	}

	protected virtual void OnNodeCreated( INode node )
	{

	}

	SelectionBox _selectionBox;
	bool _dragging;

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		if ( e.IsDoubleClick )
		{
			var scenePosition = ToScene( e.LocalPosition );

			if ( GetPlugAt( scenePosition ) is { } plug )
			{
				plug.Inner.OnDoubleClick( plug.Node, plug, e );

				if ( e.Accepted ) return;
			}

			if ( GetNodeAt( scenePosition ) is { } node )
			{
				node.Node.OnDoubleClick( e );

				if ( e.Accepted ) return;
			}
		}

		if ( e.MiddleMouseButton )
		{
			e.Accepted = true;
			return;
		}

		if ( e.RightMouseButton )
		{
			e.Accepted = true;
			return;
		}

		if ( e.LeftMouseButton )
		{
			_dragging = true;
		}
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );

		_selectionBox?.Destroy();
		_selectionBox = null;
		_dragging = false;
	}

	protected override void OnMouseMove( MouseEvent e )
	{

		if ( _dragging && e.ButtonState.HasFlag( MouseButtons.Left ) )
		{
			// Selection box when holding left mouse button and dragging
			if ( !_selectionBox.IsValid() && !SelectedItems.Any() && !Items.Any( x => x.Hovered ) )
			{
				Add( _selectionBox = new SelectionBox( ToScene( e.LocalPosition ), this ) );
			}

			if ( _selectionBox.IsValid() )
			{
				_selectionBox.EndScene = ToScene( e.LocalPosition );
			}
		}
		else if ( _dragging )
		{
			// Release dragging if left mouse button is not down anymore
			_selectionBox?.Destroy();
			_selectionBox = null;
			_dragging = false;
		}

		var scenePosition = ToScene( e.LocalPosition );
		if ( _dragging )
		{
			var camTopLeft = ToScene( ContentRect.TopLeft );
			var camBottomRight = ToScene( ContentRect.BottomRight );
			var delta = Vector2.Zero;

			if ( scenePosition.x < camTopLeft.x ) delta.x = camTopLeft.x - scenePosition.x;
			else if ( scenePosition.x > camBottomRight.x ) delta.x = camBottomRight.x - scenePosition.x;
			if ( scenePosition.y < camTopLeft.y ) delta.y = camTopLeft.y - scenePosition.y;
			else if ( scenePosition.y > camBottomRight.y ) delta.y = camBottomRight.y - scenePosition.y;

			if ( !delta.IsNearlyZero() )
			{
				Translate( delta / 8f );
			}
		}
		else if ( e.ButtonState.HasFlag( MouseButtons.Middle ) ) // or space down?
		{
			var delta = scenePosition - lastMouseScenePosition;
			Translate( delta );
			e.Accepted = true;
			Cursor = CursorShape.ClosedHand;
		}
		else
		{
			Cursor = CursorShape.None;
		}

		e.Accepted = true;

		lastMouseScenePosition = ToScene( e.LocalPosition );
	}

	private void SetPlugZIndex( float value, bool inputs )
	{
		foreach ( var otherNode in Items.OfType<NodeUI>() )
		{
			if ( inputs )
			{
				foreach ( var plugIn in otherNode.Inputs )
				{
					plugIn.ZIndex = value;
				}
			}
			else
			{
				foreach ( var plugOut in otherNode.Outputs )
				{
					plugOut.ZIndex = value;
				}
			}
		}
	}

	private Plug GetPlugAt( Vector2 scenePosition )
	{
		var selectedItem = GetItemAt( scenePosition );

		if ( selectedItem is ValueEditor valueEditor )
		{
			return valueEditor.Parent as Plug;
		}

		return selectedItem as Plug;
	}

	private NodeUI GetNodeAt( Vector2 scenePosition )
	{
		if ( GetPlugAt( scenePosition ) is { } plug )
		{
			return plug.Node;
		}

		return GetItemAt( scenePosition ) as NodeUI;
	}

	private void SetPlugsZIndex( bool inputs, float? value )
	{
		foreach ( var node in Items.OfType<NodeUI>() )
		{
			if ( inputs )
			{
				foreach ( var input in node.Inputs )
				{
					input.ZIndex = value ?? input.DefaultZIndex;
				}
			}
			else
			{
				foreach ( var output in node.Outputs )
				{
					output.ZIndex = value ?? output.DefaultZIndex;
				}
			}
		}
	}

	private IDisposable MakePlugsDroppable( bool inputs )
	{
		SetPlugsZIndex( inputs, 5f );

		return new DisposeAction( () =>
		{
			SetPlugsZIndex( inputs, null );
		} );
	}

	internal void DraggingPlug( Plug plug, Vector2 scenePosition, Connection source )
	{
		using var _ = MakePlugsDroppable( plug is PlugOut );

		var dropTarget = GetPlugAt( scenePosition );

		if ( plug is PlugIn ) dropTarget = dropTarget as PlugOut;
		else dropTarget = dropTarget as PlugIn;

		DropTarget?.Update();
		DropTarget = dropTarget?.Node != plug.Node ? dropTarget : null;
		DropTarget?.Update();

		if ( !Preview.IsValid() )
		{
			Preview = new Connection( plug );
			Add( Preview );
		}

		Preview.LayoutForPreview( plug, scenePosition, DropTarget );
	}

	internal void DroppedPlug( Plug plug, Vector2 scenePosition, Connection source )
	{
		bool disconnected = false;
		bool connected = false;

		if ( source.IsValid() )
		{
			// Dropped on the same connection it was already connected to
			if ( source.Input == DropTarget )
			{
				Preview?.Destroy();
				Preview = null;
				return;
			}

			disconnected = true;

			if ( DropTarget.IsValid() )
			{
				PushUndo( "Change Connection" );
			}
			else
			{
				PushUndo( "Drop Connection" );
			}

			source.Disconnect();
			source.Destroy();
		}

		if ( DropTarget.IsValid() && DropTarget.Node != plug.Node )
		{
			connected = true;

			if ( !disconnected )
			{
				PushUndo( "Create Connection" );
			}

			var connections = Connections.Where( x => x.Input == DropTarget ).ToList();
			foreach ( var connection in connections )
			{
				RemoveConnection( connection );
				connection.Destroy();
			}

			CreateConnection( plug as PlugOut ?? DropTarget as PlugOut, plug as PlugIn ?? DropTarget as PlugIn );
		}

		DropTarget?.Update();
		DropTarget = null;

		if ( disconnected || connected )
		{
			PushRedo();
		}

		if ( !disconnected && !connected )
		{
			if ( _createReroute )
			{
				CreateNewNode( RerouteNodeType, scenePosition, plug, false );
			}
			else
			{
				OpenContextMenu( ToScreen( FromScene( scenePosition ) ), scenePosition, plug, onClose: () =>
				{
					Preview?.Destroy();
					Preview = null;
				} );
				return;
			}
		}

		Preview?.Destroy();
		Preview = null;
	}

	private Connection CreateConnection( PlugOut nodeOutput, PlugIn dropTarget, bool uiOnly = false )
	{
		ArgumentNullException.ThrowIfNull( nodeOutput );
		ArgumentNullException.ThrowIfNull( dropTarget );

		if ( !uiOnly )
		{
			dropTarget.Inner.ConnectedOutput = nodeOutput.Inner;
		}

		if ( !nodeOutput.Inner.ShowConnection || !dropTarget.Inner.ShowConnection )
		{
			return null;
		}

		var connection = new Connection( nodeOutput, dropTarget );
		Add( connection );

		connection.Layout();

		Connections.Add( connection );

		return connection;
	}

	internal void RemoveConnection( Connection c )
	{
		if ( c.Input.Inner.ConnectedOutput == c.Output.Inner )
		{
			c.Input.Inner.ConnectedOutput = null;
		}

		Connections.Remove( c );
	}

	internal void RemoveConnections( PlugIn plugIn )
	{
		var connections = Connections
			.Where( x => x.Input == plugIn )
			.ToArray();

		foreach ( var connection in connections )
		{
			connection.Disconnect();
			connection.Destroy();
		}
	}

	internal void RemoveConnections( PlugOut plugOut )
	{
		var connections = Connections
			.Where( x => x.Output == plugOut )
			.ToArray();

		foreach ( var connection in connections )
		{
			connection.Disconnect();
			connection.Destroy();
		}
	}

	internal void RerouteConnection( Connection c, Vector2 scenePosition )
	{
		using var undoScope = UndoScope( "Reroute Connection" );

		var nodeUI = CreateNewNode( RerouteNodeType, scenePosition );
		var input = nodeUI.Inputs.FirstOrDefault();
		var output = nodeUI.Outputs.FirstOrDefault();
		CreateConnection( c.Output, input );
		CreateConnection( output, c.Input );

		Connections.Remove( c );
		c.Destroy();
	}

	internal void NodePositionChanged( NodeUI node )
	{
		foreach ( var connection in Connections )
		{
			if ( !connection.IsAttachedTo( node ) )
				continue;

			connection.Layout();
		}
	}

	public void RebuildFromGraph()
	{
		Preview?.Destroy();
		Preview = null;

		DropTarget?.Update();
		DropTarget = null;

		Connections.Clear();

		OnClear();

		DeleteAllItems();

		if ( _graph is not null ) BuildFromNodes( _graph.Nodes, true );

		OnRebuild();
		RestoreViewFromCookie();
	}

	protected virtual void OnClear()
	{

	}

	/// <summary>
	/// Create or update the <see cref="NodeUI"/> representation of a set of <see cref="INode"/>s and their connections.
	/// </summary>
	/// <param name="nodes">Set of nodes to create / update the UI for.</param>
	/// <param name="insert">If true, we're inserting new nodes so there won't be any existing UI elements for them.</param>
	/// <param name="offset">Optional position offset to apply to new nodes.</param>
	/// <param name="selectNew">If true, select newly-created nodes and connections.</param>
	public void BuildFromNodes( IEnumerable<INode> nodes, bool insert, Vector2 offset = default, bool selectNew = false )
	{
		var nodesSet = nodes.ToImmutableHashSet();

		if ( !insert )
		{
			var removed = Items
				.OfType<NodeUI>()
				.Where( x => !nodesSet.Contains( x.Node ) )
				.ToArray();

			foreach ( var nodeUi in removed )
			{
				nodeUi.Destroy();
			}
		}

		foreach ( var node in nodesSet )
		{
			if ( !insert && FindNode( node ) is { } nodeUi )
			{
				nodeUi.Rebuild();
			}
			else
			{
				node.Position += offset;

				nodeUi = node.CreateUI( this );
				if ( !nodeUi.IsValid() )
					continue;

				Add( nodeUi );

				nodeUi.Position = node.Position;

				if ( selectNew )
				{
					nodeUi.Selected = true;
				}
			}
		}

		UpdateConnections( nodesSet, selectNew );
	}

	protected virtual void OnRebuild()
	{

	}

	public void UpdateConnections( IEnumerable<INode> nodes, bool selectNew = false )
	{
		var nodeDict = new Dictionary<INode, NodeUI>();
		var connectionSet = new HashSet<(IPlug, IPlug)>();

		foreach ( var connection in Connections.ToArray() )
		{
			if ( connection.Input.Inner.ConnectedOutput != connection.Output.Inner
				|| !connection.Input.Inner.ShowConnection
				|| !connection.Output.Inner.ShowConnection )
			{
				connection.Destroy();

				Connections.Remove( connection );
			}
			else
			{
				connectionSet.Add( (connection.Output.Inner, connection.Input.Inner) );
			}
		}

		foreach ( var nodeUi in Items.OfType<NodeUI>() )
		{
			nodeDict.Add( nodeUi.Node, nodeUi );
		}

		var nodeSet = new HashSet<INode>( nodes );

		// Find inputs connected to the given set of nodes too

		foreach ( var node in Graph.Nodes )
		{
			if ( nodeSet.Contains( node ) )
			{
				continue;
			}

			foreach ( var input in node.Inputs )
			{
				if ( nodeSet.Contains( input.ConnectedOutput?.Node ) )
				{
					nodeSet.Add( node );
					break;
				}
			}
		}

		foreach ( var node in nodeSet )
		{
			foreach ( var input in node.Inputs )
			{
				if ( input.ConnectedOutput is not { } output )
					continue;

				if ( !input.ShowConnection || !output.ShowConnection )
					continue;

				if ( !connectionSet.Add( (output, input) ) )
					continue;

				nodeDict.TryGetValue( node, out var a );
				nodeDict.TryGetValue( output.Node, out var b );

				var dropTarget = a?.Inputs.FirstOrDefault( x => x.Inner == input );
				var nodeOutput = b?.Outputs.FirstOrDefault( x => x.Inner == output );

				if ( !dropTarget.IsValid() || !nodeOutput.IsValid() )
					continue;

				var connection = CreateConnection( nodeOutput, dropTarget, true );

				if ( selectNew && connection.IsValid() )
				{
					connection.Selected = true;
				}
			}
		}

		foreach ( var connection in Connections.ToArray() )
		{
			if ( connectionSet.Contains( (connection.Output?.Inner, connection.Input?.Inner) ) )
			{
				connection.Layout();
			}
		}
	}

	public HandleConfig DefaultHandleConfig { get; } = new( null, Color.Parse( "#999" )!.Value );

	protected virtual HandleConfig OnGetHandleConfig( Type type )
	{
		return DefaultHandleConfig;
	}

	private Dictionary<Type, HandleConfig> HandleConfigCache { get; } = new();

	public HandleConfig GetHandleConfig( Type t )
	{
		if ( HandleConfigCache.TryGetValue( t, out var config ) )
		{
			return config;
		}

		config = OnGetHandleConfig( t );

		return HandleConfigCache[t] = config with { Name = config.Name ?? t.ToSimpleString( false ).HtmlEncode() };
	}

	public NodeUI FindNode( INode node )
	{
		if ( node == null )
			return null;

		return Items.OfType<NodeUI>().FirstOrDefault( x => x.Node == node );
	}

	public NodeUI SelectNode( INode node )
	{
		if ( node == null )
			return null;

		foreach ( var item in SelectedItems )
		{
			item.Selected = false;
		}

		var nodeUI = Items.OfType<NodeUI>().FirstOrDefault( x => x.Node == node );
		nodeUI.Selected = true;

		return nodeUI;
	}

	public void UpdateNode( INode node )
	{
		if ( node == null )
			return;

		var nodeUI = Items.OfType<NodeUI>().FirstOrDefault( x => x.Node == node );
		if ( nodeUI.IsValid() )
		{
			nodeUI.Update();
		}
	}

	protected virtual INodeType NodeTypeFromDragEvent( DragEvent ev )
	{
		return null;
	}

	public override void OnDragHover( DragEvent ev )
	{
		base.OnDragHover( ev );

		if ( _hasDropped )
		{
			return;
		}

		var position = ToScene( ev.LocalPosition ).SnapToGrid( GridSize );

		if ( !_nodePreview.IsValid() )
		{
			if ( NodeTypeFromDragEvent( ev ) is not { } type )
			{
				ev.Action = DropAction.Ignore;
				return;
			}

			ev.Action = DropAction.Link;

			var node = type.CreateNode( Graph );
			node.Position = ToScene( ev.LocalPosition ).SnapToGrid( GridSize );

			_nodePreview = CreateNodeUI( node );
		}
		else
		{
			_nodePreview.Position = position;
		}
	}

	public override void OnDragLeave()
	{
		base.OnDragLeave();

		if ( _hasDropped )
		{
			return;
		}

		if ( _nodePreview.IsValid() )
		{
			Graph.RemoveNode( _nodePreview.Node );
			_nodePreview.Destroy();
			_nodePreview = null;
		}
	}

	public override void OnDragDrop( DragEvent ev )
	{
		base.OnDragDrop( ev );

		if ( _hasDropped )
		{
			return;
		}

		Focus();

		_hasDropped = true;

		if ( _nodePreview.IsValid() )
		{
			Graph.RemoveNode( _nodePreview.Node );
			_nodePreview.Destroy();
			_nodePreview = null;
		}

		if ( NodeTypeFromDragEvent( ev ) is not { } type )
		{
			return;
		}

		CreateNewNode( type, ToScene( ev.LocalPosition ), null );
	}
}
