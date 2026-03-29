using System;

namespace Editor;

public static class EditorPreferences
{
	public static bool NotificationPopups
	{
		get => EditorCookie.Get<bool>( "NotificationPopups", true );
		set => EditorCookie.Set( "NotificationPopups", value );
	}

	public static bool NotificationSounds
	{
		get => EditorCookie.Get<bool>( "NotificationSounds", true );
		set => EditorCookie.Set( "NotificationSounds", value );
	}

	public static bool ClearConsoleOnPlay
	{
		get => EditorCookie.Get<bool>( "ClearConsoleOnPlay", false );
		set => EditorCookie.Set( "ClearConsoleOnPlay", value );
	}

	public static bool FullScreenOnPlay
	{
		get => EditorCookie.Get<bool>( "FullScreenOnPlay", false );
		set => EditorCookie.Set( "FullScreenOnPlay", value );
	}

	[Description( "Use a much faster way of loading code changes if you only change method bodies. Will currently break stack traces of hotloaded methods." )]
	public static bool FastHotload
	{
		get => bool.TryParse( ConsoleSystem.GetValue( "hotload_fast" ), out var val ) ? val : true;
		set => ConVarSystem.SetValue( "hotload_fast", value.ToString(), true );
	}

	public enum NotificationLevel
	{
		ShowAlways,
		ShowOnError,
		NeverShow
	}

	public static NotificationLevel CompileNotifications
	{
		get => EditorCookie.Get( "CompileNotifications", NotificationLevel.ShowAlways );
		set => EditorCookie.Set( "CompileNotifications", value );
	}

	/// <summary>
	/// The amount of seconds to keep a notification open if it's an error
	/// </summary>
	[Title( "Error Timeout" )]
	public static float ErrorNotificationTimeout
	{
		get => EditorCookie.Get( "ErrorNotificationTimeout", 30.0f );
		set => EditorCookie.Set( "ErrorNotificationTimeout", value );
	}


	/// <summary>
	/// Camera field of view
	/// </summary>
	[Title( "Field Of View" )]
	[Range( 1.0f, 180.0f )]
	public static float CameraFieldOfView
	{
		get => EditorCookie.Get( "SceneView.CameraFOV", 80.0f );
		set => EditorCookie.Set( "SceneView.CameraFOV", value );
	}

	/// <summary>
	/// The closest thing to render
	/// </summary>
	[Title( "ZNear" )]
	[Range( 1.0f, 10000.0f )]
	public static float CameraZNear
	{
		get => EditorCookie.Get( "SceneView.CameraZNear", 1.0f );
		set => EditorCookie.Set( "SceneView.CameraZNear", value );
	}

	/// <summary>
	/// The furthest thing to render
	/// </summary>
	[Title( "ZFar" )]
	[Range( 1.0f, 100000.0f )]
	public static float CameraZFar
	{
		get => EditorCookie.Get( "SceneView.CameraZFar", 100000.0f );
		set => EditorCookie.Set( "SceneView.CameraZFar", value );
	}

	/// <summary>
	/// Should we smooth the movement of the camera. This is the smooth time, in seconds. No smoothing
	/// feels pretty jarring, but a bit feels nice. Once you get over half a second it makes everything feel
	/// slow and horrible.
	/// </summary>
	[Title( "Movement Smoothing" )]
	[Range( 0.0f, 1.0f )]
	public static float CameraMovementSmoothing
	{
		get => EditorCookie.Get( "SceneView.CameraMovementSmoothing", 0.5f );
		set => EditorCookie.Set( "SceneView.CameraMovementSmoothing", value );
	}

	/// <summary>
	/// How fast should the camera move
	/// </summary>
	[Title( "Movement Speed" )]
	[Range( 0.0f, 100.0f )]
	public static float CameraSpeed
	{
		get => EditorCookie.Get( "SceneView.CameraSpeed", 1.0f );
		set => EditorCookie.Set( "SceneView.CameraSpeed", value );
	}

	[Title( "Sensitivity" )]
	[Range( 0.01f, 5.0f )]
	public static float CameraSensitivity
	{
		get => EditorCookie.Get( "SceneView.CameraSensitivity", 1.0f );
		set => EditorCookie.Set( "SceneView.CameraSensitivity", value );
	}

	[Title( "Create Objects at Origin" )]
	public static bool CreateObjectsAtOrigin
	{
		get => EditorCookie.Get( "SceneView.CreateObjectsAtOrigin", false );
		set => EditorCookie.Set( "SceneView.CreateObjectsAtOrigin", value );
	}

	/// <summary>
	/// Should the orbit camera zoom be inverted?
	/// <list type="bullet">
	/// <item>Inverted: mouse up/left zooms in, mouse down/right zooms out</item>
	/// <item>Standard: mouse down/right zooms in, mouse up/left zooms out</item>
	/// </list>
	/// </summary>
	[Title( "Invert Orbit Zoom" )]
	public static bool InvertOrbitZoom
	{
		get => EditorCookie.Get( "SceneView.InvertOrbitZoom", false );
		set => EditorCookie.Set( "SceneView.InvertOrbitZoom", value );
	}

	/// <summary>
	/// How fast should the orbit camera zoom?
	/// </summary>
	[Title( "Orbit Zoom Speed" )]
	[Range( 0.0f, 10.0f )]
	public static float OrbitZoomSpeed
	{
		get => EditorCookie.Get( "SceneView.OrbitZoomSpeed", 1.0f );
		set => EditorCookie.Set( "SceneView.OrbitZoomSpeed", value );
	}

	/// <summary>
	/// Should the camera panning be inverted?
	/// </summary>
	[Title( "Invert Pan" )]
	public static bool CameraInvertPan
	{
		get => EditorCookie.Get( "SceneView.CameraInvertPan", false );
		set => EditorCookie.Set( "SceneView.CameraInvertPan", value );
	}

	/// <summary>
	/// Should we hide the eye cursor when rotating the scene camera?
	/// </summary>
	[Title( "Hide Cursor on Rotate" )]
	public static bool HideRotateCursor
	{
		get => EditorCookie.Get( "SceneView.HideRotateCursor", false );
		set => EditorCookie.Set( "SceneView.HideRotateCursor", value );
	}

	/// <summary>
	/// Should we hide the eye cursor when panning scene camera?
	/// </summary>
	[Title( "Hide Cursor on Pan" )]
	public static bool HidePanCursor
	{
		get => EditorCookie.Get( "SceneView.HidePanCursor", false );
		set => EditorCookie.Set( "SceneView.HidePanCursor", value );
	}

	/// <summary>
	/// Should we hide the eye cursor when orbiting scene camera?
	/// </summary>
	[Title( "Hide Cursor on Orbit" )]
	public static bool HideOrbitCursor
	{
		get => EditorCookie.Get( "SceneView.HideOrbitCursor", false );
		set => EditorCookie.Set( "SceneView.HideOrbitCursor", value );
	}

	/// <summary>
	/// Should we hit the back faces when tracing meshes
	/// </summary>
	[Title( "Backface Selection" )]
	public static bool BackfaceSelection
	{
		get => EditorCookie.Get( "SceneView.BackfaceSelection", true );
		set => EditorCookie.Set( "SceneView.BackfaceSelection", value );
	}

	/// <summary>
	/// Use bounds when dragging in objects
	/// </summary>
	[Title( "Place Using Bounds" )]
	public static bool BoundsPlacement
	{
		get => EditorCookie.Get( "SceneView.BoundsPlacement", true );
		set => EditorCookie.Set( "SceneView.BoundsPlacement", value );
	}

	/// <summary>
	/// When enabled, pasted or duplicated objects are placed under the cursor and aligned to the hit surface.
	/// </summary>
	[Title( "Paste At Cursor" )]
	public static bool PasteAtCursor
	{
		get => EditorCookie.Get( "SceneView.PasteAtCursor", true );
		set => EditorCookie.Set( "SceneView.PasteAtCursor", value );
	}

	/// <summary>
	/// Controls whether a sound is played for any undo/redo operation (success or failure)
	/// </summary>
	[Title( "Undo/Redo Sounds" )]
	public static bool UndoSounds
	{
		get => EditorCookie.Get( "UndoSounds", true );
		set => EditorCookie.Set( "UndoSounds", value );
	}

	/// <summary>
	/// Overrides for any Editor shortcuts.
	/// </summary>
	public static Dictionary<string, string> ShortcutOverrides
	{
		get
		{
			if ( _shortcutOverrides is null )
			{
				var json = EditorCookie.GetString( "KeybindOverrides", null );
				if ( string.IsNullOrEmpty( json ) )
					_shortcutOverrides = new Dictionary<string, string>();
				else
				{
					_shortcutOverrides = Json.Deserialize<Dictionary<string, string>>( json );
					if ( _shortcutOverrides is null )
						_shortcutOverrides = new Dictionary<string, string>();
				}
			}
			return _shortcutOverrides;
		}
		set
		{
			_shortcutOverrides = value;
			EditorCookie.SetString( "KeybindOverrides", Json.Serialize( value ) );
		}
	}

	static Dictionary<string, string> _shortcutOverrides;

	/// <summary>
	/// Whether new game instances spawned by the editor are in windowed mode.
	/// </summary>
	[Title( "Windowed Local Instances" )]
	[Description( "Whether new game instances spawned by the editor are in windowed mode." )]
	public static bool WindowedLocalInstances
	{
		get => ProjectCookie.Get( "NewInstance.Windowed", true );
		set => ProjectCookie.Set( "NewInstance.Windowed", value );
	}

	/// <summary>
	/// Command-line arguments for new game instances spawned by the editor.
	/// </summary>
	[Title( "Command Line Args" )]
	[Description( "Command-line arguments for new game instances spawned by the editor." )]
	public static string NewInstanceCommandLineArgs
	{
		get => ProjectCookie.GetString( "NewInstance.Args" );
		set => ProjectCookie.SetString( "NewInstance.Args", value );
	}

	/// <summary>
	/// Command-line arguments for new game instances spawned by the editor.
	/// </summary>
	[Title( "Server Command Line Args" )]
	[Description( "Command-line arguments for the dedicated server spawned by the editor." )]
	public static string DedicatedServerCommandLineArgs
	{
		get => ProjectCookie.GetString( "DedicatedServerInstance.Args" );
		set => ProjectCookie.SetString( "DedicatedServerInstance.Args", value );
	}
}
