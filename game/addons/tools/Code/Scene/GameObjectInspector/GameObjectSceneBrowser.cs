namespace Editor;

public class GameObjectSceneBrowser : Widget
{
	/// <summary>
	/// This action is invoked whenever a GameObject has been selected
	/// </summary>
	public Action<GameObject> OnGameObjectSelect { get; set; }

	/// <summary>
	/// This action is invoked whenever a Component has been selected
	/// </summary>
	public Action<Component> OnComponentSelect { get; set; }

	/// <summary>
	/// If specified, this button will become enabled once a valid selection has been made
	/// </summary>
	public Button ConfirmButton { get; set; }

	/// <summary>
	/// This action is invoked when the selection is confirmed (double-click or button)
	/// </summary>
	public Action OnConfirm { get; set; }

	/// <summary>
	/// Component type to search for (null if searching for GameObjects)
	/// </summary>
	public Type ComponentType { get; set; }

	HashSet<SceneGameObjectNode> ObjectNodes = new();
	HashSet<SceneComponentNode> ComponentNodes = new();
	SceneLocations Locations;
	TreeView SceneTree;

	public GameObjectSceneBrowser( Type componentType = null ) : base( null )
	{
		ComponentType = componentType;
		WindowTitle = "Scene Browser";
		SetWindowIcon( "image" );

		Layout = Layout.Column();
		SceneTree = new TreeView( this );
		SceneTree.MultiSelect = false;
		SceneTree.OnSelectionChanged = ( objs ) =>
		{
			var firstObj = objs.FirstOrDefault();
			if ( firstObj is SceneGameObjectNode gameObjNode && ComponentType is null )
			{
				OnGameObjectSelect?.Invoke( gameObjNode.GameObject );
				if ( ConfirmButton is not null )
					ConfirmButton.Enabled = true;
			}
			if ( firstObj is SceneComponentNode componentNode )
			{
				OnComponentSelect?.Invoke( componentNode.Component );
				if ( ConfirmButton is not null )
					ConfirmButton.Enabled = true;
			}
		};
		SceneTree.ItemActivated = ( obj ) =>
		{
			if ( obj is SceneGameObjectNode && ComponentType is null )
			{
				OnConfirm?.Invoke();
			}
			if ( obj is SceneComponentNode )
			{
				OnConfirm?.Invoke();
			}
		};
		Locations = new SceneLocations( this );

		var splitter = Layout.Add( new Splitter( this ) );
		splitter.IsHorizontal = true;
		splitter.AddWidget( Locations );
		splitter.SetStretch( 0, 1 );
		splitter.AddWidget( SceneTree );
		splitter.SetStretch( 1, 5 );

		Locations.OnSceneSelected += SelectScene;
	}

	public void SelectScene( Scene scene )
	{
		SceneTree.Clear();

		var header = new TreeNode.SmallHeader( "image", scene.Name );
		SceneTree.AddItem( header );

		foreach ( var gameObj in scene.Children )
		{
			if ( gameObj.Flags.HasFlag( GameObjectFlags.EditorOnly ) || gameObj.Flags.HasFlag( GameObjectFlags.Hidden ) )
				continue;
			var treeNode = AddGameObject( gameObj, header );
		}

		SceneTree.Open( header, ComponentType is not null );
	}

	public void SelectGameObject( GameObject gameObject )
	{
		var gameObjectNode = ObjectNodes.FirstOrDefault( x => (x is SceneGameObjectNode goNode) && goNode.GameObject == gameObject );
		if ( gameObjectNode is not null )
		{
			SceneTree.SelectItem( gameObjectNode );
		}
	}

	public void SelectComponent( Component component )
	{
		var componentNode = ComponentNodes.FirstOrDefault( x => x.Component == component );
		if ( componentNode is not null )
		{
			SceneTree.SelectItem( componentNode );
		}
	}

	TreeNode AddGameObject( GameObject gameObject, TreeNode parent )
	{
		var node = new SceneGameObjectNode( gameObject, ComponentType );
		ObjectNodes.Add( node );
		parent.AddItem( node );
		AddComponents( gameObject, node );
		foreach ( var child in gameObject.Children )
		{
			if ( child.Flags.HasFlag( GameObjectFlags.EditorOnly ) || child.Flags.HasFlag( GameObjectFlags.Hidden ) )
				continue;
			AddGameObject( child, node );
		}
		return node;
	}

	void AddComponents( GameObject gameObject, TreeNode parent )
	{
		if ( ComponentType is null )
			return;

		foreach ( var component in gameObject.Components.GetAll() )
		{
			if ( !component.GetType().IsAssignableTo( ComponentType ) )
				continue;

			var node = new SceneComponentNode( component );
			ComponentNodes.Add( node );
			parent.AddItem( node );
		}
	}
}

class SceneLocations : TreeView
{
	public Action<Scene> OnSceneSelected;

	public SceneLocations( GameObjectSceneBrowser parent ) : base( parent )
	{
		MinimumSize = 200;
		ItemSelected = OnItemClicked;

		var header = new TreeNode.Header( "wallpaper", "Active Scenes" );
		AddItem( header );
		Open( header );
		var activeId = SceneEditorSession.Active.Scene.Id;
		foreach ( var editorSession in SceneEditorSession.All )
		{
			var node = new SceneLocationNode( editorSession.Scene );
			header.AddItem( node );
			if ( editorSession.Scene.Id == activeId )
			{
				// Select the active one first by default
				parent.SelectScene( editorSession.Scene );
				SelectItem( node );
			}
		}
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect );

		base.OnPaint();
	}

	protected void OnItemClicked( object value )
	{
		if ( value is SceneLocationNode locationNode )
		{
			OnSceneSelected?.Invoke( locationNode.Scene );
		}
	}
}

class SceneLocationNode : TreeNode
{
	public Scene Scene;

	public SceneLocationNode( Scene scene ) : base()
	{
		Scene = scene;
	}

	public override void OnPaint( VirtualWidget item )
	{
		PaintSelection( item );

		var rect = item.Rect;

		Paint.SetPen( Theme.Text );
		Paint.SetDefaultFont();
		var nameRect = Paint.DrawText( rect.Shrink( 24, 0 ), Scene.Name, TextFlag.LeftCenter );

		Paint.SetPen( Theme.TextLight );
		Paint.DrawIcon( rect, "image", 16, TextFlag.LeftCenter );
	}
}

class SceneGameObjectNode : TreeNode
{
	public GameObject GameObject;
	float Alpha = 1f;

	public SceneGameObjectNode( GameObject gameObject, Type componentType ) : base()
	{
		GameObject = gameObject;

		if ( componentType is not null )
		{
			Alpha = 0.5f;
		}
	}

	public override void OnPaint( VirtualWidget item )
	{
		PaintSelection( item );

		var rect = item.Rect;

		Paint.SetPen( Theme.Text.WithAlpha( Alpha ) );
		Paint.SetDefaultFont();
		var nameRect = Paint.DrawText( rect.Shrink( 24, 0 ), GameObject.Name, TextFlag.LeftCenter );

		Paint.SetPen( Theme.TextLight.WithAlpha( Alpha ) );
		Paint.DrawIcon( rect, "layers", 16, TextFlag.LeftCenter );
	}
}

class SceneComponentNode : TreeNode
{
	public Component Component;
	string DisplayName;
	string Icon = "category";
	Color Color = Theme.Blue;

	public SceneComponentNode( Component component ) : base()
	{
		Component = component;
		var type = component.GetType();
		DisplayName = type.Name.ToTitleCase();
		var title = TypeLibrary.GetAttribute<TitleAttribute>( type );
		if ( title is not null )
		{
			DisplayName = title.Value;
		}
		var icon = TypeLibrary.GetAttribute<IconAttribute>( type );
		if ( icon is not null )
		{
			Icon = icon.Value;
		}
		var tint = TypeLibrary.GetAttribute<TintAttribute>( type );
		if ( tint is not null )
		{
			Color = Theme.GetTint( tint.Tint );
		}
	}

	public override void OnPaint( VirtualWidget item )
	{
		PaintSelection( item );

		var rect = item.Rect;

		Paint.SetPen( Theme.Text );
		Paint.SetDefaultFont();
		var nameRect = Paint.DrawText( rect.Shrink( 24, 0 ), DisplayName, TextFlag.LeftCenter );

		Paint.SetPen( Color );
		Paint.DrawIcon( rect, Icon, 16, TextFlag.LeftCenter );
	}
}
