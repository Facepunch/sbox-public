using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Editor.Mcp;

[McpToolset( "scene", "Open scenes - the game object hierarchy, creating and editing game objects and components, the editor camera and viewport screenshots" )]
public static partial class SceneTools
{
	/// <summary>
	/// List the scenes open in the editor - name, resource path, type (scene, prefab or the running game
	/// session), whether it's the active tab, unsaved changes, and root object count.
	/// </summary>
	[McpTool.ReadOnly( "list_scenes" )]
	public static OpenSceneList ListScenes()
	{
		return new OpenSceneList
		{
			Scenes = SceneEditorSession.All.Select( session => new OpenScene
			{
				Name = session.Scene?.Name,
				ResourcePath = session.Scene?.Source?.ResourcePath,
				Type = session is GameEditorSession ? "Game" : session.Scene is PrefabScene ? "Prefab" : "Scene",
				IsActive = session == SceneEditorSession.Active,
				HasUnsavedChanges = session.HasUnsavedChanges,
				RootObjectCount = session.Scene?.Children.Count ?? 0
			} ).ToArray()
		};
	}

	/// <summary>
	/// The game object hierarchy of an open scene - every object's name, id, enabled state and component
	/// types, nested like the editor's hierarchy panel. Use the ids with get_game_object to dig into a
	/// specific object.
	/// </summary>
	/// <param name="scene">Scene name or resource path from list_scenes. Empty for the active scene.</param>
	/// <param name="maxDepth">How many levels deep to descend. 0 for unlimited.</param>
	[McpTool.ReadOnly( "scene_tree" )]
	public static SceneTreeResult SceneTree( string scene = "", int maxDepth = 0 )
	{
		var target = ResolveScene( scene );
		var budget = 5000;

		SceneTreeNode Node( GameObject go, int depth )
		{
			budget--;

			var cut = depth == 1 || budget <= 0;

			return new SceneTreeNode
			{
				Name = go.Name,
				Id = go.Id,
				Enabled = go.Enabled,
				Components = go.Components.GetAll().Select( x => x.GetType().Name ).ToArray(),
				Children = cut ? null : go.Children.Select( x => Node( x, depth - 1 ) ).ToArray(),
				ChildrenNotShown = cut ? go.Children.Count : 0
			};
		}

		return new SceneTreeResult
		{
			Scene = target.Name,
			ResourcePath = target.Source?.ResourcePath,
			Objects = target.Children.Select( x => Node( x, maxDepth <= 0 ? int.MaxValue : maxDepth ) ).ToArray()
		};
	}

	/// <summary>
	/// Everything about one game object - enabled state, tags, world transform, parent, children, and its
	/// components. Set includeComponentProperties to also get every component's serialized properties.
	/// </summary>
	/// <param name="id">The game object's id, from scene_tree or find_game_objects.</param>
	/// <param name="includeComponentProperties">Include the full serialized properties of every component, not just their types.</param>
	[McpTool.ReadOnly( "get_game_object" )]
	public static GameObjectDetails GetGameObject( string id, bool includeComponentProperties = false )
	{
		var go = FindByGuid( id );

		return new GameObjectDetails
		{
			Scene = go.Scene?.Name,
			Name = go.Name,
			Id = go.Id,
			Enabled = go.Enabled,
			ActiveInHierarchy = go.Active,
			Tags = go.Tags.TryGetAll().ToArray(),
			Parent = go.Parent is null || go.Parent is Scene ? null : new ObjectRef { Id = go.Parent.Id, Name = go.Parent.Name },
			WorldPosition = go.WorldPosition,
			WorldRotation = go.WorldRotation.Angles(),
			WorldScale = go.WorldScale,
			Components = go.Components.GetAll().Select( x => new ComponentInfo
			{
				Type = x.GetType().Name,
				Id = x.Id,
				Enabled = x.Enabled,
				Properties = includeComponentProperties ? x.Serialize() : null
			} ).ToArray(),
			Children = go.Children.Select( x => new ChildObject { Id = x.Id, Name = x.Name, Enabled = x.Enabled } ).ToArray()
		};
	}

	/// <summary>
	/// Search a scene's game objects by name substring, component type and/or tag. All filters are optional
	/// and combine - empty filters list everything. Returns each match with its hierarchy path and id.
	/// </summary>
	/// <param name="name">Case insensitive substring of the object's name.</param>
	/// <param name="component">Case insensitive substring of a component type name the object must have.</param>
	/// <param name="tag">A tag the object must have.</param>
	/// <param name="scene">Scene name or resource path from list_scenes. Empty for the active scene.</param>
	/// <param name="limit">How many matches to return.</param>
	[McpTool.ReadOnly( "find_game_objects" )]
	public static FoundObjects FindGameObjects( string name = "", string component = "", string tag = "",
		string scene = "", [Sandbox.Range( 1, 500 )] int limit = 100 )
	{
		var target = ResolveScene( scene );
		var results = new List<FoundObject>();
		var total = 0;

		// An explicit stack with a visit budget - a pathologically big scene shouldn't hang
		// the editor or overflow the stack
		var budget = 100_000;
		var pending = new Stack<(GameObject Object, string ParentPath)>();

		foreach ( var root in target.Children.AsEnumerable().Reverse() )
		{
			pending.Push( (root, "") );
		}

		while ( pending.Count > 0 && budget-- > 0 )
		{
			var (go, parentPath) = pending.Pop();
			var path = parentPath.Length == 0 ? go.Name : $"{parentPath}/{go.Name}";

			var match = (string.IsNullOrWhiteSpace( name ) || (go.Name?.Contains( name, StringComparison.OrdinalIgnoreCase ) ?? false))
				&& (string.IsNullOrWhiteSpace( tag ) || go.Tags.Has( tag ))
				&& (string.IsNullOrWhiteSpace( component ) || go.Components.GetAll().Any( x => x.GetType().Name.Contains( component, StringComparison.OrdinalIgnoreCase ) ));

			if ( match )
			{
				total++;

				if ( results.Count < limit )
				{
					results.Add( new FoundObject
					{
						Name = go.Name,
						Id = go.Id,
						Path = path,
						Enabled = go.Enabled,
						Components = go.Components.GetAll().Select( x => x.GetType().Name ).ToArray()
					} );
				}
			}

			foreach ( var child in go.Children.AsEnumerable().Reverse() )
			{
				pending.Push( (child, path) );
			}
		}

		return new FoundObjects { Total = total, Results = results.ToArray(), Truncated = pending.Count > 0 };
	}

	/// <summary>
	/// The editor scene camera's transform - where the scene viewport is looking from. Position, angles
	/// (pitch yaw roll) and field of view.
	/// </summary>
	[McpTool.ReadOnly( "get_editor_camera" )]
	public static CameraState GetEditorCamera()
	{
		var camera = Application.Editor?.Camera;

		if ( !camera.IsValid() )
			throw new Exception( "No editor scene camera - is a scene viewport open?" );

		return new CameraState
		{
			Position = camera.WorldPosition,
			Angles = camera.WorldRotation.Angles(),
			FieldOfView = camera.FieldOfView
		};
	}

	/// <summary>
	/// Move the editor scene camera. Give a position and/or angles - anything left empty keeps its current
	/// value. Returns the resulting transform.
	/// </summary>
	/// <param name="position">World position as 'x,y,z'.</param>
	/// <param name="angles">View angles as 'pitch,yaw,roll'.</param>
	[McpTool( "set_editor_camera" )]
	public static object SetEditorCamera( string position = "", string angles = "" )
	{
		var viewport = SceneViewWidget.Current?.LastSelectedViewportWidget
			?? throw new Exception( "No scene viewport open" );

		var state = viewport.State;

		if ( !string.IsNullOrWhiteSpace( position ) )
			state.CameraPosition = Vector3.Parse( position );

		if ( !string.IsNullOrWhiteSpace( angles ) )
			state.CameraRotation = Rotation.From( Angles.Parse( angles ) );

		// the viewport copies this state onto the camera component every frame - apply it
		// now too so a screenshot batched in the same call sees the move
		var camera = Application.Editor?.Camera;

		if ( camera.IsValid() )
		{
			camera.WorldPosition = state.CameraPosition;
			camera.WorldRotation = state.CameraRotation;
		}

		return new CameraState
		{
			Position = state.CameraPosition,
			Angles = state.CameraRotation.Angles(),
			FieldOfView = camera.IsValid() ? camera.FieldOfView : 0
		};
	}

	/// <summary>
	/// Render what the editor scene camera sees and return it as an image - see the scene the way the
	/// viewport does. Move the view first with set_editor_camera.
	/// </summary>
	/// <param name="width">Image width in pixels.</param>
	/// <param name="height">Image height in pixels.</param>
	[McpTool.ReadOnly( "editor_camera_screenshot" )]
	public static object EditorCameraScreenshot( [Sandbox.Range( 16, 4096 )] int width = 1280, [Sandbox.Range( 16, 4096 )] int height = 720 )
	{
		var camera = Application.Editor?.Camera;

		if ( !camera.IsValid() )
			throw new Exception( "No editor scene camera - is a scene viewport open?" );

		var bitmap = new Bitmap( width, height );
		camera.RenderToBitmap( bitmap, false );

		return bitmap;
	}

	/// <summary>
	/// Create a game object in the active scene - empty or from a prefab, optionally with components.
	/// For putting a model in the scene, spawn_model is quicker. The user can undo this. Returns the
	/// new object's id for the other scene tools.
	/// </summary>
	/// <param name="name">Name for the new object. Empty picks a sensible default.</param>
	/// <param name="parent">Parent game object id. Empty parents to the scene root.</param>
	/// <param name="prefab">Prefab asset path to instantiate, from asset_search.</param>
	/// <param name="position">World position as 'x,y,z'. Default origin.</param>
	/// <param name="angles">Rotation as 'pitch,yaw,roll'.</param>
	/// <param name="scale">Scale as 'x,y,z', or a single number for uniform.</param>
	/// <param name="components">Comma separated component type names to add, e.g. 'ModelRenderer,BoxCollider'.</param>
	[McpTool( "create_game_object" )]
	public static CreatedObject CreateGameObject( string name = "", string parent = "", string prefab = "",
		string position = "", string angles = "", string scale = "", string components = "" )
	{
		var session = ActiveSession();
		var parentGo = string.IsNullOrWhiteSpace( parent ) ? null : FindByGuid( parent );

		var transform = new Transform(
			string.IsNullOrWhiteSpace( position ) ? Vector3.Zero : Vector3.Parse( position ),
			string.IsNullOrWhiteSpace( angles ) ? Rotation.Identity : Rotation.From( Angles.Parse( angles ) ) );

		using var sceneScope = session.Scene.Push();
		using var undo = session.UndoScope( "Create GameObject" ).WithGameObjectCreations().Push();

		GameObject go;

		if ( !string.IsNullOrWhiteSpace( prefab ) )
		{
			go = GameObject.Clone( prefab, transform, parentGo, name: string.IsNullOrWhiteSpace( name ) ? null : name )
				?? throw new Exception( $"Couldn't instantiate prefab '{prefab}' - asset_search finds prefabs" );
		}
		else
		{
			go = new GameObject( true, string.IsNullOrWhiteSpace( name ) ? "GameObject" : name );

			if ( parentGo is not null )
				go.SetParent( parentGo, true );

			go.WorldPosition = transform.Position;
			go.WorldRotation = transform.Rotation;
		}

		ApplyTransform( go, "", "", scale );

		var added = new List<ComponentRef>();

		foreach ( var typeName in components.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
		{
			var component = go.Components.Create( ComponentTools.FindComponentType( typeName ), true );
			added.Add( new ComponentRef { Id = component.Id, Type = component.GetType().Name, GameObject = go.Name } );
		}

		return new CreatedObject { Id = go.Id, Name = go.Name, Position = go.WorldPosition, Components = added.ToArray() };
	}

	/// <summary>
	/// Create a game object with a ModelRenderer in one call - the fastest way to put a model in the
	/// scene. Optionally tint it, override its material, scale it and parent it. The user can undo
	/// this. Returns the new object's id.
	/// </summary>
	/// <param name="model">Model asset path, e.g. 'models/dev/box.vmdl'.</param>
	/// <param name="name">The new object's name. Empty uses the model filename.</param>
	/// <param name="position">World position as 'x,y,z'.</param>
	/// <param name="angles">Rotation as 'pitch,yaw,roll'.</param>
	/// <param name="scale">Scale as 'x,y,z', or a single number for uniform.</param>
	/// <param name="tint">Tint color as 'r,g,b,a' (0-1 floats) or '#rrggbb'.</param>
	/// <param name="material">Material asset path to override every surface with.</param>
	/// <param name="parent">Parent game object id. Empty parents to the scene root.</param>
	[McpTool( "spawn_model" )]
	public static ObjectRef SpawnModel( string model, string name = "", string position = "", string angles = "",
		string scale = "", string tint = "", string material = "", string parent = "" )
	{
		var session = ActiveSession();

		using var sceneScope = session.Scene.Push();
		using var undo = session.UndoScope( "Spawn Model" ).WithGameObjectCreations().Push();

		var go = SpawnModelInternal( session.Scene, model, name, position, angles, scale, tint, material, parent );

		return new ObjectRef { Id = go.Id, Name = go.Name };
	}

	/// <summary>
	/// Create many model game objects in one call - each item takes the same fields spawn_model
	/// does. Use this to build big arrangements without a round trip per object. One undo step
	/// covers the whole batch. Returns the created ids.
	/// </summary>
	/// <param name="items">The models to spawn.</param>
	[McpTool( "spawn_models" )]
	public static SpawnedModels SpawnModels( SpawnItem[] items )
	{
		if ( items is null || items.Length == 0 )
			throw new Exception( "Give at least one item like {\"model\": \"models/dev/box.vmdl\", \"position\": \"0,0,0\"}" );

		var session = ActiveSession();

		using var sceneScope = session.Scene.Push();
		using var undo = session.UndoScope( $"Spawn {items.Length} Models" ).WithGameObjectCreations().Push();

		var created = new List<ObjectRef>();

		foreach ( var item in items )
		{
			if ( item is null )
				throw new Exception( "Items can't be null" );

			var go = SpawnModelInternal( session.Scene, item.Model, item.Name, item.Position,
				item.Angles, item.Scale, item.Tint, item.Material, item.Parent );

			created.Add( new ObjectRef { Id = go.Id, Name = go.Name } );
		}

		return new SpawnedModels { Count = created.Count, Created = created.ToArray() };
	}

	/// <summary>
	/// One model to spawn - the same fields spawn_model takes.
	/// </summary>
	public class SpawnItem
	{
		/// <summary>Model asset path, e.g. 'models/dev/box.vmdl'.</summary>
		public required string Model { get; set; }

		/// <summary>The new object's name. Empty uses the model filename.</summary>
		public string Name { get; set; } = "";

		/// <summary>World position as 'x,y,z'.</summary>
		public string Position { get; set; } = "";

		/// <summary>Rotation as 'pitch,yaw,roll'.</summary>
		public string Angles { get; set; } = "";

		/// <summary>Scale as 'x,y,z', or a single number for uniform.</summary>
		public string Scale { get; set; } = "";

		/// <summary>Tint color as 'r,g,b,a' (0-1 floats) or '#rrggbb'.</summary>
		public string Tint { get; set; } = "";

		/// <summary>Material asset path to override every surface with.</summary>
		public string Material { get; set; } = "";

		/// <summary>Parent game object id. Empty parents to the scene root.</summary>
		public string Parent { get; set; } = "";
	}

	/// <summary>
	/// Delete a game object and its children. The user can undo this.
	/// </summary>
	/// <param name="id">The game object's id, from scene_tree or find_game_objects.</param>
	[McpTool( "delete_game_object" )]
	public static object DeleteGameObject( string id )
	{
		var go = FindByGuid( id );
		var session = ActiveSession();

		using ( session.UndoScope( "Delete GameObject" ).WithGameObjectDestructions( go ).Push() )
		{
			go.DestroyImmediate();
		}

		return $"Deleted '{go.Name}'";
	}

	/// <summary>
	/// Change a game object - rename, enable/disable, reparent, move, rotate or scale. Only the
	/// arguments you give change; everything else stays. The user can undo this.
	/// </summary>
	/// <param name="id">The game object's id, from scene_tree or find_game_objects.</param>
	/// <param name="name">New name.</param>
	/// <param name="enabled">Enable or disable the object.</param>
	/// <param name="parent">New parent game object id, or 'root' for the scene root.</param>
	/// <param name="position">World position as 'x,y,z'.</param>
	/// <param name="angles">Rotation as 'pitch,yaw,roll'.</param>
	/// <param name="scale">Scale as 'x,y,z', or a single number for uniform.</param>
	[McpTool( "set_game_object" )]
	public static GameObjectState SetGameObject( string id, string name = "", bool? enabled = null, string parent = "",
		string position = "", string angles = "", string scale = "" )
	{
		var go = FindByGuid( id );
		var session = ActiveSession();

		var newParent = parent switch
		{
			"" => null,
			"root" => go.Scene as GameObject,
			_ => FindByGuid( parent )
		};

		using ( session.UndoScope( "Edit GameObject" ).WithGameObjectChanges( go, GameObjectUndoFlags.All ).Push() )
		{
			if ( !string.IsNullOrWhiteSpace( name ) )
				go.Name = name;

			if ( enabled is not null )
				go.Enabled = enabled.Value;

			if ( newParent is not null )
				go.SetParent( newParent, true );

			ApplyTransform( go, position, angles, scale );
		}

		return new GameObjectState
		{
			Id = go.Id,
			Name = go.Name,
			Enabled = go.Enabled,
			Parent = go.Parent is null || go.Parent is Scene ? null : new ObjectRef { Id = go.Parent.Id, Name = go.Parent.Name },
			Position = go.WorldPosition,
			Angles = go.WorldRotation.Angles(),
			Scale = go.WorldScale
		};
	}

	/// <summary>
	/// Add a component to a game object, optionally setting its properties in the same call. The
	/// user can undo this. Returns the component's id.
	/// </summary>
	/// <param name="id">The game object's id, from scene_tree or find_game_objects.</param>
	/// <param name="type">The component type name, e.g. 'ModelRenderer'. get_component_type documents what exists.</param>
	/// <param name="properties">Optional json object of property values, e.g. {"LightColor": "1,0.8,0.6,1", "Radius": 512}.</param>
	[McpTool( "add_component" )]
	public static ComponentRef AddComponent( string id, string type, JsonObject properties = null )
	{
		var go = FindByGuid( id );
		var td = ComponentTools.FindComponentType( type );
		var session = ActiveSession();

		using ( session.UndoScope( $"Add {td.Name}" ).WithComponentCreations().Push() )
		{
			var component = go.Components.Create( td, true );

			if ( properties is { Count: > 0 } )
				ApplyComponentProperties( component, properties );

			return new ComponentRef { Id = component.Id, Type = td.Name, GameObject = go.Name };
		}
	}

	/// <summary>
	/// Remove a component. Give the component's id, or its game object's id plus a type name. The
	/// user can undo this. Removal applies at the end of the frame, so reading the object in the
	/// same batch still shows it.
	/// </summary>
	/// <param name="id">The component's id, or its game object's id.</param>
	/// <param name="type">The component type to remove, when id is a game object.</param>
	[McpTool( "remove_component" )]
	public static object RemoveComponent( string id, string type = "" )
	{
		var component = ResolveComponent( id, type );
		var session = ActiveSession();

		using ( session.UndoScope( $"Remove {component.GetType().Name}" ).WithComponentDestructions( component ).Push() )
		{
			component.Destroy();
		}

		return $"Removed {component.GetType().Name} from '{component.GameObject?.Name}'";
	}

	/// <summary>
	/// Set properties on a component. Give the component's id, or its game object's id plus a type
	/// name. Values use the same serialized forms get_game_object shows - strings, numbers, 'x,y,z'
	/// vectors, colors as 'r,g,b,a', asset paths as strings. Everything validates before anything
	/// applies, and the user can undo it.
	/// </summary>
	/// <param name="id">The component's id, or its game object's id.</param>
	/// <param name="properties">A json object of property name to new value.</param>
	/// <param name="type">The component type to change, when id is a game object.</param>
	[McpTool( "set_component" )]
	public static ComponentEdit SetComponent( string id, JsonObject properties, string type = "" )
	{
		if ( properties is null || properties.Count == 0 )
			throw new Exception( "Give properties as a json object of name to value" );

		var component = ResolveComponent( id, type );
		var session = ActiveSession();

		using ( session.UndoScope( $"Edit {component.GetType().Name}" ).WithComponentChanges( component ).Push() )
		{
			var applied = ApplyComponentProperties( component, properties );

			return new ComponentEdit { Id = component.Id, Type = component.GetType().Name, Applied = applied };
		}
	}

	/// <summary>
	/// What's selected in the editor - the game objects and components the user (or you) has picked.
	/// </summary>
	[McpTool.ReadOnly( "get_selection" )]
	public static SelectionState GetSelection()
	{
		var selection = ActiveSession().Selection;

		return new SelectionState
		{
			GameObjects = selection.OfType<GameObject>().Select( x => new ObjectRef { Id = x.Id, Name = x.Name } ).ToArray(),
			Components = selection.OfType<Component>().Select( x => new ComponentRef { Id = x.Id, Type = x.GetType().Name, GameObject = x.GameObject?.Name } ).ToArray()
		};
	}

	/// <summary>
	/// Replace the editor selection. Selecting something highlights it for the user - useful for
	/// showing them what you're talking about. An empty list clears the selection.
	/// </summary>
	/// <param name="ids">Game object or component ids to select.</param>
	[McpTool( "set_selection" )]
	public static object SetSelection( string[] ids )
	{
		var session = ActiveSession();

		// Resolve everything first so a bad id doesn't half change the selection
		var targets = (ids ?? []).Select( x => Guid.TryParse( x, out var guid )
			? (object)session.Scene?.Directory?.FindByGuid( guid ) ?? session.Scene?.Directory?.FindComponentByGuid( guid )
				?? throw new Exception( $"Nothing in the scene has id {guid}" )
			: throw new Exception( $"'{x}' isn't a guid" ) ).ToArray();

		using ( session.UndoScope( "Select" ).Push() )
		{
			session.Selection.Clear();

			foreach ( var target in targets )
			{
				session.Selection.Add( target );
			}
		}

		return $"Selected {targets.Length} things";
	}

	/// <summary>
	/// Save the active scene to its file.
	/// </summary>
	[McpTool( "save_scene" )]
	public static SavedScene SaveScene()
	{
		var session = ActiveSession();

		if ( session.Scene?.Source is null )
			throw new Exception( "This scene has never been saved so it has no path - the user needs to save it in the editor first" );

		session.Save( false );

		return new SavedScene { Saved = session.Scene.Source.ResourcePath, HasUnsavedChanges = session.HasUnsavedChanges };
	}

	/// <summary>
	/// Undo the last edit in the active scene, exactly like the user pressing ctrl+z. Use it to back
	/// out of your own mistake.
	/// </summary>
	[McpTool( "undo" )]
	public static object Undo()
	{
		return ActiveSession().UndoSystem.Undo() ? "Undone" : "Nothing to undo";
	}

	/// <summary>
	/// Redo the last undone edit in the active scene.
	/// </summary>
	[McpTool( "redo" )]
	public static object Redo()
	{
		return ActiveSession().UndoSystem.Redo() ? "Redone" : "Nothing to redo";
	}

	/// <summary>
	/// Render a camera in the scene and return it as an image. Give any CameraComponent's id or its
	/// game object's id, or nothing for the scene's main camera. Works in edit and play mode -
	/// editor_camera_screenshot shows the editor viewport instead.
	/// </summary>
	/// <param name="camera">A CameraComponent id or its game object's id. Empty uses the scene's main camera.</param>
	/// <param name="width">Image width in pixels.</param>
	/// <param name="height">Image height in pixels.</param>
	/// <param name="includeUi">Include any UI the camera renders.</param>
	[McpTool.ReadOnly( "camera_screenshot" )]
	public static object CameraScreenshot( string camera = "", [Sandbox.Range( 16, 4096 )] int width = 1280,
		[Sandbox.Range( 16, 4096 )] int height = 720, bool includeUi = true )
	{
		var target = string.IsNullOrWhiteSpace( camera )
			? ResolveScene( "" ).Camera
			: ResolveComponent( camera, "CameraComponent" ) as CameraComponent
				?? throw new Exception( "That component isn't a camera - give a CameraComponent or its game object" );

		if ( !target.IsValid() )
			throw new Exception( "The scene has no camera - find one with find_game_objects component 'Camera', or add one" );

		var bitmap = new Bitmap( width, height );
		target.RenderToBitmap( bitmap, includeUi );

		return bitmap;
	}

	/// <summary>
	/// Trace a ray through the active scene's physics and report what it hits - find the floor,
	/// check line of sight, probe where something can be placed.
	/// </summary>
	/// <param name="from">Ray start as 'x,y,z'.</param>
	/// <param name="to">Ray end as 'x,y,z'.</param>
	[McpTool.ReadOnly( "scene_trace" )]
	public static TraceHit SceneTrace( string from, string to )
	{
		var scene = ResolveScene( "" );

		var result = scene.Trace.Ray( Vector3.Parse( from ), Vector3.Parse( to ) ).Run();

		return new TraceHit
		{
			Hit = result.Hit,
			StartedSolid = result.StartedSolid,
			EndPosition = result.EndPosition,
			Normal = result.Normal,
			Fraction = result.Fraction,
			Distance = (result.EndPosition - result.StartPosition).Length,
			GameObject = result.GameObject is null ? null : new ObjectRef { Id = result.GameObject.Id, Name = result.GameObject.Name },
			Component = result.Component?.GetType().Name
		};
	}

	/// <summary>
	/// What a scene_trace found.
	/// </summary>
	public class TraceHit
	{
		/// <summary>Whether the trace hit anything.</summary>
		public bool Hit { get; set; }

		/// <summary>Whether the trace started inside something solid.</summary>
		public bool StartedSolid { get; set; }

		/// <summary>Where the trace ended - the hit point, or the requested end on a miss.</summary>
		public Vector3 EndPosition { get; set; }

		/// <summary>The hit surface normal.</summary>
		public Vector3 Normal { get; set; }

		/// <summary>How far along the ray the hit was, 0 to 1.</summary>
		public float Fraction { get; set; }

		/// <summary>How far the trace travelled, in units.</summary>
		public float Distance { get; set; }

		/// <summary>The game object hit. Null on a miss.</summary>
		public ObjectRef GameObject { get; set; }

		/// <summary>The component type hit. Null on a miss.</summary>
		public string Component { get; set; }
	}

	/// <summary>
	/// A game object's id and name - enough to find it with the other scene tools.
	/// </summary>
	public class ObjectRef
	{
		public Guid Id { get; set; }

		public string Name { get; set; }
	}

	/// <summary>
	/// A component, its type and the game object holding it.
	/// </summary>
	public class ComponentRef
	{
		public Guid Id { get; set; }

		public string Type { get; set; }

		public string GameObject { get; set; }
	}

	/// <summary>What create_game_object made.</summary>
	public class CreatedObject
	{
		public Guid Id { get; set; }
		public string Name { get; set; }
		public Vector3 Position { get; set; }
		public ComponentRef[] Components { get; set; }
	}

	/// <summary>What spawn_models made.</summary>
	public class SpawnedModels
	{
		public int Count { get; set; }
		public ObjectRef[] Created { get; set; }
	}

	/// <summary>A game object's state after a change.</summary>
	public class GameObjectState
	{
		public Guid Id { get; set; }
		public string Name { get; set; }
		public bool Enabled { get; set; }
		/// <summary>The parent. Null when parented to the scene root.</summary>
		public ObjectRef Parent { get; set; }
		public Vector3 Position { get; set; }
		public Angles Angles { get; set; }
		public Vector3 Scale { get; set; }
	}

	/// <summary>What set_component changed.</summary>
	public class ComponentEdit
	{
		public Guid Id { get; set; }
		public string Type { get; set; }
		public string[] Applied { get; set; }
	}

	/// <summary>What's selected in the editor.</summary>
	public class SelectionState
	{
		public ObjectRef[] GameObjects { get; set; }
		public ComponentRef[] Components { get; set; }
	}

	/// <summary>Where the scene was saved.</summary>
	public class SavedScene
	{
		public string Saved { get; set; }
		public bool HasUnsavedChanges { get; set; }
	}

	/// <summary>The scenes open in the editor.</summary>
	public class OpenSceneList
	{
		public OpenScene[] Scenes { get; set; }
	}

	/// <summary>One open scene tab.</summary>
	public class OpenScene
	{
		public string Name { get; set; }
		public string ResourcePath { get; set; }
		/// <summary>Scene, Prefab, or Game for the running session.</summary>
		public string Type { get; set; }
		public bool IsActive { get; set; }
		public bool HasUnsavedChanges { get; set; }
		public int RootObjectCount { get; set; }
	}

	/// <summary>A scene's game object hierarchy.</summary>
	public class SceneTreeResult
	{
		public string Scene { get; set; }
		public string ResourcePath { get; set; }
		public SceneTreeNode[] Objects { get; set; }
	}

	/// <summary>One game object in the hierarchy.</summary>
	public class SceneTreeNode
	{
		public string Name { get; set; }
		public Guid Id { get; set; }
		public bool Enabled { get; set; }
		public string[] Components { get; set; }
		/// <summary>Child objects. Null when the depth limit or node budget cut them off.</summary>
		public SceneTreeNode[] Children { get; set; }
		/// <summary>How many children weren't shown.</summary>
		public int ChildrenNotShown { get; set; }
	}

	/// <summary>Everything about one game object.</summary>
	public class GameObjectDetails
	{
		public string Scene { get; set; }
		public string Name { get; set; }
		public Guid Id { get; set; }
		public bool Enabled { get; set; }
		public bool ActiveInHierarchy { get; set; }
		public string[] Tags { get; set; }
		/// <summary>The parent. Null when parented to the scene root.</summary>
		public ObjectRef Parent { get; set; }
		public Vector3 WorldPosition { get; set; }
		public Angles WorldRotation { get; set; }
		public Vector3 WorldScale { get; set; }
		public ComponentInfo[] Components { get; set; }
		public ChildObject[] Children { get; set; }
	}

	/// <summary>One component on a game object.</summary>
	public class ComponentInfo
	{
		public string Type { get; set; }
		public Guid Id { get; set; }
		public bool Enabled { get; set; }
		/// <summary>The serialized properties. Null unless includeComponentProperties was set.</summary>
		public JsonNode Properties { get; set; }
	}

	/// <summary>A child game object.</summary>
	public class ChildObject
	{
		public Guid Id { get; set; }
		public string Name { get; set; }
		public bool Enabled { get; set; }
	}

	/// <summary>What find_game_objects matched.</summary>
	public class FoundObjects
	{
		/// <summary>How many matched in total, beyond what's shown.</summary>
		public int Total { get; set; }
		public FoundObject[] Results { get; set; }
		/// <summary>True when the scene was too big to finish searching.</summary>
		public bool Truncated { get; set; }
	}

	/// <summary>One matched game object.</summary>
	public class FoundObject
	{
		public string Name { get; set; }
		public Guid Id { get; set; }
		/// <summary>The object's place in the hierarchy, like 'Map/Props/Barrel'.</summary>
		public string Path { get; set; }
		public bool Enabled { get; set; }
		public string[] Components { get; set; }
	}

	/// <summary>The editor camera's transform.</summary>
	public class CameraState
	{
		public Vector3 Position { get; set; }
		public Angles Angles { get; set; }
		public float FieldOfView { get; set; }
	}

	private static SceneEditorSession ActiveSession()
	{
		return SceneEditorSession.Active ?? throw new Exception( "No scene is open in the editor" );
	}

	/// <summary>
	/// A component from either its own id, or its game object's id plus a type name.
	/// </summary>
	private static Component ResolveComponent( string id, string type )
	{
		if ( !Guid.TryParse( id, out var guid ) )
			throw new Exception( $"'{id}' isn't a guid - get_game_object shows component ids" );

		foreach ( var session in SceneEditorSession.All )
		{
			var found = session.Scene?.Directory?.FindComponentByGuid( guid );
			if ( found is not null ) return found;
		}

		var go = FindByGuid( id );

		if ( string.IsNullOrWhiteSpace( type ) )
			throw new Exception( $"'{go.Name}' is a game object - give the component's id from get_game_object, or a type name too" );

		var components = go.Components.GetAll().ToArray();

		var component = components.FirstOrDefault( x => string.Equals( x.GetType().Name, type, StringComparison.OrdinalIgnoreCase ) )
			?? components.FirstOrDefault( x => x.GetType().Name.Contains( type, StringComparison.OrdinalIgnoreCase ) );

		return component
			?? throw new Exception( $"'{go.Name}' has no {type} component - it has {string.Join( ", ", components.Select( x => x.GetType().Name ) )}" );
	}

	/// <summary>
	/// Set component properties by name via reflection, converting values through the engine's json
	/// converters. Validates everything before applying anything, so a bad name or value doesn't
	/// leave the component half edited. Returns the applied property names.
	/// </summary>
	private static string[] ApplyComponentProperties( Component component, JsonObject properties )
	{
		var td = TypeLibrary.GetType( component.GetType() );
		var pending = new List<(PropertyDescription Property, object Value)>();

		foreach ( var (key, value) in properties )
		{
			// Only the [Property] surface - the same properties the inspector edits and the
			// scene serializes, not every public setter the type happens to have
			var property = td.Properties.FirstOrDefault( x => !x.IsStatic && x.HasAttribute<Sandbox.PropertyAttribute>()
				&& string.Equals( x.Name, key, StringComparison.OrdinalIgnoreCase ) );

			if ( property is null || !property.CanWrite )
			{
				var writable = string.Join( ", ", td.Properties
					.Where( x => !x.IsStatic && x.CanWrite && x.HasAttribute<Sandbox.PropertyAttribute>() )
					.Select( x => x.Name ).Order() );

				throw new Exception( $"{td.Name} has no writable property '{key}'. Writable: {writable}" );
			}

			try
			{
				pending.Add( (property, Json.FromNode( value, property.PropertyType )) );
			}
			catch ( Exception e )
			{
				throw new Exception( $"Couldn't convert '{key}' to {property.PropertyType.Name} - {e.Message}" );
			}
		}

		foreach ( var (property, value) in pending )
		{
			property.SetValue( component, value );
		}

		return pending.Select( x => x.Property.Name ).ToArray();
	}

	/// <summary>
	/// Apply any non-empty transform arguments to a game object. Scale takes 'x,y,z' or a single
	/// number for uniform.
	/// </summary>
	private static void ApplyTransform( GameObject go, string position, string angles, string scale )
	{
		if ( !string.IsNullOrWhiteSpace( position ) )
			go.WorldPosition = Vector3.Parse( position );

		if ( !string.IsNullOrWhiteSpace( angles ) )
			go.WorldRotation = Rotation.From( Angles.Parse( angles ) );

		if ( !string.IsNullOrWhiteSpace( scale ) )
			go.WorldScale = scale.Contains( ',' ) ? Vector3.Parse( scale ) : new Vector3( scale.ToFloat() );
	}

	private static GameObject SpawnModelInternal( Scene scene, string model, string name, string position,
		string angles, string scale, string tint, string material, string parent )
	{
		if ( string.IsNullOrWhiteSpace( model ) )
			throw new Exception( "Give a model asset path, e.g. 'models/dev/box.vmdl'" );

		var loaded = Model.Load( model );

		if ( loaded is null || loaded.IsError )
			throw new Exception( $"Couldn't load model '{model}' - find models with asset_search type:vmdl" );

		var go = scene.CreateObject();
		go.Name = string.IsNullOrWhiteSpace( name ) ? System.IO.Path.GetFileNameWithoutExtension( model ) : name;

		if ( !string.IsNullOrWhiteSpace( parent ) )
			go.SetParent( FindByGuid( parent ), keepWorldPosition: false );

		ApplyTransform( go, position, angles, scale );

		var renderer = go.Components.Create<ModelRenderer>();
		renderer.Model = loaded;

		if ( !string.IsNullOrWhiteSpace( tint ) )
		{
			renderer.Tint = Color.TryParse( tint, out var color ) ? color
				: throw new Exception( $"Couldn't parse tint '{tint}' - use 'r,g,b,a' floats or '#rrggbb'" );
		}

		if ( !string.IsNullOrWhiteSpace( material ) )
		{
			renderer.MaterialOverride = Material.Load( material )
				?? throw new Exception( $"Couldn't load material '{material}' - find materials with asset_search type:vmat" );
		}

		return go;
	}

	private static Scene ResolveScene( string nameOrPath )
	{
		if ( string.IsNullOrWhiteSpace( nameOrPath ) )
		{
			return SceneEditorSession.Active?.Scene ?? Game.ActiveScene
				?? throw new Exception( "No scene is open in the editor" );
		}

		var match = SceneEditorSession.All
			.Select( x => x.Scene )
			.FirstOrDefault( x => string.Equals( x?.Name, nameOrPath, StringComparison.OrdinalIgnoreCase )
				|| string.Equals( x?.Source?.ResourcePath, nameOrPath, StringComparison.OrdinalIgnoreCase ) );

		return match ?? throw new Exception( $"No open scene matches '{nameOrPath}' - list_scenes shows what's open" );
	}

	private static GameObject FindByGuid( string id )
	{
		if ( !Guid.TryParse( id, out var guid ) )
			throw new Exception( $"'{id}' isn't a guid - scene_tree and find_game_objects show object ids" );

		foreach ( var session in SceneEditorSession.All )
		{
			var found = session.Scene?.Directory?.FindByGuid( guid );
			if ( found is not null ) return found;
		}

		throw new Exception( $"No game object {guid} in any open scene" );
	}
}
