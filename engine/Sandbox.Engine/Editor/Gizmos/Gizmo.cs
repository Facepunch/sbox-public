using Sandbox.Utility;
using System.Collections.Immutable;

namespace Sandbox;

public static partial class Gizmo
{
	public static SceneWorld World => Active.World;

	public static Ray PreviousRay => Active.previous.Input.CursorRay;
	public static Ray CurrentRay => Active.current.Input.CursorRay;


	public static float RayDepth => Camera.ZFar + MathF.Abs( Camera.ZNear );

	public static string ControlMode
	{
		get => Active.ControlMode;
	}

	public static Transform Transform
	{
		get => Active.scope.Transform;
		set => Active.scope.Transform = value;
	}

	public static string Path
	{
		get => Active.scope.Path;
		set => Active.scope.Path = value;
	}

	public static object Object
	{
		get => Active.scope.Object;
		set => Active.scope.Object = value;
	}

	public static SceneCamera Camera
	{
		get => Active.Input.Camera;
	}

	public static bool IsHovered => HasHovered && Active.current.HoveredPath == Path;
	//public static bool IsChildHovered => HasHovered && Active.current.HoveredPath.StartsWith( Path );
	//public static bool IsParentHovered => HasHovered && Path.StartsWith( Active.current.HoveredPath );

	public static bool IsSelected => HasSelected && ((Active.current.SelectedPath?.Contains( Path ) ?? false) || (Object is not null && Active.Selection.Contains( Object )));
	public static bool IsChildSelected => HasSelected && (Active.current.SelectedPath?.Any( x => x.StartsWith( Path ) ) ?? false);
	//public static bool IsParentSelected => HasSelected && (Active.current.SelectedPath?.Any( x => Path.StartsWith( x ) ) ?? false);

	public static bool WasClicked => HasMouseFocus && IsHovered && Active.current.Click;

	public static bool HasSelected => Active.current.SelectedPath?.Count > 0 || Active.Selection.Any();
	public static bool HasHovered => !string.IsNullOrEmpty( Active.current.HoveredPath );

	public static bool HasClicked => Active.current.Click;

	public static bool HasMouseFocus => Active.Input.IsHovered;

	internal static void BeginInstance( Instance instance )
	{
		Active = instance;

		Draw.Color = Color.White;
		Transform = Transform.Zero;
		Path = "";

		Draw.Start();
	}


	public static void EndInstance( Instance previous )
	{
		Active.scope = default;
		Active = previous;
		Draw.End();
	}

	public static void Select( bool allowUnselect = true, bool allowMultiSelect = true )
	{
		// If already selected - can deselect if holding down Ctrl
		if ( allowUnselect && Active.current.Input.Modifiers.Contains( KeyboardModifiers.Ctrl ) )
		{
			if ( Active.builder.SelectedPath?.Contains( Path ) ?? false )
			{
				Active.builder.SelectedPath = Active.builder.SelectedPath.Remove( Path );
				return;
			}

			if ( Object is not null && Active.Selection.Contains( Object ) )
			{
				Active.Selection.Remove( Object );
				return;
			}
		}

		// If we're not multi selecting, clear the current selection
		if ( !allowMultiSelect || (!Active.current.Input.Modifiers.Contains( KeyboardModifiers.Ctrl ) && !Active.current.Input.Modifiers.Contains( KeyboardModifiers.Shift )) )
		{
			Active.builder.SelectedPath = null;
			Active.Selection.Clear();
		}

		Active.builder.SelectedPath ??= ImmutableHashSet<string>.Empty;
		if ( !string.IsNullOrWhiteSpace( Active.current.HoveredPath ) )
		{

			if ( Object is not null )
			{
				Active.Selection.Add( Object );
			}
			else
			{
				Active.builder.SelectedPath = Active.builder.SelectedPath.Add( Active.current.HoveredPath );
			}
		}
	}

	/// <summary>
	/// Create a new scope - any changes to colors and transforms will be stored
	/// and reverted when exiting the scope.
	/// </summary>
	public static IDisposable Scope( string path, Transform tx )
	{
		var scope = BlankScope();

		Transform = Transform.ToWorld( tx );
		Path = $"{Path}/{path}";

		return scope;
	}

	/// <summary>
	/// Create a new scope - any changes to colors and transforms will be stored
	/// and reverted when exiting the scope.
	/// </summary>
	public static IDisposable Scope( string path, Vector3 position ) => Scope( path, new Transform( position ) );

	/// <summary>
	/// Create a new scope - any changes to colors and transforms will be stored
	/// and reverted when exiting the scope.
	/// </summary>
	public static IDisposable Scope( string path, Vector3 position, Rotation rotation, float scale = 1.0f ) => Scope( path, new Transform( position, rotation, scale ) );

	/// <summary>
	/// Create a new scope - any changes to colors and transforms will be stored
	/// and reverted when exiting the scope.
	/// </summary>
	public static IDisposable Scope( string path = null )
	{
		if ( path == null )
		{
			var c = Active.scope.Create++;
			path = $"Scope{c}";
		}

		var scope = BlankScope();

		Path = $"{Path}/{path}";

		return scope;
	}

	/// <summary>
	/// Create a new scope - any changes to colors and transforms will be stored
	/// and reverted when exiting the scope.
	/// </summary>
	public static IDisposable ObjectScope<T>( T obj, Transform tx )
	{
		Assert.NotNull( obj );

		var scope = BlankScope();

		Transform = Transform.ToWorld( tx );
		Path = $"object-{obj.GetHashCode()}";
		Object = obj;

		return scope;
	}

	internal static DisposeAction<ScopeState> BlankScope()
	{
		// no scope, lets just ignore it all
		if ( Active is null )
			return default;

		var previousState = Active.scope;

		Active.scope.Create = 0;

		unsafe
		{
			static void RestoreScope( ScopeState previousState )
			{
				Active.scope = previousState;
			}

			return new DisposeAction<ScopeState>( &RestoreScope, previousState );
		}
	}



	/// <summary>
	/// Get the distance from a point on a plane
	/// </summary>
	public static Vector3? GetPositionOnPlane( Vector3 position, Vector3 planeNormal, Ray ray )
	{
		var plane = new Plane( position, planeNormal );

		if ( Hitbox.Debug )
		{
			Draw.Plane( position, planeNormal );
		}

		return plane.Trace( ray.ToLocal( Transform ), true );
	}


	/// <summary>
	/// Get the mouse delta at this current position
	/// </summary>
	public static Vector3 GetMouseDelta( Vector3 position, Vector3 planeNormal )
	{
		if ( GetPositionOnPlane( position, planeNormal, CurrentRay ) is not Vector3 a )
			return Vector3.Zero;

		if ( GetPositionOnPlane( position, planeNormal, PreviousRay ) is not Vector3 b )
			return Vector3.Zero;

		return (a - b) * Transform.Scale;
	}

	/// <summary>
	/// Get the mouse drag distance at this current position, assuming we are pressed
	/// </summary>
	public static Vector3 GetMouseDrag( Vector3 position, Vector3 planeNormal )
	{
		if ( GetPositionOnPlane( position, planeNormal, Pressed.Ray ) is not Vector3 a )
			return Vector3.Zero;

		if ( GetPositionOnPlane( position, planeNormal, CurrentRay ) is not Vector3 b )
			return Vector3.Zero;

		return (a - b) * Transform.Scale;
	}

	/// <summary>
	/// Get the distance from a point on a plane
	/// </summary>
	public static float GetMouseDistance( Vector3 position, Vector3 planeNormal )
	{
		var plane = new Plane( position, planeNormal );

		var a = plane.Trace( CurrentRay.ToLocal( Transform ), true );

		if ( a == null ) return 0.0f;

		return (position - a.Value).Length;
	}

	/// <summary>
	/// Get the distance moved from (or towards) a position on a plane
	/// </summary>
	public static float GetMouseDistanceDelta( Vector3 position, Vector3 planeNormal )
	{
		var plane = new Plane( position, planeNormal );

		var a = plane.Trace( CurrentRay.ToLocal( Transform ), true );
		var b = plane.Trace( PreviousRay.ToLocal( Transform ), true );

		if ( a == null ) return 0.0f;
		if ( b == null ) return 0.0f;

		var aDist = (position - a.Value).Length;
		var bDist = (position - b.Value).Length;

		return aDist - bDist;
	}

	/// <summary>
	/// The current cursor position, in screen space
	/// </summary>
	public static Vector2 CursorPosition => Active.previous.Input.CursorPosition;

	/// <summary>
	/// The delta of cursor movement between this frame and last, in screen space
	/// </summary>
	public static Vector2 CursorMoveDelta => Active.current.Input.CursorPosition - CursorPosition;

	/// <summary>
	/// The delta of cursor movement between last press and now, in screen space.
	/// If left mouse isn't down, will return CursorMoveDelta
	/// </summary>
	public static Vector2 CursorDragDelta => IsLeftMouseDown ? (Active.pressed.Input.CursorPosition - CursorPosition) : CursorMoveDelta;

	/// <summary>
	/// The current keyboard modifiers
	/// </summary>
	public static KeyboardModifiers KeyboardModifiers => Active.current.Input.Modifiers;

	public static bool IsCtrlPressed => KeyboardModifiers.Contains( KeyboardModifiers.Ctrl );
	public static bool IsShiftPressed => KeyboardModifiers.Contains( KeyboardModifiers.Shift );
	public static bool IsAltPressed => KeyboardModifiers.Contains( KeyboardModifiers.Alt );

	public static bool WasLeftMouseReleased => Active.previous.Input.LeftMouse && !IsLeftMouseDown;
	public static bool IsLeftMouseDown => Active.current.Input.LeftMouse;
	public static bool WasLeftMousePressed => IsLeftMouseDown && !Active.previous.Input.LeftMouse;

	public static bool WasRightMouseReleased => Active.previous.Input.RightMouse && !IsRightMouseDown;
	public static bool IsRightMouseDown => Active.current.Input.RightMouse;
	public static bool WasRightMousePressed => IsRightMouseDown && !Active.previous.Input.RightMouse;

	public static bool IsDoubleClicked => Active.current.Input.DoubleClick;

	/// <summary>
	/// Will snap this position, depending on the current snap settings and keys that are pressed.
	/// Will snap along if movement is detected along that axis. For example, if movement is 1,0,0 then we'll
	/// only snap on the x axis.
	/// </summary>
	public static Vector3 Snap( Vector3 input, Vector3 movement )
	{
		// If control does the opposite behaviour
		if ( Settings.SnapToGrid == IsCtrlPressed )
			return input;

		var sx = !movement.x.AlmostEqual( 0.0f );
		var sy = !movement.y.AlmostEqual( 0.0f );
		var sz = !movement.z.AlmostEqual( 0.0f );

		return input.SnapToGrid( Settings.GridSpacing, sx, sy, sz );
	}

	/// <summary>
	/// Will snap this position, depending on the current snap settings and keys that are pressed.
	/// Will snap along if movement is detected along that axis. For example, if movement is 1,0,0 then we'll
	/// only snap on the x axis.
	/// </summary>
	public static Angles Snap( Angles input, Angles movement )
	{
		// If control does the opposite behaviour
		if ( Settings.SnapToAngles == IsCtrlPressed )
			return input;

		var sx = !movement.pitch.AlmostEqual( 0.0f );
		var sy = !movement.yaw.AlmostEqual( 0.0f );
		var sz = !movement.roll.AlmostEqual( 0.0f );

		return input.SnapToGrid( Settings.AngleSpacing, sx, sy, sz );
	}

	/// <summary>
	/// Applies snapping to a box being resized using delta tracking. Returns a properly snapped box.
	/// </summary>
	/// <param name="startBox">The original box before resizing began</param>
	/// <param name="movement">The accumulated delta changes</param>
	/// <returns>A new snapped box with proper minimum dimensions</returns>
	public static BBox Snap( BBox startBox, BBox movement )
	{
		// Skip snapping if control is pressed (or not pressed, depending on settings)
		if ( Settings.SnapToGrid == IsCtrlPressed )
			return new BBox(
				startBox.Mins + movement.Mins,
				startBox.Maxs + movement.Maxs
			);

		// Apply snapped deltas to the original box
		var snappedBox = new BBox(
			startBox.Mins + Snap( movement.Mins, movement.Mins ),
			startBox.Maxs + Snap( movement.Maxs, movement.Maxs )
		);

		// Get minimum spacing
		float spacing = Settings.GridSpacing;

		// Ensure minimum size
		snappedBox.Maxs.x = System.Math.Max( snappedBox.Maxs.x, snappedBox.Mins.x + spacing );
		snappedBox.Mins.x = System.Math.Min( snappedBox.Mins.x, snappedBox.Maxs.x - spacing );
		snappedBox.Maxs.y = System.Math.Max( snappedBox.Maxs.y, snappedBox.Mins.y + spacing );
		snappedBox.Mins.y = System.Math.Min( snappedBox.Mins.y, snappedBox.Maxs.y - spacing );
		snappedBox.Maxs.z = System.Math.Max( snappedBox.Maxs.z, snappedBox.Mins.z + spacing );
		snappedBox.Mins.z = System.Math.Min( snappedBox.Mins.z, snappedBox.Maxs.z - spacing );

		return snappedBox;
	}

	/// <summary>
	/// Will give you a nudge vector along the most aligned left and up axis of the rotation
	/// based on left/right/up/down direction and camera angle
	/// </summary>
	public static Vector3 Nudge( Rotation rotation, Vector2 direction )
	{
		var up = rotation.ClosestAxis( Camera.Rotation.Up );
		var left = rotation.ClosestAxis( Camera.Rotation.Left );

		if ( Settings.SnapToGrid != IsCtrlPressed )
		{
			up *= Settings.GridSpacing * MathF.Sign( direction.y );
			left *= Settings.GridSpacing * MathF.Sign( direction.x );
		}
		else
		{
			up *= direction.y;
			left *= direction.x;
		}

		return up + left;
	}

	/// <summary>
	/// The cameras transform - in world space
	/// </summary>
	public static Transform CameraTransform => new Transform( Camera.Position, Camera.Rotation );

	/// <summary>
	/// The cameras transform - in local space
	/// </summary>
	public static Transform LocalCameraTransform => Gizmo.Transform.ToLocal( CameraTransform );
}
