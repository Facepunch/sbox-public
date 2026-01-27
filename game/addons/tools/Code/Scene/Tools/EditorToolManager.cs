namespace Editor;

public class EditorToolManager
{
	/// <summary>
	/// Holds the name of the current tool. We store this as a name
	/// because it's easy to store and restore. This gives us a single
	/// point to change, or read the current tool.
	/// The tool won't change until the next frame update.
	/// </summary>
	public static string CurrentModeName { get; set; } = "ObjectEditorTool";
	public static string LastModeName { get; set; } = CurrentModeName;
	private static string CurrentSubModeName { get; set; }

	public EditorTool CurrentTool { get; private set; }

	public bool IsCurrentViewFocused { get; private set; }

	public EditorTool CurrentSubTool => CurrentTool?.CurrentTool;
	public List<EditorTool> ComponentTools { get; private set; } = new List<EditorTool>();

	private SceneEditorSession _session;
	public SceneEditorSession CurrentSession
	{
		get => _session ?? SceneEditorSession.Active;
		set => _session = value;
	}

	public EditorToolManager()
	{

	}

	internal void Frame( CameraComponent camera, SceneEditorSession session, bool isFocused )
	{
		CurrentSession = session;
		UpdateTool( CurrentModeName );
		UpdateSubTool( CurrentSubModeName );
		UpdateSelection();

		IsCurrentViewFocused = isFocused;

		if ( CurrentTool is not null )
		{
			CurrentTool.Frame( camera );
		}

		foreach ( var a in ComponentTools )
		{
			a.Frame( camera );
		}

		if ( CurrentTool is not null )
		{
			Gizmo.Settings.Selection = CurrentTool.AllowGameObjectSelection;
		}
		else if ( ComponentTools.Any() )
		{
			Gizmo.Settings.Selection = ComponentTools.All( x => x.AllowGameObjectSelection );
		}
		else
		{
			Gizmo.Settings.Selection = true;
		}

	}

	private string currentMode;
	private string currentSubMode;

	internal void UpdateTool( string editMode )
	{
		if ( currentMode == editMode )
			return;

		currentMode = editMode;

		// So that we update the sub tool immediately after updating the parent tool
		currentSubMode = null;

		CurrentTool?.Dispose();
		CurrentTool = null;

		var bestType = EditorTypeLibrary.GetTypesWithAttribute<EditorToolAttribute>()
			.Where( x => x.Type.IsNamed( currentMode ) )
			.FirstOrDefault();

		if ( bestType.Type is null )
		{
			return;
		}

		CurrentTool = bestType.Type.Create<EditorTool>();
		CurrentTool.InitializeInternal( this );
	}

	private void UpdateSubTool( string editMode )
	{
		if ( currentSubMode == editMode ) return;
		if ( CurrentTool is not { } parentTool ) return;

		currentSubMode = editMode;

		parentTool.CurrentTool = parentTool.Tools.FirstOrDefault( x => editMode == x.GetType().Name )
			?? parentTool.CurrentTool
			?? parentTool.Tools.FirstOrDefault();
	}

	int previousHash;
	int SelectionHash => CurrentSession?.Selection?.GetHashCode() ?? 0;
	List<EditorTool> deleteList = new();

	/// <summary>
	/// Check if the selection has changed. If it has, recreate component tools.
	/// </summary>
	private void UpdateSelection()
	{
		var gameObjects = CurrentSession.Selection.OfType<GameObject>().ToArray();

		// Add component count to hash if we have single object selected, to update when components are added/removed
		var selectionHash = SelectionHash;
		if ( gameObjects.Length == 1 )
		{
			selectionHash = 0;
			var components = gameObjects[0].Components.GetAll();

			foreach ( var component in components )
			{
				if ( !component.IsValid() )
					continue;

				selectionHash = HashCode.Combine( selectionHash, component.GetHashCode() );
			}
		}

		if ( previousHash == selectionHash )
			return;

		previousHash = selectionHash;

		CurrentTool?.OnSelectionChanged();

		deleteList.Clear();
		deleteList.AddRange( ComponentTools.Where( t => !t.ShouldKeepActive() ) );

		if ( gameObjects.Length == 1 )
		{
			var allComponents = gameObjects[0].Components.GetAll().DistinctBy( x => x.GetType() ).ToArray();

			foreach ( var a in allComponents )
			{
				CreateToolFor( a.GetType() );
			}
		}

		foreach ( var deleteMe in deleteList )
		{
			ComponentTools.Remove( deleteMe );
			deleteMe?.Dispose();
		}

		foreach ( var a in ComponentTools )
		{
			a.OnSelectionChanged();
		}
	}

	private void CreateToolFor( Type t )
	{
		var targetType = typeof( EditorTool<> ).MakeGenericType( t );

		var editors = EditorTypeLibrary.GetTypes( targetType ).ToArray();
		foreach ( var editor in editors )
		{
			// Already exists
			if ( ComponentTools.Any( x => x.GetType() == editor.TargetType ) )
			{
				deleteList.RemoveAll( x => x.GetType() == editor.TargetType ); // don't delete these
				return;
			}

			var et = editor.Create<EditorTool>();
			ComponentTools.Add( et );
			et.InitializeInternal( this );
			return;
		}


		if ( t.BaseType != typeof( Component ) )
		{
			CreateToolFor( t.BaseType );
		}
	}

	public void DisposeAll()
	{
		previousHash = -1;
		foreach ( var tool in ComponentTools )
			tool?.Dispose();
		ComponentTools.Clear();
	}

	/// <summary>
	/// Switches to the named tool type next editor frame.
	/// </summary>
	/// <param name="name">Type name of the tool.</param>
	public static void SetTool( string name )
	{
		LastModeName = CurrentModeName;
		CurrentModeName = name;
	}

	/// <summary>
	/// Switches to the named sub-tool type next editor frame.
	/// </summary>
	/// <param name="name">Type name of the sub-tool.</param>
	public static void SetSubTool( string name )
	{
		CurrentSubModeName = name;
	}
}

public class EditorToolAttribute : System.Attribute
{
	internal string Shortcut { get; }
	public bool Hidden { get; set; }
	public EditorToolAttribute( string shortcut = "" )
	{
		Shortcut = shortcut;
	}
}
