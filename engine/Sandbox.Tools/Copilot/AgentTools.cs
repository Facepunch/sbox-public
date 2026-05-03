using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sandbox;

namespace Editor.Copilot;

/// <summary>
/// The catalog of "tools" the AI agent is allowed to invoke against the s&box
/// editor. Each tool has a JSON-Schema describing its parameters and a handler
/// that runs synchronously on the main thread and returns a JSON-friendly
/// result object.
///
/// Tools are split into two safety tiers:
///   <see cref="ToolSafety.ReadOnly"/>  — pure inspection (auto-runs).
///   <see cref="ToolSafety.Mutating"/>  — modifies the scene / disk / play-state.
///
/// The chat widget consults <see cref="CopilotPreferences.ApprovalMode"/>
/// before invoking a tool to decide whether to gate it behind a click.
/// </summary>
public static class AgentTools
{
	public enum ToolSafety { ReadOnly, Mutating }

	public class Tool
	{
		public string                                       Name        { get; init; }
		public string                                       Description { get; init; }
		public ToolSafety                                   Safety      { get; init; }
		public object                                       Schema      { get; init; }
		public Func<JsonElement, object>                    Handler     { get; init; }
	}

	// ── public registry ───────────────────────────────────────────────────────

	public static IReadOnlyList<Tool> All => _tools;
	public static Tool Find( string name ) => _tools.FirstOrDefault( t => t.Name == name );

	/// <summary>
	/// Returns the tool list shaped for the OpenAI-compatible /chat/completions endpoint.
	/// </summary>
	public static List<object> AsApiPayload()
	{
		var list = new List<object>( _tools.Count );
		foreach ( var t in _tools )
		{
			list.Add( new
			{
				type     = "function",
				function = new
				{
					name        = t.Name,
					description = t.Description,
					parameters  = t.Schema,
				}
			} );
		}
		return list;
	}

	// ── small helpers for building JSON schemas ───────────────────────────────

	private static object Obj( object props, params string[] required )
		=> new { type = "object", properties = props, required };

	private static object Str( string desc )         => new { type = "string", description = desc };
	private static object Num( string desc )         => new { type = "number", description = desc };
	private static object Bool( string desc, bool def = false )
		=> new { type = "boolean", description = desc, @default = def };
	private static object IntArr( string desc )      => new { type = "array",  items = new { type = "integer" }, description = desc };
	private static object StrArr( string desc )      => new { type = "array",  items = new { type = "string"  }, description = desc };

	// ── argument helpers ──────────────────────────────────────────────────────

	private static string GetString( JsonElement args, string name, string fallback = null )
	{
		if ( args.ValueKind == JsonValueKind.Object && args.TryGetProperty( name, out var v ) && v.ValueKind == JsonValueKind.String )
			return v.GetString();
		return fallback;
	}

	private static double GetNumber( JsonElement args, string name, double fallback = 0 )
	{
		if ( args.ValueKind == JsonValueKind.Object && args.TryGetProperty( name, out var v ) )
		{
			if ( v.ValueKind == JsonValueKind.Number ) return v.GetDouble();
			if ( v.ValueKind == JsonValueKind.String && double.TryParse( v.GetString(), out var parsed ) ) return parsed;
		}
		return fallback;
	}

	private static bool GetBool( JsonElement args, string name, bool fallback = false )
	{
		if ( args.ValueKind == JsonValueKind.Object && args.TryGetProperty( name, out var v ) && v.ValueKind is JsonValueKind.True or JsonValueKind.False )
			return v.GetBoolean();
		return fallback;
	}

	private static object Err( string message ) => new { ok = false, error = message };
	private static object Ok( object payload )  => new { ok = true, result = payload };

	// ── scene helpers ─────────────────────────────────────────────────────────

	private static Scene ActiveScene => SceneEditorSession.Active?.Scene;

	private static GameObject FindGameObject( string idOrName )
	{
		var scene = ActiveScene;
		if ( scene == null || string.IsNullOrEmpty( idOrName ) ) return null;

		// Try as Guid first
		if ( Guid.TryParse( idOrName, out var guid ) )
		{
			var byId = scene.Directory?.FindByGuid( guid );
			if ( byId != null ) return byId;
		}

		// Try by name (case-insensitive, breadth-first)
		foreach ( var obj in scene.GetAllObjects( false ) )
		{
			if ( string.Equals( obj.Name, idOrName, StringComparison.OrdinalIgnoreCase ) )
				return obj;
		}

		// Try by path "Parent/Child/Leaf"
		var parts = idOrName.Split( '/', StringSplitOptions.RemoveEmptyEntries );
		if ( parts.Length > 0 )
		{
			foreach ( var root in scene.GetAllObjects( false ) )
			{
				if ( !string.Equals( root.Name, parts[0], StringComparison.OrdinalIgnoreCase ) )
					continue;

				GameObject cur = root;
				for ( int i = 1; i < parts.Length && cur != null; i++ )
				{
					cur = cur.Children.FirstOrDefault( c => string.Equals( c.Name, parts[i], StringComparison.OrdinalIgnoreCase ) );
				}
				if ( cur != null ) return cur;
			}
		}

		return null;
	}

	private static object DescribeGameObject( GameObject go, bool detailed = false )
	{
		if ( go == null ) return null;

		var components = go.Components.GetAll().ToArray();
		var compInfo   = new List<object>( components.Length );

		foreach ( var c in components )
		{
			if ( !detailed )
			{
				compInfo.Add( new { type = c.GetType().Name, enabled = c.Enabled } );
				continue;
			}

			var props = new Dictionary<string, object>();
			foreach ( var p in c.GetType().GetProperties( BindingFlags.Public | BindingFlags.Instance ) )
			{
				if ( p.GetCustomAttribute( typeof( PropertyAttribute ) ) == null ) continue;
				try { props[p.Name] = SafeStringify( p.GetValue( c ) ); }
				catch { /* ignore */ }
			}
			compInfo.Add( new { type = c.GetType().FullName ?? c.GetType().Name, enabled = c.Enabled, properties = props } );
		}

		return new
		{
			id         = go.Id.ToString(),
			name       = go.Name,
			enabled    = go.Enabled,
			parent     = go.Parent?.Name,
			position   = new { x = go.WorldPosition.x, y = go.WorldPosition.y, z = go.WorldPosition.z },
			rotation   = new { pitch = go.WorldRotation.Pitch(), yaw = go.WorldRotation.Yaw(), roll = go.WorldRotation.Roll() },
			scale      = new { x = go.WorldScale.x, y = go.WorldScale.y, z = go.WorldScale.z },
			children   = go.Children.Select( c => new { id = c.Id.ToString(), name = c.Name } ).ToArray(),
			components = compInfo,
		};
	}

	private static object SafeStringify( object value )
	{
		if ( value == null ) return null;
		if ( value is string or bool or int or long or float or double or decimal ) return value;
		return value.ToString();
	}

	// ── the catalog ───────────────────────────────────────────────────────────

	private static readonly List<Tool> _tools = new()
	{
		new Tool
		{
			Name        = "list_scene_objects",
			Description = "List GameObjects in the active scene with their IDs, names, world positions, and the names of their components. Use this to see what's in the scene before modifying anything.",
			Safety      = ToolSafety.ReadOnly,
			Schema      = Obj( new { max_objects = new { type = "integer", description = "Cap on objects returned (default 100).", @default = 100 } } ),
			Handler     = args =>
			{
				var scene = ActiveScene;
				if ( scene == null ) return Err( "No active scene." );

				var max  = (int)GetNumber( args, "max_objects", 100 );
				var list = new List<object>();
				int count = 0;
				foreach ( var go in scene.GetAllObjects( false ) )
				{
					if ( count++ >= max ) break;
					list.Add( DescribeGameObject( go, detailed: false ) );
				}
				return Ok( new { scene = scene.Name, count = list.Count, objects = list } );
			}
		},

		new Tool
		{
			Name        = "get_gameobject",
			Description = "Get full details (transform, components, properties, children) for one GameObject by GUID, name, or '/'-separated path.",
			Safety      = ToolSafety.ReadOnly,
			Schema      = Obj(
				new { id_or_name = Str( "Guid, exact name, or 'Parent/Child' path." ) },
				"id_or_name" ),
			Handler     = args =>
			{
				var name = GetString( args, "id_or_name" );
				var go   = FindGameObject( name );
				if ( go == null ) return Err( $"GameObject '{name}' not found." );
				return Ok( DescribeGameObject( go, detailed: true ) );
			}
		},

		new Tool
		{
			Name        = "find_gameobjects",
			Description = "Find GameObjects whose name matches a substring (case-insensitive).",
			Safety      = ToolSafety.ReadOnly,
			Schema      = Obj(
				new { pattern = Str( "Substring to search for in GameObject names." ) },
				"pattern" ),
			Handler     = args =>
			{
				var scene = ActiveScene;
				if ( scene == null ) return Err( "No active scene." );

				var pat = GetString( args, "pattern", "" );
				var hits = scene.GetAllObjects( false )
					.Where( o => o.Name?.Contains( pat, StringComparison.OrdinalIgnoreCase ) == true )
					.Take( 50 )
					.Select( o => new { id = o.Id.ToString(), name = o.Name, path = BuildPath( o ) } )
					.ToArray();
				return Ok( new { matches = hits } );
			}
		},

		new Tool
		{
			Name        = "get_selected_objects",
			Description = "Get the currently-selected GameObjects in the editor scene view.",
			Safety      = ToolSafety.ReadOnly,
			Schema      = Obj( new { } ),
			Handler     = _ =>
			{
				var session = SceneEditorSession.Active;
				if ( session == null ) return Err( "No active editor session." );

				var sel = session.Selection.OfType<GameObject>()
					.Select( o => new { id = o.Id.ToString(), name = o.Name, path = BuildPath( o ) } )
					.ToArray();
				return Ok( new { selected = sel } );
			}
		},

		new Tool
		{
			Name        = "select_gameobjects",
			Description = "Replace the current selection with the supplied GameObjects (by id, name, or path).",
			Safety      = ToolSafety.Mutating,
			Schema      = Obj(
				new { ids_or_names = StrArr( "Array of GUIDs, names, or paths to select." ) },
				"ids_or_names" ),
			Handler     = args =>
			{
				var session = SceneEditorSession.Active;
				if ( session == null ) return Err( "No active editor session." );
				if ( !args.TryGetProperty( "ids_or_names", out var arr ) || arr.ValueKind != JsonValueKind.Array )
					return Err( "ids_or_names must be an array." );

				session.Selection.Clear();
				int hits = 0;
				foreach ( var elem in arr.EnumerateArray() )
				{
					var key = elem.GetString();
					var go  = FindGameObject( key );
					if ( go != null ) { session.Selection.Add( go ); hits++; }
				}
				return Ok( new { selected_count = hits } );
			}
		},

		new Tool
		{
			Name        = "create_gameobject",
			Description = "Create a new empty GameObject in the active scene at an optional position with an optional name and parent.",
			Safety      = ToolSafety.Mutating,
			Schema      = Obj( new
			{
				name      = Str( "Name for the new GameObject (default 'GameObject')." ),
				parent    = Str( "Optional parent (id, name, or path)." ),
				x         = Num( "World X position (default 0)." ),
				y         = Num( "World Y position (default 0)." ),
				z         = Num( "World Z position (default 0)." ),
			} ),
			Handler     = args =>
			{
				var scene = ActiveScene;
				if ( scene == null ) return Err( "No active scene." );

				var name   = GetString( args, "name", "GameObject" );
				var parent = GetString( args, "parent" );
				var pos    = new Vector3( (float)GetNumber( args, "x" ), (float)GetNumber( args, "y" ), (float)GetNumber( args, "z" ) );

				var go = scene.CreateObject();
				go.Name           = name;
				go.WorldPosition  = pos;

				if ( !string.IsNullOrEmpty( parent ) )
				{
					var parentGo = FindGameObject( parent );
					if ( parentGo != null ) go.Parent = parentGo;
				}

				return Ok( new { id = go.Id.ToString(), name = go.Name } );
			}
		},

		new Tool
		{
			Name        = "delete_gameobject",
			Description = "Destroy a GameObject and all its children. This cannot be undone via this tool — the user can Ctrl+Z in the editor.",
			Safety      = ToolSafety.Mutating,
			Schema      = Obj( new { id_or_name = Str( "Guid, name, or path." ) }, "id_or_name" ),
			Handler     = args =>
			{
				var go = FindGameObject( GetString( args, "id_or_name" ) );
				if ( go == null ) return Err( "GameObject not found." );
				var name = go.Name;
				go.Destroy();
				return Ok( new { destroyed = name } );
			}
		},

		new Tool
		{
			Name        = "set_transform",
			Description = "Set position, rotation (pitch/yaw/roll degrees) and/or scale of a GameObject. Omit any field to leave it unchanged. If 'relative' is true, position/rotation are added to the existing values.",
			Safety      = ToolSafety.Mutating,
			Schema      = Obj( new
			{
				id_or_name = Str( "Guid, name, or path." ),
				x          = Num( "Position X." ),
				y          = Num( "Position Y." ),
				z          = Num( "Position Z." ),
				pitch      = Num( "Rotation pitch (degrees)." ),
				yaw        = Num( "Rotation yaw (degrees)." ),
				roll       = Num( "Rotation roll (degrees)." ),
				scale_x    = Num( "Scale X." ),
				scale_y    = Num( "Scale Y." ),
				scale_z    = Num( "Scale Z." ),
				relative   = Bool( "If true, treat position/rotation as deltas." ),
			}, "id_or_name" ),
			Handler     = args =>
			{
				var go = FindGameObject( GetString( args, "id_or_name" ) );
				if ( go == null ) return Err( "GameObject not found." );

				bool rel    = GetBool( args, "relative" );
				bool hasPos = args.TryGetProperty( "x", out _ ) || args.TryGetProperty( "y", out _ ) || args.TryGetProperty( "z", out _ );
				bool hasRot = args.TryGetProperty( "pitch", out _ ) || args.TryGetProperty( "yaw", out _ ) || args.TryGetProperty( "roll", out _ );
				bool hasScl = args.TryGetProperty( "scale_x", out _ ) || args.TryGetProperty( "scale_y", out _ ) || args.TryGetProperty( "scale_z", out _ );

				if ( hasPos )
				{
					var p = go.WorldPosition;
					var nx = (float)GetNumber( args, "x", p.x );
					var ny = (float)GetNumber( args, "y", p.y );
					var nz = (float)GetNumber( args, "z", p.z );
					go.WorldPosition = rel ? p + new Vector3( nx, ny, nz ) : new Vector3( nx, ny, nz );
				}
				if ( hasRot )
				{
					var r = go.WorldRotation;
					var pitch = (float)GetNumber( args, "pitch", r.Pitch() );
					var yaw   = (float)GetNumber( args, "yaw",   r.Yaw()   );
					var roll  = (float)GetNumber( args, "roll",  r.Roll()  );
					var nr    = Rotation.From( pitch, yaw, roll );
					go.WorldRotation = rel ? r * nr : nr;
				}
				if ( hasScl )
				{
					var s = go.WorldScale;
					go.WorldScale = new Vector3(
						(float)GetNumber( args, "scale_x", s.x ),
						(float)GetNumber( args, "scale_y", s.y ),
						(float)GetNumber( args, "scale_z", s.z ) );
				}

				return Ok( DescribeGameObject( go ) );
			}
		},

		new Tool
		{
			Name        = "add_component",
			Description = "Add a component to a GameObject by type name. Type can be the simple class name (e.g. 'ModelRenderer') or fully-qualified (e.g. 'Sandbox.ModelRenderer').",
			Safety      = ToolSafety.Mutating,
			Schema      = Obj( new
			{
				id_or_name      = Str( "Target GameObject (id, name, or path)." ),
				component_type  = Str( "Component type name." ),
			}, "id_or_name", "component_type" ),
			Handler     = args =>
			{
				var go = FindGameObject( GetString( args, "id_or_name" ) );
				if ( go == null ) return Err( "GameObject not found." );

				var typeName = GetString( args, "component_type", "" );
				var type     = ResolveComponentType( typeName );
				if ( type == null ) return Err( $"Component type '{typeName}' not found." );

				var addMethod = typeof( GameObject.ComponentList )
					.GetMethods()
					.FirstOrDefault( m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetParameters().Length <= 1 );
				if ( addMethod == null ) return Err( "Could not locate Components.Create<T>() via reflection." );

				var generic = addMethod.MakeGenericMethod( type );
				var args0   = addMethod.GetParameters().Length == 0 ? null : new object[] { true };
				var comp    = generic.Invoke( go.Components, args0 ) as Component;

				return comp == null ? Err( "Failed to add component." ) : Ok( new { type = type.FullName, enabled = comp.Enabled } );
			}
		},

		new Tool
		{
			Name        = "set_component_property",
			Description = "Set a [Property] on a component via reflection. Value is a JSON-encoded scalar (number/string/bool) or vector { x, y, z } object.",
			Safety      = ToolSafety.Mutating,
			Schema      = Obj( new
			{
				id_or_name      = Str( "Target GameObject." ),
				component_type  = Str( "Component type name." ),
				property        = Str( "Property name." ),
				value           = new { description = "New value (scalar or { x, y, z })." }
			}, "id_or_name", "component_type", "property" ),
			Handler     = args =>
			{
				var go = FindGameObject( GetString( args, "id_or_name" ) );
				if ( go == null ) return Err( "GameObject not found." );

				var typeName = GetString( args, "component_type", "" );
				var comp     = go.Components.GetAll().FirstOrDefault( c =>
					c.GetType().Name == typeName || c.GetType().FullName == typeName );
				if ( comp == null ) return Err( $"Component '{typeName}' not on this GameObject." );

				var propName = GetString( args, "property", "" );
				var prop     = comp.GetType().GetProperty( propName, BindingFlags.Public | BindingFlags.Instance );
				if ( prop == null || !prop.CanWrite ) return Err( $"Property '{propName}' is not settable." );

				if ( !args.TryGetProperty( "value", out var jv ) ) return Err( "Missing value." );

				try
				{
					object converted = ConvertJsonToProperty( jv, prop.PropertyType );
					prop.SetValue( comp, converted );
					return Ok( new { property = propName, new_value = SafeStringify( prop.GetValue( comp ) ) } );
				}
				catch ( Exception ex )
				{
					return Err( $"Could not set property: {ex.Message}" );
				}
			}
		},

		new Tool
		{
			Name        = "instantiate_prefab",
			Description = "Instantiate a prefab from the project at an optional position. Path is relative to the project root and must end in .prefab.",
			Safety      = ToolSafety.Mutating,
			Schema      = Obj( new
			{
				prefab_path = Str( "Project-relative path to a .prefab file." ),
				x           = Num( "World X (default 0)." ),
				y           = Num( "World Y (default 0)." ),
				z           = Num( "World Z (default 0)." ),
			}, "prefab_path" ),
			Handler     = args =>
			{
				var scene = ActiveScene;
				if ( scene == null ) return Err( "No active scene." );

				var path = GetString( args, "prefab_path", "" );
				var pos  = new Vector3( (float)GetNumber( args, "x" ), (float)GetNumber( args, "y" ), (float)GetNumber( args, "z" ) );

				try
				{
					var prefab = ResourceLibrary.Get<PrefabFile>( path );
					if ( prefab == null ) return Err( $"Prefab '{path}' not found." );
					var go = SceneUtility.GetPrefabScene( prefab ).Clone( pos );
					go.SetParent( scene, true );
					return Ok( new { id = go.Id.ToString(), name = go.Name } );
				}
				catch ( Exception ex )
				{
					return Err( $"Failed to instantiate: {ex.Message}" );
				}
			}
		},

		new Tool
		{
			Name        = "save_scene",
			Description = "Save the active scene to disk.",
			Safety      = ToolSafety.Mutating,
			Schema      = Obj( new { } ),
			Handler     = _ =>
			{
				var session = SceneEditorSession.Active;
				if ( session == null ) return Err( "No active editor session." );
				try { session.Save(); return Ok( new { saved = session.Scene?.Name } ); }
				catch ( Exception ex ) { return Err( ex.Message ); }
			}
		},

		new Tool
		{
			Name        = "log_to_console",
			Description = "Write a message to the editor's Log output (Info, Warning, or Error).",
			Safety      = ToolSafety.ReadOnly,
			Schema      = Obj( new
			{
				message = Str( "Text to log." ),
				level   = Str( "info | warning | error (default info)." )
			}, "message" ),
			Handler     = args =>
			{
				var msg   = GetString( args, "message", "" );
				var level = GetString( args, "level", "info" )?.ToLowerInvariant();
				switch ( level )
				{
					case "warning": Log.Warning( "[Copilot] " + msg ); break;
					case "error":   Log.Error  ( "[Copilot] " + msg ); break;
					default:        Log.Info   ( "[Copilot] " + msg ); break;
				}
				return Ok( new { logged = true } );
			}
		},

		new Tool
		{
			Name        = "read_project_file",
			Description = "Read the contents of a project file. Path is relative to the project root.",
			Safety      = ToolSafety.ReadOnly,
			Schema      = Obj( new { path = Str( "Project-relative file path." ) }, "path" ),
			Handler     = args =>
			{
				var path = GetString( args, "path", "" );
				try
				{
					if ( Sandbox.FileSystem.Mounted?.FileExists( path ) == true )
						return Ok( new { path, content = Sandbox.FileSystem.Mounted.ReadAllText( path ) } );
					if ( Sandbox.FileSystem.Root?.FileExists( path ) == true )
						return Ok( new { path, content = Sandbox.FileSystem.Root.ReadAllText( path ) } );
					if ( File.Exists( path ) )
						return Ok( new { path, content = File.ReadAllText( path ) } );
					return Err( $"File not found: {path}" );
				}
				catch ( Exception ex ) { return Err( ex.Message ); }
			}
		},

		new Tool
		{
			Name        = "write_project_file",
			Description = "Write text content to a project file (creates or overwrites). Path is relative to the project root.",
			Safety      = ToolSafety.Mutating,
			Schema      = Obj( new
			{
				path    = Str( "Project-relative file path." ),
				content = Str( "Text content to write." ),
			}, "path", "content" ),
			Handler     = args =>
			{
				var path    = GetString( args, "path", "" );
				var content = GetString( args, "content", "" );
				try
				{
					var fs = Sandbox.FileSystem.Mounted ?? Sandbox.FileSystem.Root;
					if ( fs == null ) return Err( "No writable filesystem." );
					var dir = Path.GetDirectoryName( path );
					if ( !string.IsNullOrEmpty( dir ) ) fs.CreateDirectory( dir );
					fs.WriteAllText( path, content );
					return Ok( new { wrote = path, bytes = content.Length } );
				}
				catch ( Exception ex ) { return Err( ex.Message ); }
			}
		},

		new Tool
		{
			Name        = "list_project_directory",
			Description = "List files and subdirectories at a project-relative path.",
			Safety      = ToolSafety.ReadOnly,
			Schema      = Obj( new
			{
				path    = Str( "Project-relative path (default '.')." ),
				pattern = Str( "Optional glob pattern (e.g. '*.cs')." ),
			} ),
			Handler     = args =>
			{
				var path = GetString( args, "path", "" );
				var pat  = GetString( args, "pattern", "*" );
				try
				{
					var fs = Sandbox.FileSystem.Mounted ?? Sandbox.FileSystem.Root;
					if ( fs == null ) return Err( "Filesystem unavailable." );
					return Ok( new
					{
						path,
						directories = fs.FindDirectory( path, "*",  false ).ToArray(),
						files       = fs.FindFile     ( path, pat,  false ).ToArray(),
					} );
				}
				catch ( Exception ex ) { return Err( ex.Message ); }
			}
		},

		new Tool
		{
			Name        = "list_component_types",
			Description = "Return all Component types available in the loaded assemblies (filtered by an optional substring). Useful before calling add_component.",
			Safety      = ToolSafety.ReadOnly,
			Schema      = Obj( new { filter = Str( "Optional substring filter on type name." ) } ),
			Handler     = args =>
			{
				var filter = GetString( args, "filter", "" );
				var hits = new List<string>();
				foreach ( var asm in AppDomain.CurrentDomain.GetAssemblies() )
				{
					Type[] types;
					try   { types = asm.GetTypes(); }
					catch { continue; }
					foreach ( var t in types )
					{
						if ( !typeof( Component ).IsAssignableFrom( t ) || t.IsAbstract ) continue;
						if ( !string.IsNullOrEmpty( filter ) && !t.Name.Contains( filter, StringComparison.OrdinalIgnoreCase ) ) continue;
						hits.Add( t.FullName ?? t.Name );
						if ( hits.Count >= 200 ) break;
					}
					if ( hits.Count >= 200 ) break;
				}
				return Ok( new { types = hits.ToArray() } );
			}
		},

		new Tool
		{
			Name        = "play_scene",
			Description = "Enter play mode in the editor.",
			Safety      = ToolSafety.Mutating,
			Schema      = Obj( new { } ),
			Handler     = _ =>
			{
				try   { EditorScene.Play(); return Ok( new { entered_play_mode = true } ); }
				catch ( Exception ex ) { return Err( ex.Message ); }
			}
		},

		new Tool
		{
			Name        = "stop_scene",
			Description = "Exit play mode in the editor.",
			Safety      = ToolSafety.Mutating,
			Schema      = Obj( new { } ),
			Handler     = _ =>
			{
				try   { EditorScene.Stop(); return Ok( new { exited_play_mode = true } ); }
				catch ( Exception ex ) { return Err( ex.Message ); }
			}
		},

		new Tool
		{
			Name        = "frame_camera_on_object",
			Description = "Move the editor camera to frame a GameObject (like pressing F in the scene view).",
			Safety      = ToolSafety.Mutating,
			Schema      = Obj( new { id_or_name = Str( "Target GameObject." ) }, "id_or_name" ),
			Handler     = args =>
			{
				var go = FindGameObject( GetString( args, "id_or_name" ) );
				if ( go == null ) return Err( "GameObject not found." );
				try
				{
					var session = SceneEditorSession.Active;
					session?.FullUndoSnapshot( "frame_camera" );
					session?.Selection.Set( go );
					EditorEvent.Run( "scene.frame.selection" );
					return Ok( new { framed = go.Name } );
				}
				catch ( Exception ex ) { return Err( ex.Message ); }
			}
		},
	};

	// ── reflection helpers ────────────────────────────────────────────────────

	private static string BuildPath( GameObject go )
	{
		var parts = new List<string>();
		var cur   = go;
		while ( cur != null ) { parts.Insert( 0, cur.Name ); cur = cur.Parent; }
		return string.Join( "/", parts );
	}

	private static Type ResolveComponentType( string name )
	{
		foreach ( var asm in AppDomain.CurrentDomain.GetAssemblies() )
		{
			Type[] types;
			try   { types = asm.GetTypes(); }
			catch { continue; }
			foreach ( var t in types )
			{
				if ( !typeof( Component ).IsAssignableFrom( t ) || t.IsAbstract ) continue;
				if ( t.Name == name || t.FullName == name ) return t;
			}
		}
		return null;
	}

	private static object ConvertJsonToProperty( JsonElement json, Type targetType )
	{
		if ( targetType == typeof( string ) )  return json.ValueKind == JsonValueKind.String ? json.GetString() : json.ToString();
		if ( targetType == typeof( bool ) )    return json.GetBoolean();
		if ( targetType == typeof( int ) )     return json.GetInt32();
		if ( targetType == typeof( float ) )   return json.GetSingle();
		if ( targetType == typeof( double ) )  return json.GetDouble();

		if ( targetType == typeof( Vector3 ) && json.ValueKind == JsonValueKind.Object )
			return new Vector3(
				(float)( json.TryGetProperty( "x", out var x ) ? x.GetDouble() : 0 ),
				(float)( json.TryGetProperty( "y", out var y ) ? y.GetDouble() : 0 ),
				(float)( json.TryGetProperty( "z", out var z ) ? z.GetDouble() : 0 ) );

		if ( targetType == typeof( Color ) && json.ValueKind == JsonValueKind.Object )
			return new Color(
				(float)( json.TryGetProperty( "r", out var r ) ? r.GetDouble() : 0 ),
				(float)( json.TryGetProperty( "g", out var g ) ? g.GetDouble() : 0 ),
				(float)( json.TryGetProperty( "b", out var b ) ? b.GetDouble() : 0 ),
				(float)( json.TryGetProperty( "a", out var a ) ? a.GetDouble() : 1 ) );

		// Fallback — JSON-deserialise
		var raw = json.GetRawText();
		return JsonSerializer.Deserialize( raw, targetType );
	}

	// ── execution gateway ─────────────────────────────────────────────────────

	/// <summary>
	/// Invoke a tool's handler on the main thread and return the JSON-serialised
	/// result string the model should see.
	/// </summary>
	public static string ExecuteSync( Tool tool, JsonElement args )
	{
		object result;
		try
		{
			result = tool.Handler( args );
		}
		catch ( Exception ex )
		{
			result = new { ok = false, error = $"Handler crashed: {ex.Message}" };
		}

		try
		{
			return JsonSerializer.Serialize( result, new JsonSerializerOptions
			{
				DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
				WriteIndented           = false,
			} );
		}
		catch ( Exception ex )
		{
			return JsonSerializer.Serialize( new { ok = false, error = "Result not serialisable: " + ex.Message } );
		}
	}
}
