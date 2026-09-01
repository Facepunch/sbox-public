namespace Sandbox.PanelGallery;

/// <summary>
/// The real scene tree - the GameObjects of whatever scene the editor has open. Rebuilds when the
/// scene changes underneath us, or when a branch is expanded.
/// </summary>
public class Hierarchy : Panel
{
	public Action<GameObject> OnSelected { get; set; }

	readonly HashSet<Guid> collapsed = new();
	readonly Dictionary<GameObject, Panel> rows = new();

	GameObject selected;
	string filter = "";
	int builtHash;
	int rowIndex;
	RealTimeSince timeSinceChecked;

	public Hierarchy()
	{
		AddClass( "scroll" );
	}

	/// <summary>
	/// The scene the editor has open, if any.
	/// </summary>
	public static Scene ActiveScene => SceneEditorSession.Active?.Scene;

	/// <summary>
	/// Only show objects matching this, and whatever they hang off. Empty shows everything.
	/// </summary>
	public void SetFilter( string value )
	{
		value ??= "";
		if ( value == filter ) return;

		filter = value;
		Rebuild();
	}

	public void Select( GameObject item )
	{
		if ( selected == item ) return;

		selected = item;
		if ( item.IsValid() ) sceneSelected = false;

		// Drive the editor's own selection as well, so everything else follows along
		if ( SceneEditorSession.Active is { } session && item.IsValid() )
		{
			session.Selection.Set( item );
		}

		// Just move the class - rebuilding the tree would replay every row's intro
		foreach ( var (gameObject, row) in rows )
		{
			row.SetClass( "selected", gameObject == selected );
		}

		rootRow?.SetClass( "selected", sceneSelected );

		OnSelected?.Invoke( item );
	}

	public override void Tick()
	{
		// Follow selection made anywhere else - the editor's own viewport, or ours
		if ( SceneEditorSession.Active?.Selection.FirstOrDefault() is GameObject picked && picked != selected )
		{
			Select( picked );
		}

		// Watch for the scene changing shape under us - names, parenting, enabled. No need to walk
		// the whole thing every frame
		if ( timeSinceChecked < 0.25f ) return;

		timeSinceChecked = 0;

		var hash = SceneHash();
		if ( hash == builtHash ) return;

		Rebuild();
	}

	int SceneHash()
	{
		var scene = ActiveScene;
		if ( !scene.IsValid() ) return 0;

		var hash = new HashCode();

		foreach ( var child in scene.Children )
		{
			Hash( ref hash, child );
		}

		return hash.ToHashCode();
	}

	void Hash( ref HashCode hash, GameObject item )
	{
		if ( !item.IsValid() ) return;
		if ( item.Flags.HasFlag( GameObjectFlags.Hidden ) ) return;

		hash.Add( item.Id );
		hash.Add( item.Name );
		hash.Add( item.Enabled );

		if ( filter.Length == 0 && collapsed.Contains( item.Id ) )
		{
			hash.Add( "collapsed" );
			return;
		}

		foreach ( var child in item.Children )
		{
			Hash( ref hash, child );
		}
	}

	void Rebuild()
	{
		DeleteChildren( true );
		rows.Clear();

		rowIndex = 0;
		builtHash = SceneHash();

		var scene = ActiveScene;
		if ( !scene.IsValid() )
		{
			Add.Label( "No scene open", "treerow" );
			return;
		}

		// The scene itself is the root of the tree, same as the editor's
		var root = Add.Panel( "treerow root" );
		root.SetClass( "selected", selected is null && sceneSelected );
		root.Add.Panel().Style.Width = 6;
		root.Icon( "expand_more", "arrow" );
		root.Icon( "movie", "kind" );
		root.Add.Label( scene.Name ?? "Untitled", "name" );

		rootRow = root;

		root.AddEventListener( "onclick", () =>
		{
			sceneSelected = true;
			selected = null;

			foreach ( var row in rows.Values ) row.SetClass( "selected", false );
			root.SetClass( "selected", true );

			OnSelected?.Invoke( null );
		} );

		foreach ( var child in scene.Children )
		{
			AddRow( child, 1 );
		}
	}

	bool sceneSelected;
	Panel rootRow;

	/// <summary>
	/// Does this object, or anything under it, match what's being searched for?
	/// </summary>
	bool Matches( GameObject item )
	{
		if ( filter.Length == 0 ) return true;
		if ( !item.IsValid() ) return false;

		if ( item.Name is not null && item.Name.Contains( filter, StringComparison.OrdinalIgnoreCase ) )
			return true;

		foreach ( var child in item.Children )
		{
			if ( Matches( child ) ) return true;
		}

		return false;
	}

	void AddRow( GameObject item, int depth )
	{
		if ( !item.IsValid() ) return;
		if ( item.Flags.HasFlag( GameObjectFlags.Hidden ) ) return;
		if ( !Matches( item ) ) return;

		// Searching opens everything up, otherwise the match stays hidden in a closed branch
		var expanded = filter.Length > 0 || !collapsed.Contains( item.Id );

		var row = Add.Panel( "treerow" );
		row.SetClass( "selected", item == selected );
		row.SetClass( "hidden", !item.Enabled );

		rows[item] = row;

		if ( rowIndex < 40 ) row.Style.Set( "transition-delay", FormattableString.Invariant( $"{rowIndex * 0.01f:0.000}s" ) );
		rowIndex++;

		var indent = row.Add.Panel();
		indent.Style.Width = 6 + depth * 12;
		indent.Style.FlexShrink = 0;

		if ( item.Children.Count > 0 )
		{
			var arrow = row.Icon( expanded ? "expand_more" : "chevron_right", "arrow" );
			arrow.AddEventListener( "onclick", e =>
			{
				e.StopPropagation();

				if ( !collapsed.Remove( item.Id ) ) collapsed.Add( item.Id );

				Rebuild();
			} );
		}
		else
		{
			row.Add.Panel( "arrow" );
		}

		row.Icon( IconFor( item ), "kind" );
		row.Add.Label( item.Name, "name" );

		var eye = row.Icon( item.Enabled ? "visibility" : "visibility_off", "eye" );
		eye.AddEventListener( "onclick", e =>
		{
			e.StopPropagation();

			item.Enabled = !item.Enabled;

			// Update this row rather than the whole tree, and take the new state as read so the
			// scene watcher doesn't rebuild on top of us
			row.SetClass( "hidden", !item.Enabled );
			eye.Text = item.Enabled ? "visibility" : "visibility_off";
			builtHash = SceneHash();
		} );

		row.AddEventListener( "onclick", () => Select( item ) );
		row.AddEventListener( "onrightclick", () => OpenMenu( item, row ) );
		row.AddEventListener( "ondoubleclick", () => BeginRename( item, row ) );

		if ( !expanded ) return;

		foreach ( var child in item.Children )
		{
			AddRow( child, depth + 1 );
		}
	}

	/// <summary>
	/// Whether the light theme is on, so menus we open match the window.
	/// </summary>
	public Func<bool> IsLightMode { get; set; }

	void OpenMenu( GameObject item, Panel row )
	{
		Select( item );

		ContextMenu.Open( this, new ContextMenu.Item[]
		{
			new( "Rename", () => BeginRename( item, row ), "edit", "F2" ),
			new( "Duplicate", () => Duplicate( item ), "content_copy", "Ctrl+D" ),
			null,
			new( "Create Empty Child", () => CreateChild( item ), "add" ),
			null,
			new( "Delete", () => Delete( item ), "delete", "Del" ),
		}, IsLightMode?.Invoke() ?? false );
	}

	/// <summary>
	/// Swap the name label for a text box, and put the name back when it's done.
	/// </summary>
	void BeginRename( GameObject item, Panel row )
	{
		if ( !item.IsValid() ) return;
		if ( row.Children.OfType<TextInput>().Any() ) return;

		var label = row.Children.FirstOrDefault( x => x.HasClass( "name" ) );
		if ( label is null ) return;

		label.Style.Display = DisplayMode.None;

		var input = new TextInput( item.Name, null, true );
		input.AddClass( "rename" );

		row.AddChild( input );
		row.SetChildIndex( input, row.GetChildIndex( label ) + 1 );

		input.Focus();

		// Escape puts the name back, rather than emptying the box
		input.OnButton = button =>
		{
			if ( button != "escape" ) return false;

			Rebuild();
			return true;
		};

		input.OnSubmit = () =>
		{
			if ( item.IsValid() && SceneEditorSession.Active is { } session )
			{
				using ( session.UndoScope( "Rename" ).WithGameObjectChanges( item, GameObjectUndoFlags.Properties ).Push() )
				{
					item.Name = input.Value;
				}
			}

			Rebuild();
		};
	}

	static void Duplicate( GameObject item )
	{
		if ( !item.IsValid() ) return;
		if ( SceneEditorSession.Active is not { } session ) return;

		using ( session.UndoScope( "Duplicate" ).Push() )
		{
			var copy = item.Clone();
			copy.Parent = item.Parent;
			copy.Name = item.Name;

			session.Selection.Set( copy );
		}
	}

	static void CreateChild( GameObject item )
	{
		if ( !item.IsValid() ) return;
		if ( SceneEditorSession.Active is not { } session ) return;

		using ( session.Scene.Push() )
		using ( session.UndoScope( "Create Object" ).Push() )
		{
			var child = new GameObject( true, "Object" ) { Parent = item };
			session.Selection.Set( child );
		}
	}

	static void Delete( GameObject item )
	{
		if ( !item.IsValid() ) return;
		if ( SceneEditorSession.Active is not { } session ) return;

		using ( session.UndoScope( $"Delete {item.Name}" ).WithGameObjectDestructions( item ).Push() )
		{
			item.Destroy();
		}

		session.Selection.Clear();
	}

	/// <summary>
	/// Pick an icon from what the object actually has on it.
	/// </summary>
	public static string IconFor( GameObject item )
	{
		if ( !item.IsValid() ) return "help";

		foreach ( var component in item.Components.GetAll() )
		{
			var icon = IconForComponent( component );
			if ( icon is not null ) return icon;
		}

		return item.Children.Count > 0 ? "folder" : "radio_button_unchecked";
	}

	/// <summary>
	/// An icon for a component, from what it is. Null if we've nothing better than the default.
	/// </summary>
	public static string IconForComponent( Component component )
	{
		var name = component?.GetType().Name ?? "";

		if ( name.Contains( "Camera" ) ) return "videocam";
		if ( name.Contains( "Light" ) ) return "lightbulb";
		if ( name.Contains( "Sound" ) || name.Contains( "Audio" ) ) return "volume_up";
		if ( name.Contains( "Particle" ) ) return "auto_awesome";
		if ( name.Contains( "Collider" ) ) return "category";
		if ( name.Contains( "Rigidbody" ) ) return "fitness_center";
		if ( name.Contains( "Terrain" ) ) return "landscape";
		if ( name.Contains( "Renderer" ) || name.Contains( "Model" ) ) return "view_in_ar";
		if ( name.Contains( "Panel" ) || name.Contains( "Screen" ) ) return "web_asset";

		return null;
	}
}
