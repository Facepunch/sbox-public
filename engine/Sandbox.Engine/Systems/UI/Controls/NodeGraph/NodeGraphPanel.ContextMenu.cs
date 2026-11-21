namespace Sandbox.UI.NodeGraph;

//
// The right-click menu: the selection's commands and the node's own entries, or the node menu.
//
public partial class NodeGraphPanel
{
	Menu _menu;

	/// <summary>
	/// Fill the right-click menu for <paramref name="node"/>, the node clicked and already selected,
	/// laid out like the editor's node graph: cut, copy, duplicate or delete the selection, the
	/// node's own entries, then comment it or take it out of its comment. <paramref name="position"/>
	/// is where, in canvas units. Left empty, the menu is the node menu instead.
	/// </summary>
	protected virtual void BuildContextMenu( Menu menu, NodePanel node, Vector2 position )
	{
		if ( node is null ) return;

		var copyable = CopyableSelection().Length;

		if ( copyable > 0 && SerializeSelection() is not null )
		{
			menu.AddOption( $"Cut {NodeCount( copyable )}", "content_cut", CutSelection );
			menu.AddOption( $"Copy {NodeCount( copyable )}", "content_copy", CopySelection );
			menu.AddOption( $"Duplicate {NodeCount( copyable )}", "file_copy", DuplicateSelection );
		}

		var removable = SelectedNodes.Count( x => x.Node.CanRemove );

		if ( removable > 0 )
		{
			menu.AddOption( $"Delete {NodeCount( removable )}", "delete", DeleteSelection );
		}

		if ( node.Node.GoToDefinition is { } goToDefinition )
		{
			menu.AddOption( "Go to Definition", "read_more", goToDefinition );
		}

		// A menu with options of its own is a submenu row
		if ( node.Node.CreateContextMenu( node ) is { } nodeMenu )
		{
			menu.AddOption( nodeMenu );
		}

		var inComment = FindComment( node ) is not null;
		var fittable = node is CommentPanel { Comment.Members.Count: > 0 };
		if ( !inComment && !fittable && CommentNodeType is null ) return;

		if ( menu.HasOptions ) menu.AddSeparator();

		if ( fittable )
		{
			var comment = (CommentPanel)node;
			menu.AddOption( "Fit to Content", "fit_screen", comment.FitToContent );
		}

		if ( inComment )
		{
			var members = SelectedNodes.Where( x => FindComment( x ) is not null ).ToArray();
			menu.AddOption( "Remove from comment", "playlist_remove", () => RemoveFromComment( members ) );
		}

		if ( CommentNodeType is null ) return;

		// Things inside a selected comment stay in it; the new comment takes the comment
		var selectedComments = SelectedNodes.OfType<CommentPanel>().ToArray();
		var selected = SelectedNodes.Where( x => !selectedComments.Any( c => c != x && IsWithin( x, c ) ) ).ToArray();
		menu.AddOption( $"Add Comment for {NodeCount( selected.Length )}", "notes", () => CreateComment( position, selected ) );
	}

	static string NodeCount( int count ) => $"{count} {(count == 1 ? "node" : "nodes")}";

	/// <summary>
	/// Right-clicking a node selects it, then opens the menu for it.
	/// </summary>
	internal void OpenContextMenu( NodePanel node )
	{
		if ( node is not null && !node.Selected ) SelectNode( node );

		OpenContextMenu( ScreenToCanvas( MousePosition ), node );
	}

	/// <summary>
	/// Open the right-click menu at the mouse for <paramref name="node"/>, or for empty space when
	/// null, at <paramref name="position"/> in canvas units. When nothing's on offer for a node -
	/// always on empty space, or for a wire dropped from <paramref name="targetPlug"/> - it's the
	/// node menu; otherwise the node menu is its "Create Node" submenu. <paramref name="onClose"/>
	/// runs once it's gone, or straight away when there was nothing to open.
	/// </summary>
	protected virtual void OpenContextMenu( Vector2 position, NodePanel node, PlugPanel targetPlug = null, Action onClose = null )
	{
		var menu = new Menu();

		if ( targetPlug is null ) BuildContextMenu( menu, node, position );

		var anySelected = menu.HasOptions;

		OnOpenContextMenu( menu, targetPlug );

		if ( !anySelected )
		{
			if ( !menu.HasOptions )
			{
				CreateNodeMenu( menu, position, targetPlug );
			}
			else
			{
				var nodeMenu = new Menu( "Create Node", "add" );
				CreateNodeMenu( nodeMenu, position, targetPlug );

				if ( nodeMenu.Options.Count > 0 ) menu.AddOption( nodeMenu );
				else nodeMenu.Delete( true );
			}
		}

		if ( onClose is not null ) menu.Closed += _ => onClose();

		if ( !OpenMenu( menu ) ) onClose?.Invoke();
	}

	/// <summary>
	/// Add to the right-click menu before it opens. <paramref name="targetPlug"/> is the plug a
	/// wire was dropped from, if that's what opened it.
	/// </summary>
	protected virtual void OnOpenContextMenu( Menu menu, PlugPanel targetPlug ) { }

	/// <summary>
	/// Open a menu under the mouse, filled by <paramref name="build"/>, closing any this graph has
	/// open. Nothing opens when it adds no options.
	/// </summary>
	protected void OpenMenu( Action<Menu> build )
	{
		var menu = new Menu();
		build( menu );
		OpenMenu( menu );
	}

	/// <summary>
	/// Open <paramref name="menu"/> under the mouse, closing any this graph has open, and put the
	/// keyboard in its first text field. It's deleted once closed. False, and deleted, when it has
	/// no options to pick.
	/// </summary>
	protected internal bool OpenMenu( Menu menu )
	{
		_menu?.Delete( true );
		_menu = null;

		if ( menu.Options.Count == 0 )
		{
			menu.Delete( true );
			return false;
		}

		menu.Open( this, Popup.PositionMode.UnderMouse );

		if ( !menu.IsOpen )
		{
			menu.Delete( true );
			return false;
		}

		_menu = menu;
		menu.Closed += OnMenuClosed;

		menu.Rows.OfType<TextEntry>().FirstOrDefault()?.Focus();
		return true;
	}

	void OnMenuClosed( Menu menu )
	{
		if ( _menu == menu ) _menu = null;
		menu.Delete( true );
	}

	protected override void OnRightClick( MousePanelEvent e )
	{
		if ( Graph is null ) return;

		OpenContextMenu( null );
		e.StopPropagation();
	}
}
