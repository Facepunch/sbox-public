namespace Editor;

[EditorTool( "tools.eye-dropper-tool", Hidden = true )]
public class EyeDropperTool : EditorTool
{
	static string LastTool;
	internal static SerializedProperty TargetProperty = null;
	internal static Action OnBackToLastTool;

	static bool MenuOpen;

	string LastSelection;


	public override void OnEnabled()
	{
		base.OnEnabled();

		SubscribeEvents();

		AllowGameObjectSelection = true;
		LastSelection = Manager.CurrentSession.SerializeSelection();

		SceneOverlay.Parent.Cursor = CursorShape.BitmapCursor;
		SceneOverlay.Parent.PixmapCursor = Pixmap.FromFile( "cursors/eyedropper_centered.png" );
	}

	public override void OnDisabled()
	{
		base.OnDisabled();

		UnsubscribeEvents();
		SceneOverlay.Parent.Cursor = CursorShape.Arrow;
	}

	void SubscribeEvents()
	{
		Selection.OnItemAdded += OnItemAdded;
		if ( MainAssetBrowser.Instance?.IsValid ?? false )
		{
			MainAssetBrowser.Instance.Local.OnAssetHighlight += OnItemAdded;
			MainAssetBrowser.Instance.Local.OnHighlight += OnItemAdded;
			Application.OnWidgetClicked += OnWidgetPressed;
		}
	}

	void UnsubscribeEvents()
	{
		Selection.OnItemAdded -= OnItemAdded;
		if ( MainAssetBrowser.Instance?.IsValid ?? false )
		{
			MainAssetBrowser.Instance.Local.OnAssetHighlight -= OnItemAdded;
			MainAssetBrowser.Instance.Local.OnHighlight -= OnItemAdded;
			Application.OnWidgetClicked -= OnWidgetPressed;
		}
	}

	void OnItemAdded( object obj )
	{
		Select( obj );
		UnsubscribeEvents();
		Manager.CurrentSession.DeserializeSelection( LastSelection );
	}

	void OnWidgetPressed( Widget widget, MouseEvent mouseEvent )
	{
		// Allow clicking on a Component Header to select the Component
		if ( widget is ComponentSheetHeader header )
		{
			var component = header.GetComponent();
			if ( component.IsValid() )
			{
				Select( component );
			}

			mouseEvent.Accepted = true;
			return;
		}

		// Allow clicking on a GameObjectControlWidget when it has a Prefab
		if ( widget is GameObjectControlWidget controlWidget )
		{
			var val = controlWidget.SerializedProperty.GetValue<GameObject>();
			if ( val is PrefabScene prefabScene )
			{
				var resource = prefabScene?.Source ?? null;
				var asset = resource != null ? AssetSystem.FindByPath( resource.ResourcePath ) : null;

				if ( asset != null )
				{
					Select( asset );
					mouseEvent.Accepted = true;
				}
			}
		}
	}

	internal static void Select( object obj )
	{
		if ( obj is GameObject gameObject )
		{
			if ( ProcessObject( gameObject ) )
				return;
		}
		else if ( obj is Component component )
		{
			ProcessComponent( component );
		}
		else if ( obj is Asset asset )
		{
			if ( TargetProperty is not null && asset.TryLoadResource( out PrefabFile prefabFile ) && TargetProperty.PropertyType == typeof( GameObject ) )
			{
				ProcessObject( SceneUtility.GetPrefabScene( prefabFile ) );
			}
		}
		BackToLastTool();
	}

	public override void OnUpdate()
	{
		base.OnUpdate();

		if ( MenuOpen )
			return;

		if ( TargetProperty is null || !TargetProperty.Parent.Contains( TargetProperty ) )
		{
			BackToLastTool();
			return;
		}


		if ( Gizmo.WasLeftMouseReleased )
		{
			var tr = MeshTrace.Run();
			GameObject hitObject = null;
			if ( tr.Hit )
			{
				hitObject = tr.GameObject;
			}
			ProcessAfterSelection( hitObject );
			return;
		}

		// Allow clicking on the header to select the component when using Eye Dropper
		//if ( Widget.CurrentlyPressedWidget is ComponentSheetHeader )
		//{
		//	var component = TargetObject.Targets.FirstOrDefault() as Component;
		//	if ( component.IsValid() )
		//	{
		//		EyeDropperTool.Select( component );
		//	}
		//	return;
		//}
	}

	/// <summary>
	/// Assign from a picked GameObject. Returns true if the choice was handed off to a popup menu,
	/// which does its own teardown, so the caller shouldn't tear the tool down again.
	/// </summary>
	static bool ProcessObject( GameObject obj )
	{
		if ( TargetProperty is null ) return false;
		if ( TargetProperty.PropertyType == typeof( GameObject ) )
		{
			// GameObject Target
			TargetProperty.SetValue( obj );
			return false;
		}

		var candidates = obj.Components
			.GetAll( TargetProperty.PropertyType, FindMode.EnabledInSelfAndDescendants )
			.ToList();

		if ( candidates.Count == 0 )
		{
			candidates = obj.Components
				.GetAll( TargetProperty.PropertyType, FindMode.DisabledInSelfAndDescendants )
				.ToList();
		}

		if ( candidates.Count > 1 )
		{
			OpenComponentMenu( obj, candidates );
			return true;
		}

		ProcessComponent( candidates.FirstOrDefault() );
		return false;
	}

	/// <summary>
	/// Show a menu of every matching component on the picked object. Clicking one picks it.
	/// </summary>
	static void OpenComponentMenu( GameObject obj, List<Component> components )
	{
		MenuOpen = true;

		var menu = new Menu();
		menu.AddHeading( obj.Name );

		Component picked = null;

		foreach ( var component in components )
		{
			var type = EditorTypeLibrary.GetType( component.GetType() );
			var name = type?.Title ?? component.GetType().Name;

			if ( component.GameObject != obj )
			{
				name = $"{name} ({component.GameObject?.Name})";
			}

			menu.AddOption( name, type?.Icon, () => picked = component );
		}

		menu.OpenAtCursor( true );

		MenuOpen = false;

		if ( picked.IsValid() )
		{
			ProcessComponent( picked );
		}

		BackToLastTool();
	}

	static void ProcessComponent( Component comp )
	{
		if ( TargetProperty is null ) return;
		TargetProperty.SetValue( comp );
	}

	async void ProcessAfterSelection( GameObject obj )
	{
		await Task.Delay( 100 );

		if ( obj.IsValid() )
		{
			if ( ProcessObject( obj ) )
				return;
		}

		BackToLastTool();
	}

	public static void SetTargetProperty( SerializedProperty property )
	{
		if ( EditorToolManager.CurrentModeName == nameof( EyeDropperTool ) )
		{
			BackToLastTool();
			return;
		}

		LastTool = EditorToolManager.CurrentModeName;
		EditorToolManager.SetTool( nameof( EyeDropperTool ) );
		TargetProperty = property;
	}

	internal static void BackToLastTool()
	{
		if ( string.IsNullOrEmpty( LastTool ) )
			return;

		EditorToolManager.SetTool( LastTool );
		LastTool = null;
		TargetProperty = null;
		MenuOpen = false;

		OnBackToLastTool?.Invoke();
	}
}
