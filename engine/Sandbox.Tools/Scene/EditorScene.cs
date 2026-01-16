using Sandbox.Engine;
using Sandbox.Modals;
using System;
using System.Text.Json.Nodes;

namespace Editor;

public static class EditorScene
{
	/// <summary>
	/// Should the game start in play mode when hitting play, instead of playing the active scene.
	/// </summary>
	public static bool PlayMode { get; set; } = false;

	public static Gizmo.SceneSettings GizmoSettings { get; private set; } = new Gizmo.SceneSettings();

	public static SelectionSystem Selection => SceneEditorSession.Active?.Selection;

	public static void RestoreState()
	{
		var package = Project.Current.Package;
		if ( ProjectCookie?.Get<Gizmo.SceneSettings>( $"gizmo.settings", null ) is Gizmo.SceneSettings savedSettings )
		{
			GizmoSettings = savedSettings;
		}
	}

	[Shortcut( "editor.new", "CTRL+N" )]
	public static void NewScene()
	{
		var newSession = SceneEditorSession.CreateDefault();
		newSession.MakeActive();
	}

	[Shortcut( "editor.open", "CTRL+O" )]
	public static void Open()
	{
		var fd = new FileDialog( null )
		{
			Title = "Open",
			Directory = Project.Current.GetAssetsPath()
		};
		fd.SetNameFilter( "(*.scene; *.prefab)" );

		if ( !fd.Execute() ) return;

		var asset = AssetSystem.FindByPath( fd.SelectedFile );

		var session = SceneEditorSession.CreateFromPath( asset.RelativePath );

		if ( session is null )
		{
			Log.Error( $"Failed to open {fd.SelectedFile}" );
			return;
		}

		session.MakeActive();
	}

	/// <summary>
	/// Opens the given scene file for editing, if it's not already open.
	/// </summary>
	public static void OpenScene( SceneFile resource )
	{
		if ( SceneEditorSession.Resolve( resource ) is { } session )
		{
			session.MakeActive();
			return;
		}

		if ( string.IsNullOrWhiteSpace( resource.ResourcePath ) )
		{
			// Play mode has a copy of the scene file that shouldn't be edited.
			return;
		}

		session = SceneEditorSession.CreateFromPath( resource.ResourcePath );
		session.MakeActive();
	}

	/// <summary>
	/// Opens the given prefab file for editing, if it's not already open.
	/// </summary>
	public static void OpenPrefab( PrefabFile resource )
	{
		if ( SceneEditorSession.Resolve( resource ) is PrefabEditorSession session )
		{
			session.MakeActive();
			return;
		}

		var prefabScene = PrefabScene.CreateForEditing();
		using ( prefabScene.Push() )
		{
			prefabScene.Load( resource );

			session = new PrefabEditorSession( prefabScene );
			session.MakeActive();
		}
	}

	[Shortcut( "editor.save", "CTRL+S" )]
	public static void SaveSession()
	{
		SceneEditorSession.Active?.Save( false );
	}

	[Shortcut( "editor.save-as", "CTRL+SHIFT+S" )]
	public static void SaveSessionAs()
	{
		SceneEditorSession.Active?.Save( true );
	}

	[Shortcut( "editor.save-all", "CTRL+ALT+SHIFT+S" )]
	public static void SaveAllSessions()
	{
		foreach ( var session in SceneEditorSession.All )
		{
			session.Save( false );
		}
	}

	public static void Discard()
	{
		if ( !SceneEditorSession.Active?.HasUnsavedChanges ?? false )
			return;

		var popup = new PopupDialogWidget( "🔄️" );
		popup.FixedWidth = 500;
		popup.WindowTitle = "Scene";
		popup.MessageLabel.Text = $"Are you sure you want to do this, you'll lose all your progress...";

		popup.ButtonLayout.Spacing = 4;
		popup.ButtonLayout.AddStretchCell();

		popup.ButtonLayout.Add( new Button.Primary( "Yes" )
		{
			Clicked = () =>
			{
				// Load from source
				var scene = SceneEditorSession.Active?.Scene;
				if ( scene is not null )
					scene.Load( scene.Source );

				popup.Destroy();
			}
		} );

		popup.ButtonLayout.Add( new Button( "No, nevermind" )
		{
			Clicked = popup.Destroy
		} );

		popup.SetModal( true, true );
		popup.Hide();
		popup.Show();
	}

	static SceneEditorSession recentlyActivePlayableScene;

	static SceneEditorSession FindPlayableSession()
	{
		// Current scene is good
		if ( SceneEditorSession.Active?.Scene is not PrefabScene )
			return SceneEditorSession.Active;

		// Last viewed scene is good
		if ( SceneEditorSession.All.Contains( recentlyActivePlayableScene ) )
			return recentlyActivePlayableScene;

		// We don't want to play prefab scenes
		var sessions = SceneEditorSession.All.Where( x => x.Scene is not PrefabScene ).ToList();
		var idx = sessions.IndexOf( SceneEditorSession.Active );

		// If the active session is not in the filtered list, we can't play
		if ( idx < 0 )
			return null;

		// Find closest scene (prefer left, then right)
		return sessions.Take( idx ).LastOrDefault() ?? sessions.Skip( idx + 1 ).FirstOrDefault();
	}

	/// <summary>
	/// Toggles play mode.
	/// </summary>
	[Shortcut( "editor.toggle-play", "F5", ShortcutType.Window )]
	public static void TogglePlay()
	{
		EditorShortcuts.ReleaseAll();

		if ( !Game.IsPlaying )
			Play();
		else
			Stop();
	}

	static JsonObject _mixerStore;

	/// <summary>
	/// Store stuff before playing
	/// </summary>
	static void OnPlayStore()
	{
		_mixerStore = Sandbox.Audio.Mixer.Master.Serialize();
	}

	/// <summary>
	/// Restore stuff before playing
	/// </summary>
	static void OnPlayRestore()
	{
		if ( _mixerStore is not null )
		{
			Sandbox.Audio.Mixer.ResetToDefault();
			Sandbox.Audio.Mixer.Master.Deserialize( _mixerStore, TypeLibrary );
		}

	}

	public static void PlayMap( Asset asset )
	{
		Stop();

		if ( asset is null )
		{
			Log.Error( "Cannot play map: map unknown." );
			return;
		}

		LaunchArguments.Map = asset.RelativePath;
		Play( true );
	}

	public static void Play( SceneEditorSession session = null ) => Play( PlayMode, session );

	public static void Play( bool playMode, SceneEditorSession playableSession = null )
	{
		playableSession ??= FindPlayableSession();
		if ( playableSession is null ) return;

		OnPlayStore();

		Game.IsPlaying = true;

		IModalSystem.Current?.CloseAll( true );

		EditorEvent.Run( "scene.startplay" );

		if ( playMode )
		{
			LoadingScreen.IsVisible = true;
			LoadingScreen.Title = "Loading Game..";
			IGameInstanceDll.Current.EditorPlay();
		}
		else
		{
			LoadingScreen.IsVisible = true;
			LoadingScreen.Title = "Loading Scene..";

			if ( Game.ActiveScene is not null && !Game.ActiveScene.IsEditor )
			{
				Game.ActiveScene?.Destroy();
				Game.ActiveScene = null;
			}

			var current = playableSession.Scene.CreateSceneFile();
			var name = playableSession.Scene.Name;

			Game.ActiveScene = new Scene();
			Game.ActiveScene.Name = name;
			Game.ActiveScene.StartLoading();

			var options = new SceneLoadOptions();
			options.SetScene( current );

			Game.ActiveScene.RunEvent<ISceneStartup>( x => x.OnHostPreInitialize( options.GetSceneFile() ) );

			Game.ActiveScene.Load( options );

			Game.ActiveScene.RunEvent<ISceneStartup>( x => x.OnHostInitialize() );
			Game.ActiveScene.RunEvent<ISceneStartup>( x => x.OnClientInitialize() );
		}

		if ( Game.ActiveScene is null )
		{
			Log.Warning( "Tried to play, but there was no scene." );
			return;
		}

		SceneEditorSession.Active.SetPlaying( Game.ActiveScene );

		EditorEvent.Run( "scene.play" );
	}

	public static void Stop()
	{
		Game.IsClosing = true;

		SceneEditorSession.Active.StopPlaying();

		Game.IsPlaying = false;
		Game.IsPaused = false;

		// Immediately stop active recordings so we don't get any black frames
		ScreenRecorder.StopRecording();

		// Let's use a disconnect scope here to prevent network destroy messages being sent. This lets clients
		// already connected to our editor session keep playing. Orphaned actions will take care of any objects
		// owned by us.
		using ( Networking.DisconnectScope() )
		{
			Game.ActiveScene?.Destroy();
			Game.ActiveScene = null;
		}

		Game.IsClosing = false;

		Sound.StopAll( 0.5f );

		SceneEditorTick();

		EditorEvent.Run( "scene.stop" );

		Mouse.Visibility = MouseVisibility.Auto;

		OnPlayRestore();
	}

	/// <summary>
	/// Called once a frame to keep the game camera in sync with the main camera in the editor scene
	/// </summary>
	[EditorEvent.Frame]
	public static void SceneEditorTick()
	{
		if ( SceneEditorSession.Active is null ) return;

		if ( SceneEditorSession.Active.Scene is not PrefabScene )
			recentlyActivePlayableScene = SceneEditorSession.Active;

		SceneEditorSession.ProcessSceneEdits();
	}

	[EditorForAssetType( "scene" )]
	[EditorForAssetType( "prefab" )]
	public static void LoadFromResource( GameResource resource )
	{
		Assert.NotNull( resource, "resource should not be null" );

		var session = SceneEditorSession.CreateFromPath( resource.ResourcePath );
		session.MakeActive();
	}

	internal static void UpdatePrefabInstancesInScene( Scene scene, PrefabFile prefab )
	{
		var changedPath = prefab.ResourcePath;

		using ( scene.Push() )
		{
			// Copy, because this collection can be modified during prefab updating ( e.g. refreshing/deserializing prefab spawns GOs or components)
			var prefabInstancesRequiringUpdate = scene.GetAllObjects( false )
				.Where( x => x.IsPrefabInstanceRoot && x.PrefabInstanceSource == changedPath )
				.Select( x => x.OutermostPrefabInstanceRoot ) // We always need to update the outermostprefab instance
				.ToHashSet();
			foreach ( var obj in prefabInstancesRequiringUpdate )
			{
				if ( obj.IsValid() )
				{
					obj.UpdateFromPrefab();
					scene.Editor.HasUnsavedChanges = true;
				}
			}
		}
	}

	/// <summary>
	/// Update any/all instances of a prefab in any open sessions
	/// </summary>
	public static void UpdatePrefabInstances( PrefabFile prefab )
	{
		ArgumentNullException.ThrowIfNull( prefab );

		var prefabSessions = SceneEditorSession.All
			.OfType<PrefabEditorSession>();

		// We need to update the prefab file itself, so that it has the latest changes
		foreach ( var session in prefabSessions )
		{
			UpdatePrefabInstancesInScene( session.Scene, prefab );
			session.Scene.ToPrefabFile();
		}

		// And then update all prefab instances again
		// This makes sure prefab dependencies are updated as well
		foreach ( var session in SceneEditorSession.All )
		{
			UpdatePrefabInstancesInScene( session.Scene, prefab );
		}
	}

	[Event( "model.reload" )]
	internal static void OnModelReload( Model model )
	{
		ArgumentNullException.ThrowIfNull( model );

		foreach ( var session in SceneEditorSession.All )
		{
			var scene = session.Scene;
			using var scope = scene.Push();

			var components = scene.GetAllComponents<IHasModel>();
			foreach ( var c in components )
			{
				if ( c.Model != model )
					continue;

				c.OnModelReloaded();
			}
		}
	}

	[Shortcut( "editor.cut", "CTRL+X" )]
	public static void Cut()
	{
		var selection = EditorScene.Selection.OfType<GameObject>().ToArray();
		if ( selection.Count() < 1 ) return;

		var session = SceneEditorSession.Resolve( selection.FirstOrDefault() );
		using var scene = session.Scene.Push();

		var options = new GameObject.SerializeOptions();
		var serializedObjects = selection.Select( x => x.Serialize( options ) ).ToArray();

		EditorUtility.Clipboard.Copy( Json.Serialize( serializedObjects ) );

		using ( session.UndoScope( "Cut" ).WithGameObjectDestructions( selection ).Push() )
		{
			session.Selection.Clear();
			// Delete all objects in selection
			foreach ( var go in selection )
			{
				go.Destroy();
			}
		}
	}

	[Shortcut( "editor.select-all", "CTRL+A" )]
	public static void SelectAll()
	{
		using ( SceneEditorSession.Active.UndoScope( "Select All" ).Push() )
		{
			Selection.Clear();
			foreach ( var child in SceneEditorSession.Active.Scene.Children )
			{
				Selection.Add( child );
			}
		}
	}

	[Shortcut( "editor.copy", "CTRL+C" )]
	public static void Copy()
	{
		var options = new GameObject.SerializeOptions();

		var selection = EditorScene.Selection.OfType<GameObject>().ToArray();
		if ( !selection.Any() ) return;

		var serialized = selection.Select( x =>
		{
			var s = x.Serialize( options );
			// When we copy we keep the world transform.
			s["Position"] = JsonValue.Create( x.WorldPosition );
			s["Rotation"] = JsonValue.Create( x.WorldRotation );
			s["Scale"] = JsonValue.Create( x.WorldScale );
			return s;
		} );

		EditorUtility.Clipboard.Copy( Json.Serialize( serialized ) );
	}

	[Shortcut( "editor.paste", "CTRL+V" )]
	public static void Paste()
	{
		var selected = EditorScene.Selection.FirstOrDefault() as GameObject;

		var session = SceneEditorSession.Resolve( selected );
		using var scene = session.Scene.Push();

		// Paste to scene root if nobody is selected
		if ( selected is null )
		{
			selected = SceneEditorSession.Active.Scene;
		}

		if ( selected is Scene )
		{
			PasteAsChild();
			return;
		}

		ExecutableUndoablePaste( selected, false );
	}

	[Shortcut( "editor.paste-as-child", "CTRL+SHIFT+V" )]
	public static void PasteAsChild()
	{
		var selected = Selection.OfType<GameObject>().ToArray();
		var first = selected.FirstOrDefault();

		var session = SceneEditorSession.Resolve( first );
		using var scene = session.Scene.Push();

		// Paste to scene root if nobody is selected
		if ( !first.IsValid() )
		{
			selected = [SceneEditorSession.Active.Scene];
		}

		ExecutableUndoablePaste( selected, true );
	}

	private static void ExecutableUndoablePaste( GameObject target, bool asChild )
	{
		ExecutableUndoablePaste( [target], asChild );
	}

	private static void ExecutableUndoablePaste( IEnumerable<GameObject> targets, bool asChild )
	{
		var text = EditorUtility.Clipboard.Paste();

		// Deserialize can fail if the clipboards contents area ambigous / partally invalid
		try
		{
			if ( Json.Deserialize<IEnumerable<JsonObject>>( text ) is IEnumerable<JsonObject> serializedObjects )
			{
				var objCount = serializedObjects.Count();
				if ( objCount == 0 ) return;

				var session = SceneEditorSession.Resolve( targets.FirstOrDefault() );
				using var scene = session.Scene.Push();
				using ( session.UndoScope( $"Paste {objCount} Objects" ).WithGameObjectCreations().Push() )
				{
					EditorScene.Selection.Clear();

					foreach ( var target in targets )
					{
						foreach ( var jso in serializedObjects )
						{
							var go = SceneEditorSession.Active.Scene.CreateObject();
							// avoids some warnings
							SceneUtility.MakeIdGuidsUnique( jso );
							go.Deserialize( jso );

							if ( target.IsValid() )
							{
								if ( asChild )
								{
									go.SetParent( target );
								}
								else
								{
									target.AddSibling( go, false );
								}
							}

							go.MakeNameUnique();

							EditorScene.Selection.Add( go );
						}
					}
				}
			}
		}
		catch
		{
			Log.Warning( "Failed to paste, invalid JSON." );
		}
	}

	/// <summary>
	/// Capture a high resolution screenshot using the active scene camera.
	/// </summary>
	[ConCmd( "screenshot_highres", Help = "Take a high resolution screenshot you specify the width and height" )]
	public static void TakeHighResScreenshot( int width, int height )
	{
		ScreenshotService.TakeHighResScreenshot( SceneEditorSession.Active.Scene, width, height );
	}
}
