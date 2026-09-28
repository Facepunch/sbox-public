using System.Text.RegularExpressions;

namespace Editor;

[Dock( "Editor", "Hierarchy", "list", DockArea.Right )]
public partial class SceneTreeWidget : Widget
{
	public TreeView TreeView { get; private set; }

	Layout Header;
	Layout SubHeader;
	LineEdit Search;
	ToolButton SearchClear;

	IDisposable _selectionUndoScope = null;

	/// <summary>
	/// Which items were open before the current search, restored when it's cleared.
	/// </summary>
	HashSet<object> _openBeforeSearch;

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

		var add = SubHeader.Add( new HeaderButton( "add" ) );
		add.MouseLeftPress = CreateGameObjectMenu;

		// The box holds the advanced filter's chips ahead of the text, so every active filter shows
		// where the search is, each with an X to remove it.
		var searchBox = SubHeader.Add( new SearchBox(), 1 );
		searchBox.Layout = Layout.Row();
		searchBox.Layout.Margin = new Sandbox.UI.Margin( 3, 0, 0, 0 );
		searchBox.Layout.Spacing = 3;

		_chips = searchBox.Layout.AddRow();
		_chips.Spacing = 3;
		RebuildChips();

		Search = searchBox.Layout.Add( new LineEdit(), 1 );
		Search.PlaceholderText = "⌕  Search";
		Search.SetStyles( "background-color: transparent; border: 0px;" );
		Search.TextChanged += x => queryDirty = true;
		Search.FixedHeight = Theme.RowHeight;

		SearchClear = searchBox.Layout.Add( new ToolButton( string.Empty, "clear", this ) );
		SearchClear.ToolTip = "Clear the search and filters";
		SearchClear.MouseLeftPress = () =>
		{
			Search.Text = string.Empty;
			ClearFilters();
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

		var filter = SubHeader.Add( new HeaderButton( "filter_list" ) { ToolTip = "Advanced filter" } );
		filter.MouseLeftPress = () => OpenFilterPopup( filter );

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

		var sceneChanged = !ReferenceEquals( last, session.Scene );

		// if query AND scene is unchanged - no need to rebuild the tree
		if ( !queryDirty && !sceneChanged )
			return;

		_lastScene.SetTarget( session.Scene );

		// Expand state is keyed by GameObjects, so it pins whatever scene they belong to. Close
		// the ones whose scene has gone, or we hold every closed scene for the whole session.
		// Only the closed ones: switching between two open scenes must keep both expanded.
		if ( sceneChanged )
			CloseItemsFromClosedScenes();

		queryDirty = false;
		Rebuild();
	}

	void CloseItemsFromClosedScenes()
	{
		foreach ( var item in TreeView.OpenItems.ToArray() )
		{
			if ( item is GameObject go && !IsSceneOpen( go.Scene ) )
				TreeView.Close( item );
		}
	}

	static bool IsSceneOpen( Scene scene )
	{
		if ( scene is null )
			return false;

		foreach ( var session in SceneEditorSession.All )
		{
			if ( ReferenceEquals( session.Scene, scene ) )
				return true;
		}

		return false;
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

		bool hasSearch = !string.IsNullOrEmpty( Search.Text ) || Filters.Count > 0;
		SearchClear.Visible = hasSearch;

		// Searching opens every parent of a match. Put the tree back as it was when the search ends.
		if ( hasSearch )
		{
			_openBeforeSearch ??= TreeView.OpenItems.ToHashSet();
		}
		else if ( _openBeforeSearch is not null )
		{
			foreach ( var item in TreeView.OpenItems.ToArray() )
				TreeView.Close( item );

			foreach ( var item in _openBeforeSearch.Where( x => x is not GameObject go || go.IsValid() ) )
				TreeView.Open( item );

			_openBeforeSearch = null;
		}

		var scene = session.Scene;
		if ( hasSearch )
		{
			// search view: the matches, under the parents they sit in
			var matches = new HashSet<GameObject>();

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

				if ( !go.Name.Contains( search, StringComparison.OrdinalIgnoreCase ) )
					continue;

				if ( !PassesFilters( go ) )
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

				matches.Add( go );
			}

			// Everything on the way down to a match, opened so the matches are in view.
			var shown = new HashSet<GameObject>( matches );
			foreach ( var match in matches )
			{
				for ( var parent = match.Parent; parent is not null && parent.Parent is not null; parent = parent.Parent )
				{
					TreeView.Open( parent );

					// Already walked from here, or will be as a match of its own.
					if ( !shown.Add( parent ) )
						break;
				}
			}

			var results = new GameObjectSearchNode.Results( matches, shown );
			foreach ( var root in scene.Children.Where( shown.Contains ) )
			{
				TreeView.AddItem( new GameObjectSearchNode( root, results ) );
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

file class HeaderButton : Widget
{
	public string Icon;

	public HeaderButton( string icon ) : base( null )
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

/// <summary>
/// Draws the search input's background, so the filter chips and the text read as one field.
/// </summary>
file class SearchBox : Widget
{
	public SearchBox() : base( null )
	{
		FixedHeight = Theme.RowHeight;
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect, Theme.ControlRadius );
	}
}
