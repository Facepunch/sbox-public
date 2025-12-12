using static Editor.BaseItemWidget;
namespace Editor;

partial class GameObjectNode : TreeNode<GameObject>
{
	public GameObjectNode( GameObject o ) : base( o )
	{
		Height = Theme.RowHeight;
	}

	public override string Name
	{
		get => Value.Name;
		set => Value.Name = value;
	}

	public override bool CanEdit => true;

	protected override void BuildChildren() => SetChildren( Value.Children.Where( x => x.ShouldShowInHierarchy() ), x => new GameObjectNode( x ) );
	protected override bool HasDescendant( object obj ) => obj is GameObject go && Value.IsDescendant( go );

	public override int ValueHash
	{
		get
		{
			HashCode hc = new HashCode();
			hc.Add( Value.Name );
			hc.Add( Value.IsPrefabInstance );
			hc.Add( Value.Flags );
			hc.Add( Value.NetworkMode );
			hc.Add( Value.Network.Active );
			hc.Add( Value.Network.IsOwner );
			hc.Add( Value.IsProxy );
			hc.Add( Value.Active );

			foreach ( var val in Value.Children )
			{
				if ( !val.ShouldShowInHierarchy() ) continue;

				hc.Add( val );
			}

			return hc.ToHashCode();
		}
	}

	public override void OnPaint( VirtualWidget item )
	{
		if ( !Value.Scene.IsValid() )
			return;

		var isEven = item.Row % 2 == 0;
		var selected = item.Selected || item.Pressed || item.Dragging;
		var isBone = Value.Flags.Contains( GameObjectFlags.Bone );
		var isProceduralBone = Value.Flags.Contains( GameObjectFlags.ProceduralBone );
		var isAttachment = Value.Flags.Contains( GameObjectFlags.Attachment );
		var isNetworked = Value.Scene.IsEditor ? Value.NetworkMode == NetworkMode.Object : Value.Network.Active;
		var isNetworkRoot = isNetworked && (Value.Scene.IsEditor ? Value.NetworkMode == NetworkMode.Object : Value.IsNetworkRoot);

		bool isErrored = Value.Flags.Contains( GameObjectFlags.Error );
		bool isLoading = Value.Flags.Contains( GameObjectFlags.Loading );
		bool isTemporary = Value.Flags.Contains( GameObjectFlags.NotSaved );
		bool isEditorOnly = Value.Flags.Contains( GameObjectFlags.EditorOnly );

		var fullSpanRect = item.Rect;
		fullSpanRect.Left = 0;
		fullSpanRect.Right = TreeView.Width;

		float opacity = 0.9f;

		if ( !Value.Active ) opacity *= 0.5f;

		Color pen = Theme.TextControl;
		string icon = "layers";
		Color iconColor = Theme.TextControl.WithAlpha( 0.6f );
		Color overlayIconColor = iconColor;

		string overlayIcon = null;

		if ( Value.IsPrefabInstance )
		{
			pen = Theme.Blue;
			overlayIconColor = Theme.Blue;

			if ( Value.IsPrefabInstanceRoot )
			{
				icon = "dataset";
				iconColor = Theme.Blue;

				if ( EditorUtility.Prefabs.IsInstanceModified( Value ) )
				{
					Paint.ClearPen();
					Paint.SetBrush( Theme.Blue.Darken( 0.25f ) );
					var modifiedRect = fullSpanRect;
					modifiedRect.Width = 2;
					Paint.DrawRect( modifiedRect );
				}
			}

			if ( EditorUtility.Prefabs.IsGameObjectAddedToInstance( Value ) )
			{
				overlayIcon = "add_circle";
			}
		}

		if ( isBone )
		{
			icon = "polyline";
			iconColor = Theme.Pink.WithAlpha( 0.8f );

			if ( isProceduralBone )
			{
				iconColor = Theme.Blue.WithAlpha( 0.8f );
			}
		}

		if ( isAttachment )
		{
			icon = "push_pin";
			iconColor = Theme.Pink.WithAlpha( 0.8f );

			if ( isProceduralBone )
			{
				iconColor = Theme.Blue.WithAlpha( 0.8f );
			}
		}

		if ( isTemporary )
		{
			iconColor = Color.White.WithAlpha( 0.5f );
			pen = iconColor.Lighten( 0.2f ).WithAlpha( 0.8f );
			icon = "no_sim";
		}

		if ( isNetworked )
		{
			icon = "rss_feed";
			iconColor = Theme.Blue.WithAlpha( 0.8f );

			if ( Value.Network.IsOwner )
			{
				iconColor = Theme.Green.WithAlpha( 0.8f );
			}

			if ( Value.IsProxy )
			{
				iconColor = Theme.TextControl.WithAlpha( 0.6f );
			}

			if ( !isNetworkRoot ) iconColor = iconColor.WithAlphaMultiplied( 0.4f );
		}

		if ( isErrored )
		{
			icon = "report";
			iconColor = Theme.Red.WithAlpha( 0.8f );
		}

		if ( isEditorOnly )
		{
			icon = "highlight_alt";
			iconColor = Theme.Yellow.WithAlpha( 0.8f );
			pen = Theme.Yellow.WithAlpha( 0.6f );
		}


		//
		// If there's a drag and drop happening, fade out nodes that aren't possible
		//
		if ( TreeView.IsBeingDroppedOn )
		{
			if ( TreeView.CurrentItemDragEvent.Data.Object is GameObject[] gos && gos.Any( go => Value.IsAncestor( go ) ) )
			{
				opacity *= 0.23f;
			}
			else if ( TreeView.CurrentItemDragEvent.Data.Object is GameObject go && Value.IsAncestor( go ) )
			{
				opacity *= 0.23f;
			}
		}

		if ( item.Dropping )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Blue );

			if ( TreeView.CurrentItemDragEvent.DropEdge.HasFlag( ItemEdge.Top ) )
			{
				var droprect = item.Rect;
				droprect.Top -= 1;
				droprect.Height = 2;
				Paint.DrawRect( droprect, 2 );
			}
			else if ( TreeView.CurrentItemDragEvent.DropEdge.HasFlag( ItemEdge.Bottom ) )
			{
				var droprect = item.Rect;
				droprect.Top = droprect.Bottom - 1;
				droprect.Height = 2;
				Paint.DrawRect( droprect, 2 );
			}
			else
			{
				Paint.SetBrushAndPen( Theme.Blue.WithAlpha( 0.2f ), Theme.Blue );
				Paint.PenSize = 2;
				Paint.DrawRect( item.Rect, 4 );
			}
		}

		if ( selected )
		{
			//item.PaintBackground( Color.Transparent, 3 );
			Paint.ClearPen();
			Paint.SetBrush( Theme.SelectedBackground.WithAlpha( opacity ) );
			Paint.DrawRect( fullSpanRect );
		}
		else if ( isEven )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.SurfaceLightBackground.WithAlpha( 0.1f ) );
			Paint.DrawRect( fullSpanRect );
		}

		var name = Value.Name;
		if ( string.IsNullOrWhiteSpace( name ) ) name = "Untitled GameObject";

		var r = item.Rect;
		r.Left += 4;

		var iconSize = 16;

		Paint.Pen = iconColor.WithAlphaMultiplied( opacity );
		Paint.DrawIcon( r, icon, iconSize, TextFlag.LeftCenter );
		if ( !string.IsNullOrEmpty( overlayIcon ) )
		{
			var overlayIconRect = r;
			overlayIconRect.Left += 8;
			overlayIconRect.Top += 8;
			overlayIconRect.Width = 13;
			overlayIconRect.Height = 13;
			Paint.Pen = Theme.WidgetBackground;
			Paint.SetBrush( Theme.WidgetBackground );
			Paint.DrawRect( overlayIconRect, 12 );
			overlayIconRect.Left += 1;
			Paint.Pen = overlayIconColor;
			Paint.DrawIcon( overlayIconRect, overlayIcon, 13, TextFlag.Center );
		}
		r.Left += 22;

		Paint.Pen = pen.WithAlphaMultiplied( opacity );
		Paint.SetDefaultFont();
		r.Left += Paint.DrawText( r, name, TextFlag.LeftCenter ).Width + 4;

		if ( isLoading )
		{
			Paint.Pen = Theme.Blue;
			Paint.DrawIcon( r, "access_time_filled", iconSize, TextFlag.LeftCenter );
			r.Left += 22;
		}

		if ( isNetworkRoot && Value.Network.OwnerId != Guid.Empty )
		{
			var connection = Connection.Find( Value.Network.OwnerId );
			if ( connection is null )
			{
				Paint.Pen = Theme.Blue;
				Paint.DrawText( r, $"Unknown Owner - {Value.Network.OwnerId}", TextFlag.LeftCenter );
				r.Left += 22;
			}
			else
			{
				Paint.Pen = Theme.Blue;
				Paint.DrawText( r, $"{connection.DisplayName}", TextFlag.LeftCenter );
				r.Left += 22;
			}


		}
	}

	public override void OnRename( VirtualWidget item, string text, List<TreeNode> selection = null )
	{
		var undoName = selection != null && selection.Count > 1 ? $"Rename {selection.Count} Objects" : "Rename Object";
		var gos = selection.Select( x => x.Value ).OfType<GameObject>().ToArray();
		using ( SceneEditorSession.Active.UndoScope( undoName ).WithGameObjectChanges( gos, GameObjectUndoFlags.All ).Push() )
		{
			foreach ( var go in gos )
			{
				go.Name = text;
			}
			var goCount = gos.Count();
			if ( goCount > 1 ) // Only make unique if we renamed multiple objects
			{
				for ( int i = 0; i < goCount; i++ )
				{
					var go = gos.ElementAt( (i + 1) % goCount ); // Offset by one so we run the first one LAST, letting it keep its name if possible
					go.MakeNameUnique();
				}
			}
		}
	}

	public override bool OnDragStart()
	{
		var drag = new Drag( TreeView );

		if ( TreeView.IsSelected( Value ) )
		{
			// If we're selected then use all selected items in the tree.
			drag.Data.Object = TreeView.SelectedItems.OfType<GameObject>().ToArray();
		}
		else
		{
			// Otherwise let's just drag this one.
			drag.Data.Object = new[] { Value };
		}

		drag.Execute();

		return true;
	}

	private async void Drop( string text )
	{
		var drop = await BaseDropObject.CreateDropFor( text );

		if ( drop is null )
			return;

		await drop.StartInitialize( text );

		using ( var sc = Value.Scene.Push() )
		{
			await drop.OnDrop();
			var go = drop.GameObject;
			if ( go.IsValid() )
			{
				go.Parent = Value;
				go.LocalTransform = new Transform();
				EditorScene.Selection.Add( go );
			}
		}
		drop.Delete();
	}

	public override DropAction OnDragDrop( ItemDragEvent e )
	{
		using var scope = Value.Scene.Push();

		if ( e.Data.OfType<Component>().FirstOrDefault() is Component draggedComp )
		{
			if ( Value is Scene )
				return DropAction.Ignore;

			if ( e.IsDrop && EditorTypeLibrary.TryGetType( draggedComp.GetType(), out TypeDescription typeDesc ) )
			{
				Menu m = new ContextMenu( null );

				var targetGo = Value;

				m.AddOption( $"Copy {draggedComp.GetType().Name} here", action: () =>
				{
					using var scene = SceneEditorSession.Scope();

					using ( SceneEditorSession.Active.UndoScope( $"Copy Component" ).WithComponentCreations().Push() )
					{
						var newComp = targetGo.Components.Create( typeDesc );
						var json = draggedComp.Serialize().AsObject();
						json.Remove( "__guid" );
						newComp.DeserializeImmediately( json );

						SceneEditorSession.Active.Selection.Clear();
						SceneEditorSession.Active.Selection.Add( targetGo );
					}
				} );
				m.AddOption( $"Move {draggedComp.GetType().Name} here", action: () =>
				{
					using var scene = SceneEditorSession.Scope();

					using ( SceneEditorSession.Active.UndoScope( $"Move Component" ).WithComponentDestructions( draggedComp ).WithComponentCreations().Push() )
					{
						var newComp = targetGo.Components.Create( typeDesc );

						var json = draggedComp.Serialize().AsObject();
						json.Remove( "__guid" );
						newComp.DeserializeImmediately( json );

						draggedComp.Destroy();

						SceneEditorSession.Active.Selection.Clear();
						SceneEditorSession.Active.Selection.Add( targetGo );
					}
				} );

				m.OpenAtCursor();
			}
			return DropAction.Move;
		}

		if ( e.Data.Object is GameObject[] draggedGos )
		{
			var targetGo = Value;

			if ( draggedGos.Any( go => go == targetGo || targetGo.IsAncestor( go ) ) )
			{
				return DropAction.Ignore;
			}

			using var scene = SceneEditorSession.Scope();

			if ( e.IsDrop )
			{
				using ( SceneEditorSession.Active.UndoScope( "Change Game Object Order" ).WithGameObjectChanges( draggedGos, GameObjectUndoFlags.Properties ).Push() )
				{
					foreach ( var draggedGo in draggedGos )
					{
						if ( Value is not Scene && e.DropEdge.HasFlag( ItemEdge.Top ) )
						{
							targetGo.AddSibling( draggedGo, true );
						}
						else if ( Value is not Scene && e.DropEdge.HasFlag( ItemEdge.Bottom ) )
						{
							targetGo.AddSibling( draggedGo, false );
						}
						else
						{
							draggedGo.SetParent( targetGo, true );
						}
					}
				}
			}
			return DropAction.Move;
		}

		var asset = AssetSystem.FindByPath( e.Data.FileOrFolder );
		if ( asset is not null && asset.AssetType.FileExtension == "prefab" )
		{
			var pf = asset.LoadResource<PrefabFile>();
			if ( pf is null ) return DropAction.Ignore;

			if ( e.IsDrop )
			{
				using var scene = SceneEditorSession.Scope();

				using ( SceneEditorSession.Active.UndoScope( "Instantiate Prefab" ).WithGameObjectCreations().Push() )
				{
					var instantiated = SceneUtility.GetPrefabScene( pf )?.Clone();

					if ( Value is not Scene && e.DropEdge.HasFlag( ItemEdge.Top ) )
					{
						Value.AddSibling( instantiated, true );
					}
					else if ( Value is not Scene && e.DropEdge.HasFlag( ItemEdge.Bottom ) )
					{
						Value.AddSibling( instantiated, false );
					}
					else
					{
						instantiated.SetParent( Value, true );
					}

					SceneEditorSession.Active.Selection.Set( instantiated );
				}
			}

			return DropAction.Move;
		}

		if ( e.Data.Url is not null && e.IsDrop )
		{
			Drop( e.Data.Url.ToString() );
			return DropAction.Move;
		}

		if ( !string.IsNullOrEmpty( e.Data.FileOrFolder ) && e.IsDrop )
		{
			Drop( e.Data.FileOrFolder );
			return DropAction.Move;
		}

		return DropAction.Ignore;
	}

	public override bool OnContextMenu()
	{
		var m = new ContextMenu( TreeView );

		AddGameObjectMenuItems( m, this );

		m.OpenAtCursor( false );

		return true;
	}

	public static void AddGameObjectMenuItems( Menu m, TreeNode treeNode )
	{
		GameObject gameObject = treeNode.Value as GameObject;
		if ( gameObject is Scene ) gameObject = null;

		bool isObjectMenu = gameObject.IsValid();
		bool isPrefabRoot = isObjectMenu && treeNode is PrefabNode;

		// For object creation, we need to determine the parent at click time, not menu creation time
		// (because right-clicking on empty space deselects nodes, and we want root-level creation in that case)
		Func<IEnumerable<GameObject>> getParentsForCreation = () =>
		{
			var currentSelection = EditorScene.Selection.OfType<GameObject>();

			if ( currentSelection.Any() )
			{
				// Use current selection as parents
				var validParents = currentSelection.Where( x => x != null && x.IsValid() );
				return validParents;
			}
			else
			{
				// No current selection - create at root level
				return [null];
			}
		};

		m.AddOption( "Cut", "content_cut", EditorScene.Cut, "editor.cut" ).Enabled = isObjectMenu && !isPrefabRoot;
		m.AddOption( "Copy", "content_copy", EditorScene.Copy, "editor.copy" ).Enabled = isObjectMenu;
		m.AddOption( "Paste", "content_paste", EditorScene.Paste, "editor.paste" );
		m.AddOption( "Paste As Child", null, EditorScene.PasteAsChild, "editor.paste-as-child" ).Enabled = isObjectMenu;
		m.AddOption( "Create Group", "file_copy", SceneEditorMenus.Group, "editor.group" ).Enabled = isObjectMenu && !isPrefabRoot;
		m.AddSeparator();
		m.AddOption( "Rename", "label", treeNode.TreeView.BeginRename, "editor.rename" ).Enabled = isObjectMenu;
		m.AddOption( "Duplicate", "file_copy", SceneEditorMenus.Duplicate, "editor.duplicate" ).Enabled = isObjectMenu && !isPrefabRoot;
		m.AddOption( "Delete", "delete", SceneEditorMenus.Delete, "editor.delete" ).Enabled = isObjectMenu && !isPrefabRoot;

		m.AddSeparator();

		Menu addMenu = m.AddMenu( isObjectMenu ? "Create Child" : "Create", "add" );
		CreateObjectMenu( addMenu, go =>
		{
			treeNode.TreeView.Open( treeNode );
		}, createdGos =>
		{
			treeNode.TreeView.SelectItems( createdGos, skipEvents: true );
			treeNode.TreeView.BeginRename();
		} );

		if ( gameObject.IsValid() ) // has selection
		{
			var selectedGos = EditorScene.Selection.OfType<GameObject>().ToArray();

			m.AddSeparator();

			// prefabs
			if ( selectedGos.All( x => x.IsPrefabInstance ) )
			{
				var sources = selectedGos.Select( x => x.PrefabInstanceSource ).Distinct();
				bool multipleSources = sources.Count() > 1;

				var prefabPath = gameObject.PrefabInstanceSource;
				var rootPrefabName = System.IO.Path.GetFileNameWithoutExtension( prefabPath ).ToTitleCase();

				var prefabMenu = m.AddMenu( multipleSources ? "Prefabs (multiple)" : $"Prefab '{rootPrefabName}'", "dataset" );

				// Only locate the prefab if we have a single prefab selected (or all instances share the same prefab)
				if ( !multipleSources )
				{
					var prefabAsset = AssetSystem.FindByPath( prefabPath );
					if ( prefabAsset is not null )
					{
						prefabMenu.AddOption( "Open in Editor", "edit", () =>
						{
							if ( prefabAsset.TryLoadResource<PrefabFile>( out var prefab ) && prefab.IsValid )
							{
								EditorScene.OpenPrefab( prefab );
							}
						} ).Enabled = !prefabAsset.IsProcedural;

						prefabMenu.AddOption( "Find in Asset Browser", "search", () =>
						{
							LocalAssetBrowser.OpenTo( prefabAsset );
						} );
					}
				}

				prefabMenu.AddSeparator();

				var unlinkFromPrefabActionName = multipleSources ? "Unlink From Prefabs" : "Unlink From Prefab";
				prefabMenu.AddOption( unlinkFromPrefabActionName, "link_off", () =>
				{
					using ( SceneEditorSession.Active.UndoScope( unlinkFromPrefabActionName ).WithGameObjectChanges( selectedGos, GameObjectUndoFlags.All ).Push() )
					{
						EditorScene.Selection.Clear();
						foreach ( var go in selectedGos )
						{
							go.BreakFromPrefab();
							EditorScene.Selection.Add( go );
						}
					}
				} );

				prefabMenu.AddSeparator();

				var outermostPrefabName = EditorUtility.Prefabs.GetOuterMostPrefabName( gameObject );
				var isModified = selectedGos.Any( x => (x.IsPrefabInstanceRoot && EditorUtility.Prefabs.IsInstanceModified( x ))
													|| (x.IsPrefabInstance && EditorUtility.Prefabs.IsGameObjectInstanceModified( gameObject )) );

				var isAdded = selectedGos.All( x => x.IsPrefabInstance && EditorUtility.Prefabs.IsGameObjectAddedToInstance( x ) );

				if ( isAdded )
				{
					var applyAddActionName = multipleSources ? "Add Objects to Prefabs" : "Add Object to Prefab";
					if ( EditorUtility.Prefabs.IsOuterMostPrefabRoot( gameObject ) )
					{
						var parentPrefabName = EditorUtility.Prefabs.GetOuterMostPrefabName( gameObject.Parent );
						applyAddActionName += $" '{parentPrefabName ?? "Invalid"}'";
					}
					prefabMenu.AddOption( applyAddActionName, "update", () =>
					{
						using var scene = SceneEditorSession.Scope();

						// Undo for this is not really possible, as it doesn't modify this scene but rather the prefab asset.
						// But flag as unsaved to make sure user gets prompted after using this action.
						SceneEditorSession.Active.HasUnsavedChanges = true;

						foreach ( var go in selectedGos )
						{
							if ( go.IsPrefabInstance )
							{
								EditorUtility.Prefabs.AddInstanceAddedGameObjectToPrefab( go );
							}
						}
					} );
				}
				else
				{
					var applyChangesActionName = multipleSources ? "Apply Instance Changes to Prefabs" : "Apply Instance Changes To Prefab";
					prefabMenu.AddOption( applyChangesActionName, "update", () =>
					{
						using var scene = SceneEditorSession.Scope();

						// Undo for this is not really possible, as it doesn't modify this scene but rather the prefab asset.
						// But flag as unsaved to make sure user gets prompted after using this action.
						SceneEditorSession.Active.HasUnsavedChanges = true;

						foreach ( var go in selectedGos )
						{
							if ( go.IsPrefabInstanceRoot )
							{
								EditorUtility.Prefabs.WriteInstanceToPrefab( go );
							}
							else if ( go.IsPrefabInstance )
							{
								EditorUtility.Prefabs.ApplyGameObjectInstanceChangesToPrefab( go );
							}
						}
					} ).Enabled = isModified;
				}



				var revertChangesActionName = "Revert Instance Changes";
				prefabMenu.AddOption( revertChangesActionName, "history", () =>
				{
					using var scene = SceneEditorSession.Scope();

					using ( SceneEditorSession.Active.UndoScope( revertChangesActionName ).WithGameObjectChanges( selectedGos, GameObjectUndoFlags.Properties ).Push() )
					{
						foreach ( var go in selectedGos )
						{
							if ( go.IsPrefabInstanceRoot )
							{
								EditorUtility.Prefabs.RevertInstanceToPrefab( go );
							}
							else if ( go.IsPrefabInstance )
							{
								EditorUtility.Prefabs.RevertGameObjectInstanceChanges( go );
							}
						}
					}
				} ).Enabled = isModified;
			}

			if ( !isPrefabRoot )
			{
				m.AddOption( "Create New Prefab", "note_add", () => ConvertToPrefab( selectedGos ) );
			}

			m.AddOption( "Replace with Prefab", "change_circle", SceneEditorMenus.ReplaceWithPrefab ).Enabled = isObjectMenu && !isPrefabRoot;
		}
	}

	static void ConvertToPrefab( IEnumerable<GameObject> gameObjects )
	{
		using var scene = SceneEditorSession.Scope();

		using ( SceneEditorSession.Active.UndoScope( "Convert to Prefab" ).WithGameObjectChanges( gameObjects, GameObjectUndoFlags.All ).WithGameObjectCreations().Push() )
		{
			var selection = gameObjects.ToArray();
			var first = selection.FirstOrDefault();

			if ( first is null ) return;

			var saveLocation = "";

			var fd = new FileDialog( null );
			fd.Title = $"Save {first.Name} as Prefab..";
			fd.Directory = Project.Current.GetAssetsPath();
			fd.DefaultSuffix = $".prefab";
			fd.SelectFile( first.Name );
			fd.SetFindFile();
			fd.SetModeSave();
			fd.SetNameFilter( $"Prefab File (*.prefab)" );

			if ( !fd.Execute() ) return;

			saveLocation = fd.SelectedFile;

			GameObject prefabRoot;

			if ( selection.Count() == 1 )
			{
				prefabRoot = first;
			}
			else
			{
				prefabRoot = new GameObject();
				prefabRoot.WorldTransform = first.WorldTransform;

				for ( var i = 0; i < selection.Length; i++ )
				{
					selection[i].SetParent( prefabRoot, true );
				}
			}
			prefabRoot.Name = System.IO.Path.GetFileNameWithoutExtension( saveLocation );

			EditorUtility.Prefabs.ConvertGameObjectToPrefab( prefabRoot, saveLocation );
			EditorUtility.InspectorObject = prefabRoot;

			EditorScene.Selection.Clear();
		}
	}

	public override void OnActivated()
	{
		SceneEditorMenus.Frame();
	}

	public static void CreateObjectMenu( Menu menu, GameObject parent, Action<GameObject> afterCreate )
	{
		void PostCreate( GameObject go, GameObject parent )
		{
			go.Parent = parent;

			if ( !EditorPreferences.CreateObjectsAtOrigin && !parent.IsValid() )
			{
				// I wonder if we should be tracing and placing it on the surface?
				go.LocalPosition = SceneViewportWidget.LastSelected.State.CameraPosition + SceneViewportWidget.LastSelected.State.CameraRotation.Forward * 300;
			}

			afterCreate?.Invoke( go );
		}

		menu.AddOption( "Empty", "dataset", () =>
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Create Empty" ).WithGameObjectCreations().Push() )
			{
				var createdObjects = new List<GameObject>();
				var go = new GameObject( true, "Object" );
				PostCreate( go, parent );
				createdObjects.Add( go );
			}
		} );

		foreach ( var entry in EditorUtility.Prefabs.GetTemplates() )
		{
			var menuPath = string.IsNullOrEmpty( entry.MenuPath ) ? entry.ResourceName : entry.MenuPath;
			menu.AddOption( menuPath.Split( '/' ), entry.MenuIcon, () =>
			{
				using var scope = SceneEditorSession.Scope();

				using ( SceneEditorSession.Active.UndoScope( "Create Prefab" ).WithGameObjectCreations().Push() )
				{
					var createdObjects = new List<GameObject>();
					var goName = menuPath.Split( '/' ).Last();
					var go = SceneUtility.GetPrefabScene( entry )?.Clone();
					if ( !entry.DontBreakAsTemplate ) go.BreakFromPrefab();
					go.Name = goName;
					PostCreate( go, parent );
					createdObjects.Add( go );
				}
			} );
		}

		menu.AddOption( "3D Object/Terrain".Split( '/' ), "landscape", () =>
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Create Terrain" ).WithGameObjectCreations().Push() )
			{
				var createdObjects = new List<GameObject>();
				var go = new GameObject( true, "Terrain" );
				go.Components.Create<Terrain>();
				PostCreate( go, parent );
				createdObjects.Add( go );
			}
		} );
	}

	public static void CreateObjectMenu( Menu menu, Action<GameObject> afterCreate, Action<IEnumerable<GameObject>> afterComplete )
	{
		void PostCreate( GameObject go, GameObject parent )
		{
			go.Parent = parent;

			if ( !EditorPreferences.CreateObjectsAtOrigin && !parent.IsValid() )
			{
				// I wonder if we should be tracing and placing it on the surface?
				go.LocalPosition = SceneViewportWidget.LastSelected.State.CameraPosition + SceneViewportWidget.LastSelected.State.CameraRotation.Forward * 300;
			}

			afterCreate?.Invoke( go );
		}

		menu.AddOption( "Empty", "dataset", () =>
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Create Empty" ).WithGameObjectCreations().Push() )
			{
				var createdObjects = new List<GameObject>();
				var parents = GetParentsForCreation();
				foreach ( var parent in parents )
				{
					var go = new GameObject( true, "Object" );
					PostCreate( go, parent );
					createdObjects.Add( go );
				}
				afterComplete?.Invoke( createdObjects );
			}
		} );

		foreach ( var entry in EditorUtility.Prefabs.GetTemplates() )
		{
			var menuPath = string.IsNullOrEmpty( entry.MenuPath ) ? entry.ResourceName : entry.MenuPath;
			menu.AddOption( menuPath.Split( '/' ), entry.MenuIcon, () =>
			{
				using var scope = SceneEditorSession.Scope();

				using ( SceneEditorSession.Active.UndoScope( "Create Prefab" ).WithGameObjectCreations().Push() )
				{
					var createdObjects = new List<GameObject>();
					var goName = menuPath.Split( '/' ).Last();
					var parents = GetParentsForCreation();
					foreach ( var parent in parents )
					{
						var go = SceneUtility.GetPrefabScene( entry )?.Clone();
						if ( !entry.DontBreakAsTemplate ) go.BreakFromPrefab();
						go.Name = goName;
						PostCreate( go, parent );
						createdObjects.Add( go );
					}
					afterComplete?.Invoke( createdObjects );
				}
			} );
		}

		menu.AddOption( "3D Object/Terrain".Split( '/' ), "landscape", () =>
		{
			using var scope = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( "Create Terrain" ).WithGameObjectCreations().Push() )
			{
				var createdObjects = new List<GameObject>();
				var parents = GetParentsForCreation();
				foreach ( var parent in parents )
				{
					var go = new GameObject( true, "Terrain" );
					go.Components.Create<Terrain>();
					PostCreate( go, parent );
					createdObjects.Add( go );
				}
				afterComplete?.Invoke( createdObjects );
			}
		} );
	}

	public static void CreateObjectCategoryMenu( string category, Menu menu, GameObject parent, Action<GameObject> afterCreate )
	{
		void PostCreate( GameObject go )
		{
			go.Parent = parent;

			if ( !EditorPreferences.CreateObjectsAtOrigin && !parent.IsValid() )
			{
				// I wonder if we should be tracing and placing it on the surface?
				go.LocalPosition = SceneViewportWidget.LastSelected.State.CameraPosition + SceneViewportWidget.LastSelected.State.CameraRotation.Forward * 300;
			}

			afterCreate?.Invoke( go );
		}

		foreach ( var entry in EditorUtility.Prefabs.GetTemplates() )
		{
			var menuPath = string.IsNullOrEmpty( entry.MenuPath ) ? entry.ResourceName : entry.MenuPath;

			if ( !menuPath.Contains( '/' ) )
				continue;

			if ( menuPath.Split( '/' )[0] != category )
				continue;

			menu.AddOption( menuPath.Split( '/' )[1], entry.MenuIcon, () =>
			{
				using var scope = SceneEditorSession.Scope();

				using ( SceneEditorSession.Active.UndoScope( "Create Prefab" ).WithGameObjectCreations().Push() )
				{
					var go = SceneUtility.GetPrefabScene( entry )?.Clone();
					if ( !entry.DontBreakAsTemplate ) go.BreakFromPrefab();
					go.Name = menuPath.Split( '/' ).Last();
					PostCreate( go );
				}
			} );
		}
	}

	/// <summary>
	/// Determines the parents for context-menu GameObject creation (at the time the menu option is selected, not when the menu is built).
	/// </summary>
	private static IEnumerable<GameObject> GetParentsForCreation()
	{
		var currentSelection = EditorScene.Selection.OfType<GameObject>();

		if ( currentSelection.Any() )
		{
			// Use current selection as parents
			var validParents = currentSelection.Where( x => x != null && x.IsValid() );
			return validParents;
		}
		else
		{
			// No current selection, create at root
			return [null];
		}
	}
}

class GameObjectSearchNode : GameObjectNode
{
	public override bool HasChildren => false;
	public GameObjectSearchNode( GameObject o ) : base( o )
	{
	}
}
