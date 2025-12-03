using Facepunch.ActionGraphs;
using Sandbox.ActionGraphs;
using System;

namespace Editor;

/// <summary>
/// A SceneEditorSession holds a Scene that is open in the editor.
/// It creates a widget, has a selection and undo system.
/// </summary>
public partial class SceneEditorSession : Scene.ISceneEditorSession
{
	/// <summary>
	/// All open editor sessions
	/// </summary>
	public static List<SceneEditorSession> All { get; } = new();

	/// <summary>
	/// The editor session that is currently active
	/// </summary>
	public static SceneEditorSession Active
	{
		get;
		private set
		{
			if ( field == value ) return;

			field = value;
			field?.UpdateEditorTitle();
		}
	}

	// scenes that have been edited, but waiting for a set interval to react
	// just to debounce changes.
	private static HashSet<SceneEditorSession> editedScenes = new();

	/// <summary>
	/// Returns true if this session is editing a prefab
	/// </summary>
	public bool IsPrefabSession => this is PrefabEditorSession;

	/// <summary>
	/// The scene for this session
	/// </summary>
	public Scene Scene { get; private set; }

	internal Widget SceneDock { get; set; }

	protected SceneEditorSession( Scene scene )
	{
		ArgumentNullException.ThrowIfNull( scene );

		Scene = scene;
		Scene.Editor = this;

		All.Add( this );

		InitUndo();
		timeSinceSavedState = 0;

		if ( this is not GameEditorSession )
		{
			// create dock - but not for game sessions, those are built into the parent session widget
			CreateSceneDock();
		}

		EditorEvent.Register( this );
	}

	/// <summary>
	/// Create the tabbed dock widget that holds the scene view
	/// </summary>
	void CreateSceneDock()
	{
		SceneDock = EditorTypeLibrary.Create<Widget>( "SceneDock", new object[] { this } );
		SceneDock.Name = $"SceneDock:{(Scene.Source?.ResourcePath ?? "untitled")}";

		SceneDock.Parent = EditorWindow;
		SceneDock.Visible = true;

		UpdateEditorTitle();

		Dock();
	}

	internal static void OnEditorWindowRestoreLayout()
	{
		// When we restore a layout it hides all windows and restores a default layout
		// Our currently open scenes are going to be in limbo with no area
		// So we need to show and dock them

		// Restoring will open a blank SceneDock as an area for the others to dock on
		var dummy = All.Where( x => EditorWindow.DockManager.IsDockOpen( x.SceneDock ) ).FirstOrDefault();

		foreach ( var entry in All )
		{
			if ( EditorWindow.DockManager.IsDockOpen( entry.SceneDock ) )
				continue;

			entry.Dock();
		}

		// Remove our dummy dock, unless it's the only one open somehow
		if ( All.Count > 1 )
			dummy.Destroy();
	}

	void Dock()
	{
		// Don't try to dock if we're being made by the DockManager (it will dock us after)
		if ( EditorWindow.DockManager._creatingDock )
			return;

		// Dock inside the same area as other scenes (must be open)
		var siblingDock = All.Where( x => x != this && EditorWindow.DockManager.IsDockOpen( x.SceneDock ) ).FirstOrDefault();
		if ( siblingDock is not null )
		{
			EditorWindow.DockManager.AddDock( siblingDock.SceneDock, SceneDock, DockArea.Inside );
			return;
		}

		// It should be impossible to have no scenes open, fail safe
		EditorWindow.DockManager.AddDock( null, SceneDock, DockArea.LastUsed );
	}

	bool _destroyed;

	public virtual void Destroy()
	{
		if ( _destroyed )
			return;

		_destroyed = true;

		// If this is the active scene
		// switch away to a sibling
		if ( this == Active )
		{
			Active = null;

			var index = All.IndexOf( this );
			if ( index >= 0 && All.Count > 1 )
			{
				if ( index > 0 ) index--;
				else index++;

				Active = All[index];
			}
		}

		All.Remove( this );
		EditorEvent.Unregister( this );

		Scene?.Destroy();
		Scene = null;

		GameSession?.Destroy();
		GameSession = null;

		SceneDock?.Destroy();
		SceneDock = default;
	}

	/// <summary>
	/// Makes this scene active and brings it to the front
	/// </summary>
	public void MakeActive( bool bringToFront = true )
	{
		Active = this;

		if ( bringToFront && EditorWindow is not null )
		{
			BringToFront();
		}
	}

	/// <summary>
	/// Bring this scene tab to the front
	/// </summary>
	public void BringToFront()
	{
		if ( EditorWindow.DockManager.IsDockOpen( SceneDock, false ) )
		{
			EditorWindow.DockManager.RaiseDock( SceneDock );
		}

		UpdateEditorTitle();
	}

	RealTimeSince timeSinceSavedState;

	public void Tick()
	{
		//
		// If this is an editor scene, tick it to flush deleted objects etc
		//
		Scene.ProcessDeletes();

		// Save camera state to disk
		if ( timeSinceSavedState > 1.0f )
		{
			timeSinceSavedState = 0;
			EditorEvent.Run( "scene.session.save" );
		}
	}

	internal void UpdateEditorTitle()
	{
		if ( !SceneDock.IsValid() )
			return;

		var name = Scene.Name.ToTitleCase().Trim();
		if ( Scene.Editor?.HasUnsavedChanges ?? false ) name += "*";

		EditorWindow?.UpdateEditorTitle( name );

		if ( SceneDock is not null )
		{
			SceneDock.Name = $"SceneDock:{(Scene.Source?.ResourcePath ?? "untitled")}";

			if ( IsPrefabSession )
			{
				SceneDock.SetWindowIcon( "home_repair_service" );
				SceneDock.WindowTitle = $"Prefab: {name}";
			}
			else
			{
				SceneDock.SetWindowIcon( "grid_4x4" );
				SceneDock.WindowTitle = name;
			}
		}
	}

	protected virtual void OnEdited()
	{

	}

	static RealTimeSince timeSinceLastUpdatePrefabs;
	internal static void ProcessSceneEdits()
	{
		if ( timeSinceLastUpdatePrefabs < 0.1 ) return;
		timeSinceLastUpdatePrefabs = 0;

		foreach ( var session in editedScenes.ToArray() )
		{
			session.OnEdited();
		}

		editedScenes.Clear();
	}

	/// <summary>
	/// Pushes the active scene to the current scope
	/// </summary>
	public static IDisposable Scope()
	{
		return Active.Scene.Push();
	}

	[Obsolete]
	void Scene.ISceneEditorSession.AddSelectionUndo()
	{
		PushUndoSelection();
	}

	public Action<BBox> OnFrameTo { get; set; }

	/// <summary>
	/// Zoom the scene to view this bbox
	/// </summary>
	public void FrameTo( in BBox box )
	{
		BringToFront();
		OnFrameTo?.Invoke( box );
	}

	bool unsavedChanges;
	public bool HasUnsavedChanges
	{
		get => unsavedChanges;
		set
		{
			editedScenes.Add( this );

			if ( unsavedChanges == value )
				return;

			unsavedChanges = value;
			UpdateEditorTitle();
		}
	}

	public void Reload()
	{
		if ( Scene.Source is null )
			return;

		InitUndo();
		Scene.Load( Scene.Source );

		Selection.Clear();
	}

	public void Save( bool saveAs )
	{
		bool isPrefab = Scene is PrefabScene;
		var saveLocation = string.Empty;

		if ( Scene.Source is not null && AssetSystem.FindByPath( Scene.Source.ResourcePath ) is Asset sourceAsset )
		{
			saveLocation = sourceAsset.AbsolutePath;
		}
		else
		{
			saveAs = true;
		}

		string extension = isPrefab ? "prefab" : "scene";
		string fileType = isPrefab ? "Prefab" : "Scene";

		if ( saveAs )
		{
			if ( string.IsNullOrEmpty( saveLocation ) )
			{
				saveLocation = System.IO.Path.Combine(
					System.IO.Path.GetDirectoryName( ProjectCookie.GetString( $"LastSaveLocation.{extension}", Project.Current.GetAssetsPath() ) ),
					$"untitled.{extension}" );
			}

			saveLocation = EditorUtility.SaveFileDialog( $"Save {fileType} As..", extension, saveLocation );

			if ( saveLocation is null )
				return;

			ProjectCookie.SetString( $"LastSaveLocation.{extension}", System.IO.Path.GetDirectoryName( saveLocation ) );
		}

		EditorEvent.Run( "scene.beforesave", SceneEditorSession.Active.Scene );

		if ( Scene is PrefabScene prefabScene )
		{
			var prefabFile = prefabScene.ToPrefabFile();
			var asset = AssetSystem.CreateResource( "prefab", saveLocation );
			asset.SaveToDisk( prefabFile );

			// Update this scene's path
			Scene.Source = prefabFile;
			Scene.Name = System.IO.Path.GetFileNameWithoutExtension( saveLocation );
		}
		else
		{
			var sceneFile = Scene.CreateSceneFile();
			var asset = AssetSystem.CreateResource( "scene", saveLocation );
			asset.SaveToDisk( sceneFile );

			// Update this scene's path
			Scene.Source = sceneFile;
			Scene.Name = System.IO.Path.GetFileNameWithoutExtension( saveLocation );
		}

		HasUnsavedChanges = false;
		EditorEvent.Run( "scene.saved", SceneEditorSession.Active.Scene );

		UpdateEditorTitle();
	}

	/// <summary>
	/// Resolve a scene to an editor session
	/// </summary>
	public static SceneEditorSession Resolve( Scene scene )
	{
		return All.FirstOrDefault( x => x.Scene == scene );
	}

	/// <summary>
	/// Resolve a scene file to an editor session.
	/// </summary>
	public static SceneEditorSession Resolve( SceneFile sceneFile )
	{
		return All.FirstOrDefault( x => x is not null && !x.IsPrefabSession
			&& string.Equals( sceneFile.ResourcePath, x.Scene.Source?.ResourcePath, StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>
	/// Resolve a prefab file to an editor session.
	/// </summary>
	public static SceneEditorSession Resolve( PrefabFile prefabFile )
	{
		return All.FirstOrDefault( x => x is not null && x.IsPrefabSession
			&& string.Equals( prefabFile.ResourcePath, x.Scene.Source?.ResourcePath, StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>
	/// Resolve an action graph source location to an editor session.
	/// </summary>
	public static SceneEditorSession Resolve( ISourceLocation sourceLocation )
	{
		return sourceLocation switch
		{
			GameResourceSourceLocation { Resource: SceneFile sceneFile } => Resolve( sceneFile ),
			GameResourceSourceLocation { Resource: PrefabFile prefabFile } => Resolve( prefabFile ),
			_ => null
		};
	}

	[Obsolete]
	public void RecordChange( SerializedProperty property )
	{
	}

	/// <summary>
	/// Make a new SceneEditorSession with a default scene
	/// </summary>
	public static SceneEditorSession CreateDefault()
	{
		var scene = Scene.CreateEditorScene();
		using var _ = scene.Push();

		scene.Name = "Untitled Scene";

		{
			var go = scene.CreateObject();
			go.Name = "Main Camera";
			go.LocalTransform = new Transform( Vector3.Up * 100 + Vector3.Backward * 300 );
			go.Components.Create<CameraComponent>();
		}

		{
			var go = scene.CreateObject();
			go.Name = "Directional Light";
			go.LocalTransform = new Transform( Vector3.Up * 200, Rotation.From( 80, 45, 0 ) );
			go.Components.Create<DirectionalLight>();
		}

		return new SceneEditorSession( scene );
	}

	/// <summary>
	/// Opens an editor session from an existing scene or prefab
	/// </summary>
	public static SceneEditorSession CreateFromPath( string path )
	{
		var resource = ResourceLibrary.Get<Resource>( path );

		if ( resource is SceneFile sceneFile )
		{
			if ( SceneEditorSession.Resolve( sceneFile ) is SceneEditorSession existingSession )
			{
				existingSession.MakeActive();
				return existingSession;
			}

			var openingScene = Scene.CreateEditorScene();
			using var _ = openingScene.Push();

			openingScene.Name = sceneFile.ResourceName.ToTitleCase();
			openingScene.Load( sceneFile );

			var session = new SceneEditorSession( openingScene );
			return session;
		}

		if ( resource is PrefabFile prefabFile )
		{
			if ( PrefabEditorSession.Resolve( prefabFile ) is PrefabEditorSession existingSession )
			{
				existingSession.MakeActive();
				return existingSession;
			}

			var openingScene = PrefabScene.CreateForEditing();
			using var _ = openingScene.Push();

			openingScene.Name = prefabFile.ResourceName.ToTitleCase();
			openingScene.Load( prefabFile );

			var session = new PrefabEditorSession( openingScene );
			return session;
		}

		return null;
	}

	public IEnumerable<object> GetSelection()
	{
		foreach ( var obj in Selection )
		{
			yield return obj;
		}
	}
}
