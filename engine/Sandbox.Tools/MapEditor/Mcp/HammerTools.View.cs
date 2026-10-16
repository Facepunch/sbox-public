using Editor.MapEditor;
using System;

namespace Editor.Mcp;

internal static partial class HammerTools
{
	[McpTool.ReadOnly( "hammer_views" )]
	[Description( "Hammer's views of the open map - each one's index, whether it's 3D or a 2D (orthographic) view, its camera position, angles and field of view, its size, and which one the user used last." )]
	public static HammerView[] Views()
	{
		ActiveMap();

		var active = Hammer.ActiveMapView;

		return Hammer.MapViews.Select( ( view, index ) => ViewState( view, index, view == active ) ).ToArray();
	}

	[McpTool( "hammer_set_camera" )]
	[Description( "Move a Hammer view's camera - give a position, and angles or a point to look at. Defaults to the 3D view the user used last. Returns the view's new state. To frame specific nodes instead, use hammer_focus_nodes." )]
	public static HammerView SetCamera(
		[Description( "Camera position as 'x,y,z'. Empty keeps the current one." )] string position = "",
		[Description( "View angles as 'pitch,yaw,roll'." )] string angles = "",
		[Description( "A point to look at, as 'x,y,z' - overrides angles." )] string lookAt = "",
		[Description( "Which view, by index from hammer_views. -1 for the main 3D view." )] int view = -1 )
	{
		ActiveMap();

		var target = ResolveView( view );

		if ( !string.IsNullOrWhiteSpace( position ) )
			target.CameraPosition = Vector3.Parse( position );

		if ( !string.IsNullOrWhiteSpace( lookAt ) )
			target.CameraAngles = Rotation.LookAt( Vector3.Parse( lookAt ) - target.CameraPosition ).Angles();
		else if ( !string.IsNullOrWhiteSpace( angles ) )
			target.CameraAngles = Angles.Parse( angles );

		return ViewState( target, Hammer.MapViews.ToList().IndexOf( target ), target == Hammer.ActiveMapView );
	}

	[McpTool( "hammer_focus_nodes" )]
	[Description( "Select nodes and frame every Hammer view on them, like the user pressing the frame selection key - the quickest way to show the user something, or to aim hammer_screenshot at it." )]
	public static HammerSelection FocusNodes( [Description( "The node ids to frame." )] int[] ids )
	{
		var map = ActiveMap();
		var nodes = ResolveNodes( map, ids );

		Selection.Clear();

		foreach ( var node in nodes )
		{
			Selection.Add( node );
		}

		Hammer.RunCommand( "FrameAllViews" );

		return GetSelection();
	}

	[McpTool.ReadOnly( "hammer_screenshot" )]
	[Description( "Render the map as Hammer's 3D view draws it and return the image - see what you've built. Renders from the main 3D view's camera unless you give a camera, which doesn't move the user's view. Aim it with hammer_focus_nodes or hammer_set_camera first, or pass position and lookAt." )]
	public static Bitmap Screenshot(
		[Description( "Image width in pixels." ), Range( 16, 4096 )] int width = 1280,
		[Description( "Image height in pixels." ), Range( 16, 4096 )] int height = 720,
		[Description( "Camera position as 'x,y,z'. Empty uses the 3D view's camera." )] string position = "",
		[Description( "Camera angles as 'pitch,yaw,roll'." )] string angles = "",
		[Description( "A point to look at as 'x,y,z' - overrides angles." )] string lookAt = "",
		[Description( "Field of view in degrees. 0 uses the 3D view's." ), Range( 0, 170 )] float fieldOfView = 0 )
	{
		ActiveMap();

		var view = ResolveView( -1 );
		var cameraPosition = string.IsNullOrWhiteSpace( position ) ? view.CameraPosition : Vector3.Parse( position );
		var cameraAngles = !string.IsNullOrWhiteSpace( lookAt ) ? Rotation.LookAt( Vector3.Parse( lookAt ) - cameraPosition ).Angles()
			: !string.IsNullOrWhiteSpace( angles ) ? Angles.Parse( angles )
			: view.CameraAngles;

		var fov = fieldOfView > 0 ? fieldOfView : view.FieldOfView > 0 ? view.FieldOfView : 90;

		var bitmap = new Bitmap( width, height );
		view.RenderToBitmap( bitmap, cameraPosition, cameraAngles, fov );

		return bitmap;
	}

	[McpTool.ReadOnly( "hammer_list_commands" )]
	[Description( "Hammer's named commands - everything its key bindings and menus can do, like GroupSelection, HideUnselected, SelectSimilar, MirrorHorizontal, SnapToGrid, CreatePrefabFromSelection, FrameAllViews or BuildMap. Run one with hammer_run_command." )]
	public static object ListCommands( [Description( "Case insensitive substring to filter by, e.g. 'select' or 'prefab'. Empty lists all." )] string query = "" )
	{
		ActiveMap();

		var commands = Hammer.Commands
			.Where( x => string.IsNullOrWhiteSpace( query ) || x.Contains( query.Trim(), StringComparison.OrdinalIgnoreCase ) )
			.Order( StringComparer.OrdinalIgnoreCase )
			.ToArray();

		return new { Total = commands.Length, Commands = commands };
	}

	[McpTool( "hammer_run_command" )]
	[Description( "Run a Hammer command by name, exactly as if the user pressed its key binding. Most act on the selection, so select first with hammer_select - e.g. select nodes then run GroupSelection. A few open dialogs and will wait for the user. hammer_list_commands lists them all." )]
	public static object RunCommand( [Description( "The command name, as hammer_list_commands shows it." )] string name )
	{
		ActiveMap();

		var command = FindCommand( name );
		Hammer.RunCommand( command );

		return $"Ran {command}";
	}

	[McpTool( "hammer_undo" )]
	[Description( "Undo the last change to the map, exactly like the user pressing ctrl+z - use it to back out of your own mistake." )]
	public static object Undo()
	{
		ActiveMap();
		Hammer.RunCommand( "Undo" );
		return "Undone";
	}

	[McpTool( "hammer_redo" )]
	[Description( "Redo the last undone change to the map." )]
	public static object Redo()
	{
		ActiveMap();
		Hammer.RunCommand( "Redo" );
		return "Redone";
	}

	[McpTool( "hammer_save" )]
	[Description( "Save the open map to its file. A map that has never been saved has no file yet - the user needs to save it in Hammer first." )]
	public static object Save()
	{
		ActiveMap();

		if ( !Hammer.Save() )
			throw new Exception( Hammer.MapAsset is null
				? "This map has never been saved so it has no file - the user needs to save it in Hammer first"
				: "Saving failed - read_console may say why" );

		return new { Saved = Hammer.MapAsset?.Path, Hammer.HasUnsavedChanges };
	}

	/// <summary>
	/// A view by index, or the 3D view the user used last when the index is negative.
	/// </summary>
	static MapView ResolveView( int index )
	{
		var views = Hammer.MapViews.ToArray();

		if ( index >= 0 )
		{
			return index < views.Length ? views[index]
				: throw new Exception( $"No view {index} - there are {views.Length}, hammer_views lists them" );
		}

		var active = Hammer.ActiveMapView;

		if ( active is not null && !active.IsOrthographic )
			return active;

		return views.FirstOrDefault( x => !x.IsOrthographic )
			?? throw new Exception( "Hammer has no 3D view open" );
	}

	static string FindCommand( string name )
	{
		var commands = Hammer.Commands;
		var command = commands.FirstOrDefault( x => string.Equals( x, name?.Trim(), StringComparison.OrdinalIgnoreCase ) );

		if ( command is not null )
			return command;

		var similar = commands.Where( x => x.Contains( name?.Trim() ?? "", StringComparison.OrdinalIgnoreCase ) ).Order().Take( 10 ).ToArray();

		throw new Exception( similar.Length > 0
			? $"No Hammer command '{name}'. Did you mean: {string.Join( ", ", similar )}"
			: $"No Hammer command '{name}' - hammer_list_commands lists them" );
	}

	static HammerView ViewState( MapView view, int index, bool active ) => new()
	{
		Index = index,
		Is3D = !view.IsOrthographic,
		Active = active,
		Position = view.CameraPosition,
		Angles = view.CameraAngles,
		FieldOfView = view.IsOrthographic ? 0 : view.FieldOfView,
		Size = view.Size
	};

	/// <summary>One of Hammer's views.</summary>
	public class HammerView
	{
		/// <summary>The view's index, for hammer_set_camera.</summary>
		public int Index { get; set; }

		/// <summary>True for a perspective view, false for the 2D top/front/side views.</summary>
		public bool Is3D { get; set; }

		/// <summary>Whether this is the view the user used last.</summary>
		public bool Active { get; set; }

		public Vector3 Position { get; set; }
		public Angles Angles { get; set; }

		/// <summary>Field of view in degrees. 0 for 2D views.</summary>
		public float FieldOfView { get; set; }

		/// <summary>The view's size in pixels.</summary>
		public Vector2 Size { get; set; }
	}
}
