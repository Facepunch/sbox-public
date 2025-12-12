using Editor.MapEditor;

namespace Editor;

public static class SceneEditorMenus
{
	[Menu( "Editor", "Scene/Duplicate" )]
	[Shortcut( "editor.duplicate", "CTRL+D" )]
	public static void Duplicate()
	{
		var selection = EditorScene.Selection.OfType<GameObject>().ToArray();

		using ( SceneEditorSession.Active.UndoScope( "Duplicate Object(s)" ).WithGameObjectCreations().Push() )
		{
			DuplicateInternal();
		}
	}

	internal static void DuplicateInternal()
	{
		using var scope = SceneEditorSession.Scope();

		var selection = EditorScene.Selection.OfType<GameObject>().Where( go => go.GetType() != typeof( Sandbox.Scene ) ).OrderBy( e => -e.Parent.Children.IndexOf( e ) ).ToArray();

		if ( selection.Length == 0 ) return;

		EditorScene.Selection.Clear();

		var groups = new Dictionary<GameObject, GameObject>( selection.Length );
		foreach ( var entry in selection )
		{
			var next = entry.GetNextSibling( false );
			if ( next.IsValid() && groups.TryGetValue( next, out var forward ) )
				groups.Add( entry, forward );
			else
				groups.Add( entry, entry );
		}

		foreach ( var entry in groups )
		{
			var clone = entry.Key.Clone();

			clone.WorldTransform = entry.Key.WorldTransform;
			entry.Value.AddSibling( clone, false );

			EditorScene.Selection.Add( clone );
		}
	}

	[Menu( "Editor", "Scene/Create Group" )]
	[Shortcut( "editor.group", "CTRL+SHIFT+G" )]
	public static void Group()
	{
		using var scope = SceneEditorSession.Scope();

		var selection = EditorScene.Selection.OfType<GameObject>().Where( go => go.GetType() != typeof( Sandbox.Scene ) ).ToArray();
		var first = selection.FirstOrDefault();

		if ( !first.IsValid() )
		{
			return;
		}

		using ( SceneEditorSession.Active.UndoScope( "Group Objects" ).WithGameObjectChanges( selection, GameObjectUndoFlags.Properties ).WithGameObjectCreations().Push() )
		{
			var go = new GameObject();
			go.WorldTransform = first.WorldTransform;
			go.MakeNameUnique();

			first.AddSibling( go, false );

			for ( var i = 0; i < selection.Length; i++ )
			{
				selection[i].SetParent( go, true );
			}

			EditorScene.Selection.Clear();
			EditorScene.Selection.Add( go );
		}
	}

	[Menu( "Editor", "Scene/Delete" )]
	[Shortcut( "editor.delete", "DEL" )]
	public static void Delete()
	{
		using var scope = SceneEditorSession.Scope();

		// Store selection as array because we are modifying the enumerable before deletion
		var objects = EditorScene.Selection.OfType<GameObject>().Where( x => x.IsDeletable() ).ToArray();

		using ( SceneEditorSession.Active.UndoScope( "Delete Object(s)" ).WithGameObjectDestructions( objects ).Push() )
		{
			var lastSelected = objects.LastOrDefault();
			if ( lastSelected != null )
			{
				var nextSelect = lastSelected.GetNextSibling( false );
				if ( !nextSelect.IsValid() )
					nextSelect = lastSelected.Parent;

				if ( SceneEditorSession.Active.Selection.Contains( lastSelected ) )
				{
					SceneEditorSession.Active.Selection.Clear();
					SceneEditorSession.Active.Selection.Add( nextSelect );
				}
			}

			foreach ( var go in objects )
			{
				go.Destroy();
			}
		}
	}

	[Menu( "Editor", "Scene/Frame Selection" )]
	[Shortcut( "gameObject.frame", "F" )]
	public static void Frame()
	{
		var selectedObjects = EditorScene.Selection.OfType<GameObject>().ToArray();

		if ( selectedObjects.Length == 0 )
			return;

		var bbox = new BBox();

		int i = 0;
		foreach ( var entry in selectedObjects )
		{
			if ( i++ == 0 )
			{
				bbox = BBox.FromPositionAndSize( entry.WorldPosition, 16 );
			}

			// get the bounding box of the selected objects
			bbox = bbox.AddBBox( BBox.FromPositionAndSize( entry.WorldPosition, 16 ) );

			foreach ( var model in entry.Components.GetAll<ModelRenderer>( FindMode.EnabledInSelfAndDescendants ) )
			{
				bbox = bbox.AddBBox( model.Bounds );
			}
		}

		selectedObjects.First().Scene.Editor.FrameTo( bbox );
	}

	[Menu( "Editor", "Scene/Align To View" )]
	[Shortcut( "gameObject.align-to-view", "CTRL+SHIFT+F" )]
	public static void AlignToView()
	{
		if ( !SceneViewportWidget.LastSelected.IsValid() )
			return;

		if ( EditorScene.Selection.Count == 0 )
			return;

		var targetTransform = new Transform( SceneViewportWidget.LastSelected.State.CameraPosition, SceneViewportWidget.LastSelected.State.CameraRotation );
		var gos = EditorScene.Selection.OfType<GameObject>().ToArray();

		gos.DispatchPreEdited( nameof( GameObject.LocalPosition ) );
		gos.DispatchPreEdited( nameof( GameObject.LocalRotation ) );

		using ( SceneEditorSession.Active.UndoScope( "Align Object(s) to View" ).WithGameObjectChanges( gos, GameObjectUndoFlags.Properties ).Push() )
		{
			foreach ( var go in gos )
			{
				go.WorldTransform = targetTransform;
			}
		}

		gos.DispatchEdited( nameof( GameObject.LocalPosition ) );
		gos.DispatchEdited( nameof( GameObject.LocalRotation ) );
	}

	[Menu( "Editor", "Scene/Transforms/Move To Grid" )]
	[Shortcut( "gameObject.move-to-grid", "CTRL+B" )]
	public static void SnapToGrid()
	{
		using var scope = SceneEditorSession.Scope();

		if ( EditorScene.Selection.Count == 0 )
			return;

		var gridSpacing = EditorScene.GizmoSettings.GridSpacing;
		var gos = EditorScene.Selection.OfType<GameObject>();

		gos.DispatchPreEdited( nameof( GameObject.LocalPosition ) );

		using ( SceneEditorSession.Active.UndoScope( "Snap Object(s) to grid" ).WithGameObjectChanges( gos, GameObjectUndoFlags.Properties ).Push() )
		{
			foreach ( var go in gos )
			{
				go.WorldPosition = go.WorldPosition.SnapToGrid( gridSpacing );
			}
		}

		gos.DispatchEdited( nameof( GameObject.LocalPosition ) );
	}

	[Menu( "Editor", "Scene/Transforms/Reset Rotation And Scale" )]
	[Shortcut( "gameObject.reset-rotation-and-scale", "CTRL+0" )]
	public static void ClearRotationAndScale()
	{
		using var scope = SceneEditorSession.Scope();

		if ( EditorScene.Selection.Count == 0 )
			return;

		var gos = EditorScene.Selection.OfType<GameObject>();
		using ( SceneEditorSession.Active.UndoScope( "Reset Object(s) Rotation and scale" ).WithGameObjectChanges( gos, GameObjectUndoFlags.Properties ).Push() )
		{
			foreach ( var go in gos )
			{
				go.WorldRotation = Rotation.Identity;
				go.WorldScale = Vector3.One;
			}
		}
	}

	[Menu( "Editor", "Scene/Transforms/Align Down Local" )]
	[Shortcut( "gameObject.align-down-local", "CTRL+1" )]
	public static void AlignToGroundLocal()
	{
		using var scope = SceneEditorSession.Scope();

		if ( EditorScene.Selection.Count == 0 )
			return;

		var gos = EditorScene.Selection.OfType<GameObject>();
		using ( SceneEditorSession.Active.UndoScope( "Align Object(s)" ).WithGameObjectChanges( gos, GameObjectUndoFlags.Properties ).Push() )
		{
			foreach ( var go in gos )
			{

				var trace = SceneEditorSession.Active.Scene.Trace
						.Ray( go.WorldPosition, go.WorldPosition + go.WorldRotation.Down * 10000 )
						.Size( go.GetBounds().Size )
						.WithoutTags( "trigger" )
						.UseRenderMeshes( true )
						.UsePhysicsWorld( false )
						.Run();

				if ( trace.Hit )
				{
					go.WorldPosition = trace.HitPosition;
				}
			}
		}
	}

	[Menu( "Editor", "Scene/Transforms/Align Down World" )]
	[Shortcut( "gameObject.align-down-world", "CTRL+2" )]
	public static void AlignToGround()
	{
		using var scope = SceneEditorSession.Scope();

		if ( EditorScene.Selection.Count == 0 )
			return;

		var gos = EditorScene.Selection.OfType<GameObject>();
		using ( SceneEditorSession.Active.UndoScope( "Align Object(s)" ).WithGameObjectChanges( gos, GameObjectUndoFlags.Properties ).Push() )
		{
			foreach ( var go in gos )
			{
				var trace = SceneEditorSession.Active.Scene.Trace
					.Ray( go.WorldPosition, go.WorldPosition + Vector3.Down * 10000 )
					.Size( go.GetBounds().Size )
					.WithoutTags( "trigger" )
					.UseRenderMeshes( true )
					.UsePhysicsWorld( false )
					.Run();

				if ( trace.Hit )
				{
					go.WorldPosition = trace.HitPosition;
				}
			}
		}
	}

	[Menu( "Editor", "Scene/Transforms/Align To Closest Normal" )]
	[Shortcut( "gameObject.align-to-closest-normal", "CTRL+3" )]
	public static void AlignToClosestNormal()
	{
		using var scope = SceneEditorSession.Scope();

		if ( EditorScene.Selection.Count == 0 )
			return;

		var gos = EditorScene.Selection.OfType<GameObject>();
		using ( SceneEditorSession.Active.UndoScope( "Align Object(s)" ).WithGameObjectChanges( gos, GameObjectUndoFlags.Properties ).Push() )
		{
			foreach ( var go in gos )
			{
				var trace = SceneEditorSession.Active.Scene.Trace
					.Box( go.GetBounds().Extents, go.LocalPosition, go.LocalPosition + go.WorldRotation.Down * 10000 )
					.WithoutTags( "trigger" )
					.UseRenderMeshes( true )
					.UsePhysicsWorld( false )
					.Run();

				if ( trace.Hit )
				{
					go.WorldRotation = Rotation.LookAt( trace.Normal, Vector3.Up ) * Rotation.From( 90, 0, 0 );
				}
			}
		}
	}

	[Menu( "Editor", "Scene/Transforms/Nudge Up" )]
	[Shortcut( "gameObject.nudge-up", "ALT+UP" )]
	public static void NudgeUp() => Nudge( Vector2.Up );

	[Menu( "Editor", "Scene/Transforms/Nudge Down" )]
	[Shortcut( "gameObject.nudge-down", "ALT+DOWN" )]
	public static void NudgeDown() => Nudge( Vector2.Down );

	[Menu( "Editor", "Scene/Transforms/Nudge Left" )]
	[Shortcut( "gameObject.nudge-left", "ALT+LEFT" )]
	public static void NudgeLeft() => Nudge( Vector2.Left );

	[Menu( "Editor", "Scene/Transforms/Nudge Right" )]
	[Shortcut( "gameObject.nudge-right", "ALT+RIGHT" )]
	public static void NudgeRight() => Nudge( Vector2.Right );

	private static void Nudge( Vector2 direction )
	{
		using var scope = SceneEditorSession.Scope();

		if ( EditorScene.Selection.Count == 0 )
			return;

		if ( !EditorScene.Selection.OfType<GameObject>().Any() )
			return;

		if ( !SceneViewportWidget.LastSelected.IsValid() )
			return;

		var gos = EditorScene.Selection.OfType<GameObject>();
		using ( SceneEditorSession.Active.UndoScope( "Nudge Object(s)" ).WithGameObjectChanges( gos, GameObjectUndoFlags.Properties ).Push() )
		{
			var gizmoInstance = SceneViewportWidget.LastSelected.GizmoInstance;

			var rotation = Rotation.Identity;
			if ( !gizmoInstance.Settings.GlobalSpace )
				rotation = EditorScene.Selection.OfType<GameObject>().FirstOrDefault().WorldRotation;

			using ( gizmoInstance.Push() )
			{
				var delta = Gizmo.Nudge( rotation, direction );

				foreach ( var go in gos )
				{
					go.WorldPosition += delta;
				}
			}
		}
	}

	[Menu( "Editor", "Scene/Deselect All" )]
	[Shortcut( "editor.clear-selection", "ESC" )]
	public static void DeselectAll()
	{
		using ( SceneEditorSession.Active.UndoScope( "Deselect All" ).Push() )
		{
			EditorScene.Selection.Clear();
		}
	}

	public static void ReplaceWithPrefab()
	{
		using var scope = SceneEditorSession.Scope();

		// Store selection as array because we are modifying the enumerable before deletion
		var objects = EditorScene.Selection.OfType<GameObject>().Where( x => x.IsDeletable() ).ToArray();

		// Open Prefab Asset Picker
		var prefabAssetType = AssetType.Find( "prefab", false );

		var picker = AssetPicker.Create( null, prefabAssetType, new AssetPicker.PickerOptions { EnableCloud = false } );
		picker.Window.Title = $"Select Prefab";
		picker.OnAssetPicked = ( selectedAsset ) =>
		{
			if ( selectedAsset.Length == 0 ) return;
			var prefab = selectedAsset[0].LoadResource<PrefabFile>();

			using var scene = SceneEditorSession.Scope();
			using ( SceneEditorSession.Active.UndoScope( "Replace with Prefab" ).WithGameObjectChanges( objects, GameObjectUndoFlags.All ).Push() )
			{
				var prefabScene = SceneUtility.GetPrefabScene( prefab );
				if ( prefabScene is null )
				{
					Log.Error( $"Failed to fetch prefab scene for {prefab.ResourcePath}." );
					return;
				}
				foreach ( var gameObject in objects )
				{
					// Clone the prefab to get a fresh instance with unique Ids
					var tempPrefabInstance = prefabScene.Clone();
					var prefabInstanceJson = tempPrefabInstance.Serialize();
					tempPrefabInstance.DestroyImmediate();

					// Serialize into the existing game object to keep references intact
					SceneUtility.MakeIdGuidsUnique( prefabInstanceJson, gameObject.Id );
					gameObject.Clear();
					var originalTransform = gameObject.WorldTransform;
					var originalName = gameObject.Name;
					gameObject.Deserialize( prefabInstanceJson );
					gameObject.WorldTransform = originalTransform;
					gameObject.Name = originalName;
				}
			}
		};
		picker.Show();
	}
}
