using System.Text.RegularExpressions;

namespace Editor;

[Dock( "Editor", "Hierarchy", "list" )]
public partial class SceneTreeWidget : Widget
{
	public TreeView TreeView { get; private set; }

	Layout Header;
	Layout SubHeader;
	LineEdit Search;
	ToolButton SearchClear;

	IDisposable _selectionUndoScope = null;

	public static SceneTreeWidget Current { get; private set; }

	public SceneTreeWidget( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();

		Current = this;

		BuildUI();
	}

	public void BuildUI()
	{
		Layout.Clear( true );
		Header = Layout.AddColumn();

		SubHeader = Layout.AddRow();
		SubHeader.Spacing = 2;
		SubHeader.Margin = new Sandbox.UI.Margin( 0, 2 );
		SubHeader.Alignment = TextFlag.LeftCenter;

		var add = SubHeader.Add( new AddButton( "add" ) );
		add.MouseLeftPress = CreateGameObjectMenu;

		Search = SubHeader.Add( new LineEdit(), 1 );
		Search.PlaceholderText = "⌕  Search";
		Search.Layout = Layout.Row();
		Search.Layout.AddStretchCell( 1 );
		Search.TextChanged += x => queryDirty = true;
		Search.FixedHeight = Theme.RowHeight;

		SearchClear = Search.Layout.Add( new ToolButton( string.Empty, "clear", this ) );
		SearchClear.MouseLeftPress = () =>
		{
			Search.Text = string.Empty;
			Rebuild();

			// make sure we're open to the stuff we picked from search
			foreach ( var item in TreeView.Selection )
			{
				TreeView.ExpandPathTo( item );
			}
			TreeView.UpdateIfDirty();

			var scrollTarget = TreeView.Selection.FirstOrDefault();
			if ( scrollTarget is not null )
			{
				TreeView.ScrollTo( scrollTarget );
			}
		};
		SearchClear.Visible = false;

		TreeView = new TreeView();
		TreeView.MultiSelect = true;
		TreeView.BodyDropTarget = TreeView.DragDropTarget.LastRoot;
		TreeView.BodyContextMenu = OpenTreeViewContextMenu;

		TreeView.OnBeforeSelection = x => _selectionUndoScope = SceneEditorSession.Active.UndoScope( "Select GameObject(s)" ).Push();
		TreeView.OnBeforeDeselection = x => _selectionUndoScope = SceneEditorSession.Active.UndoScope( "Deselect GameObject(s)" ).Push();
		TreeView.OnSelectionChanged = x =>
		{
			_selectionUndoScope?.Dispose();
			_selectionUndoScope = null;
		};

		TreeView.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( TreeView.LocalRect, Theme.ControlRadius );

			return false;
		};

		Layout.Add( TreeView, 1 );

		_lastScene.SetTarget( null );
		CheckForChanges();

		EditorUtility.OnInspect -= OnInspect;
		EditorUtility.OnInspect += OnInspect;
	}

	void CreateGameObjectMenu()
	{
		var m = new ContextMenu( TreeView );

		using var scope = SceneEditorSession.Scope();
		var selected = EditorScene.Selection.FirstOrDefault() as GameObject;

		GameObjectNode.CreateObjectMenu( m, selected, go =>
		{
			TreeView.Open( this );
			TreeView.SelectItem( go, skipEvents: true );
			TreeView.BeginRename();
		} );

		m.OpenAtCursor( false );
	}

	void OpenTreeViewContextMenu()
	{
		var rootItem = TreeView.Items.FirstOrDefault();
		if ( rootItem is null ) return;

		if ( rootItem is TreeNode node )
		{
			node.OnContextMenu();
		}
	}

	WeakReference<Scene> _lastScene = new( null );
	bool queryDirty = false;

	[EditorEvent.Frame]
	public void CheckForChanges()
	{
		var session = SceneEditorSession.Active;
		if ( session is null )
			return;

		_lastScene.TryGetTarget( out var last );

		// if query AND scene is unchanged - no need to rebuild the tree
		if ( !queryDirty && ReferenceEquals( last, session.Scene ) )
			return;

		_lastScene.SetTarget( session.Scene );

		queryDirty = false;
		Rebuild();
	}

	private void Rebuild()
	{
		var session = SceneEditorSession.Active;

		Header.Clear( true );

		// Copy the current selection as we're about to kill it
		var selection = TreeView.Selection.Select( x => x as GameObject );

		// treeview will clear the selection, so give it a new one to clear
		TreeView.Selection = new SelectionSystem();
		TreeView.Clear();

		if ( session is null )
			return;

		bool hasSearch = !string.IsNullOrEmpty( Search.Text );
		SearchClear.Visible = hasSearch;

		var scene = session.Scene;
		if ( hasSearch )
		{
			// flat search view

			var tokens = Regex.Matches( Search.Text, @"(\w+):(\S+)" )
			  .ToDictionary( m => m.Groups[1].Value, m => m.Groups[2].Value );

			var search = Regex.Replace( Search.Text, @"\b\w+:\S+\b", "" ).Trim();

			IEnumerable<GameObject> objects = Enumerable.Empty<GameObject>();
			if ( tokens.TryGetValue( "id", out string idfilter ) )
			{
				if ( Guid.TryParse( idfilter, out Guid guid ) )
				{
					var obj = scene.Directory.FindByGuid( guid );
					objects = new List<GameObject>() { obj };
				}
			}
			else
			{
				objects = scene.Directory.GetAll();
			}

			foreach ( var go in objects )
			{
				if ( !go.IsValid() ) continue;

				if ( go.Parent is null || go.Flags.HasFlag( GameObjectFlags.Hidden ) )
					continue;

				if ( go.IsPrefabInstance && !go.IsPrefabInstanceRoot )
					continue;

				if ( !go.Name.Contains( search, StringComparison.OrdinalIgnoreCase ) )
					continue;

				if ( tokens.TryGetValue( "t", out string typeFilter ) )
				{
					var types = go.Components.GetAll().Select( x => EditorTypeLibrary.GetType( x.GetType() ) );
					if ( types.FirstOrDefault( x => x.Name.Equals( typeFilter, StringComparison.OrdinalIgnoreCase ) ) is null )
						continue;
				}

				if ( tokens.TryGetValue( "tag", out string tagFilter ) )
				{
					if ( !go.Tags.Contains( tagFilter ) )
						continue;
				}

				TreeView.AddItem( new GameObjectSearchNode( go ) );
			}
		}
		else
		{
			// normal heirarchy tree

			if ( scene is PrefabScene prefabScene )
			{
				var node = TreeView.AddItem( new PrefabNode( prefabScene ) );
				TreeView.Open( node );
			}
			else
			{
				var node = TreeView.AddItem( new SceneNode( scene ) );
				TreeView.Open( node );
			}
		}

		TreeView.Selection = session.Selection;

		// Go through the current scene
		// Feel like this could be loads faster
		foreach ( var go in scene.GetAllObjects( false ) )
		{
			// If we find a matching item in our new scene
			if ( selection.FirstOrDefault( x => x.IsValid() && x.Id == go.Id ).IsValid() )
			{
				// Add it to the current selection
				TreeView.Selection.Add( go );
			}
		}
	}

	public void OnInspect( EditorUtility.OnInspectArgs args )
	{
		foreach ( var item in TreeView.Selection )
		{
			TreeView.ExpandPathTo( item );
		}
		var scrollTarget = TreeView.Selection.FirstOrDefault();
		if ( scrollTarget is not null )
		{
			TreeView.ScrollTo( scrollTarget );
		}
	}
}

file class AddButton : Widget
{
	public string Icon;

	public AddButton( string icon ) : base( null )
	{
		Icon = icon;

		Cursor = CursorShape.Finger;
		FixedHeight = Theme.RowHeight;
	}

	protected override Vector2 SizeHint()
	{
		return new Vector2( Theme.RowHeight );
	}

	protected override void OnPaint()
	{
		Paint.ClearBrush();
		Paint.ClearPen();

		var color = Enabled ? Theme.ControlBackground : Theme.SurfaceBackground;

		if ( Enabled && Paint.HasMouseOver )
		{
			color = color.Lighten( 0.1f );
		}

		Paint.ClearPen();
		Paint.SetBrush( color );
		Paint.DrawRect( LocalRect, Theme.ControlRadius );

		Paint.ClearBrush();
		Paint.ClearPen();
		Paint.SetPen( Theme.Primary );

		Paint.DrawIcon( LocalRect, Icon, 14, TextFlag.Center );
	}
}
