using System.ComponentModel;

namespace Sandbox;

public static partial class Gizmo
{
	static GizmoControls _gizmo = new();

	/// <summary>
	/// Holds fully realized controls to manipulate some value
	/// </summary>
	public static GizmoControls Control => _gizmo;

	/// <summary>
	/// Extendable helper to create common gizmos
	/// </summary>
	public sealed partial class GizmoControls
	{
		internal GizmoControls()
		{

		}

		/// <summary>
		/// A front left up position movement widget. If widget was moved then will return true and out will return the new position.
		/// </summary>
		public bool Position( string name, Vector3 position, out Vector3 newPos, Rotation? axisRotation = null, float squareSize = 3.0f, Func<Vector3?> centerRaycast = null )
		{
			if ( position.IsNaN )
			{
				newPos = Vector3.Zero;
				return true;
			}

			var localRotation = Transform.Rotation;
			var localScale = Transform.UniformScale;
			var axis = axisRotation ?? Rotation.Identity;

			using var x = Sandbox.Gizmo.Scope( name, Vector3.Zero, axis );

			using var scaler = PushFixedScale();

			// I don't know if we should even be doing this here or leave it to the implementation
			if ( Settings.GlobalSpace )
				Transform = Transform.WithRotation( Rotation.Identity );

			Sandbox.Gizmo.Draw.IgnoreDepth = true;

			var rot = Transform.Rotation;
			var forward = rot.Forward;
			var left = rot.Left;
			var up = rot.Up;

			Vector3 movement = Vector3.Zero;

			Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Colors.Up;
			if ( Arrow( "up", Vector3.Up, out var xdist ) )
			{
				movement += Vector3.Up * xdist;
			}

			Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Colors.Left;
			if ( Arrow( "left", Vector3.Left, out var ydist ) )
			{
				movement += Vector3.Left * ydist;
			}

			Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Colors.Forward;
			if ( Arrow( "forward", Vector3.Forward, out var zdist ) )
			{
				movement += Vector3.Forward * zdist;
			}

			if ( squareSize > 0 )
			{
				float squareOffset = squareSize * 3.0f;
				using var scope = Sandbox.Gizmo.Scope( "squares" );
				Hitbox.DepthBias *= 0.8f;

				if ( centerRaycast != null )
				{
					Sandbox.Gizmo.Draw.Color = Color.White.WithAlpha( 1.1f );
					if ( DragCenterRaycast( "drag-camera", 2.0f, centerRaycast, out var moved ) )
					{
						movement += moved;
					}
				}
				else
				{
					var camRot = Transform.RotationToLocal( Camera.Rotation );

					Sandbox.Gizmo.Draw.Color = Color.White.WithAlpha( 1.1f );
					if ( DragSquare( "drag-camera", squareSize, camRot, out var moved, DrawPositionCenter ) )
					{
						// movement is in camera space
						movement += moved;
					}
				}

				using ( Sandbox.Gizmo.Scope() )
				{
					Sandbox.Gizmo.Transform = Sandbox.Gizmo.Transform.ToWorld( new Transform( Vector3.Up * squareOffset + Vector3.Left * squareOffset ) );
					Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Colors.Up;
					if ( DragSquare( "left-up", squareSize, Rotation.LookAt( Vector3.Backward, Vector3.Up ), out var moved ) )
					{
						movement += moved;
					}
				}

				using ( Sandbox.Gizmo.Scope() )
				{
					Sandbox.Gizmo.Transform = Sandbox.Gizmo.Transform.ToWorld( new Transform( Vector3.Forward * squareOffset + Vector3.Left * squareOffset ) );
					Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Colors.Left;
					if ( DragSquare( "forward-left", squareSize, Rotation.LookAt( Vector3.Up, Vector3.Forward ), out var moved ) )
					{
						movement += moved;
					}
				}

				using ( Sandbox.Gizmo.Scope() )
				{
					Sandbox.Gizmo.Transform = Sandbox.Gizmo.Transform.ToWorld( new Transform( Vector3.Forward * squareOffset + Vector3.Up * squareOffset ) );
					Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Colors.Forward;
					if ( DragSquare( "forward-up", squareSize, Rotation.LookAt( Vector3.Left, Vector3.Down ), out var moved ) )
					{
						movement += moved;
					}
				}
			}

			if ( movement.IsNearlyZero() )
			{
				newPos = position;
				return false;
			}

			movement = (movement * Transform.Rotation) * localRotation.Inverse;

			if ( localScale != 0.0f )
			{
				movement *= (1.0f / localScale);
			}

			if ( movement.IsNearlyZero() )
			{
				newPos = position;
				return false;
			}

			newPos = position + movement;

			return true;
		}

		// PAINDAY - #5562 added the centerRaycast parameter. Adding a parameter is a binary break, so
		// already-compiled packages still reference the old 5 argument signature. Keep it around and forward.
		[EditorBrowsable( EditorBrowsableState.Never ), Obsolete( "Use the overload with a centerRaycast callback" )]
		public bool Position( string name, Vector3 position, out Vector3 newPos, Rotation? axisRotation, float squareSize )
		{
			return Position( name, position, out newPos, axisRotation, squareSize, null );
		}

		static void DrawPositionCenter()
		{
			Sandbox.Gizmo.Draw.LineThickness = 2.0f;
			Sandbox.Gizmo.Draw.LineCircle( 0, Sandbox.Gizmo.IsHovered ? 0.6f : 0.5f, 8 );
		}

		/// <summary>
		/// A center handle that moves the object to the position of a raycast hit, cast from mouse cursor
		/// </summary>
		public bool DragCenterRaycast( string name, float size, Func<Vector3?> raycast, out Vector3 movement )
		{
			movement = default;

			using var x = Sandbox.Gizmo.Scope( name );

			var bbox = new BBox( new Vector3( -size ), new Vector3( size ) );

			Sandbox.Gizmo.Hitbox.BBox( bbox );

			if ( !Sandbox.Gizmo.IsHovered ) Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Draw.Color.Darken( 0.33f );

			var drawSize = Sandbox.Gizmo.IsHovered ? size : size * 0.85f;
			Sandbox.Gizmo.Draw.SolidBox( new BBox( new Vector3( -drawSize ), new Vector3( drawSize ) ) );

			if ( !Pressed.This )
				return false;

			if ( raycast() is not Vector3 hit )
				return false;

			var anchor = Transform.Position;
			var rot = Transform.Rotation;

			movement = (hit - anchor) * rot.Inverse;

			return true;
		}

		/// <summary>
		/// Draw an arrow - return move delta if interacted with
		/// </summary>
		public bool Arrow( string name, Vector3 axis, out float distance, float length = 24.0f, float girth = 6.0f, float axisOffset = 2.0f, float cullAngle = 10.0f, float snapSize = 0.0f, string head = "cone" )
		{
			distance = 0;

			var angle = Vector3.GetAngle( axis, Transform.NormalToLocal( Camera.Rotation.Forward ) );

			if ( angle < cullAngle || angle > 180.0f - cullAngle )
				return false;

			var localCam = Transform.RotationToLocal( Camera.Rotation );

			// Use the camera to provide a plane that'll work for us
			var rot = Rotation.LookAt( axis, Vector3.Up );

			using var x = Sandbox.Gizmo.Scope( name, axis * axisOffset, rot );

			girth *= 0.5f;

			Sandbox.Gizmo.Hitbox.BBox( new BBox( new Vector3( 0, -girth, -girth ), new Vector3( length, girth, girth ) ) );

			if ( !Sandbox.Gizmo.IsHovered ) Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Draw.Color.Darken( 0.33f );

			Sandbox.Gizmo.Draw.LineThickness = girth;


			var lineLength = length;
			var headLength = 4.0f;

			if ( snapSize > 0 )
			{
				lineLength = lineLength.SnapToGrid( snapSize );
			}

			// not pressed, no movement
			Sandbox.Gizmo.Draw.Line( 0, Vector3.Forward * (lineLength - headLength) );

			if ( head == "cone" )
			{
				Sandbox.Gizmo.Draw.SolidCone( Vector3.Forward * (lineLength - headLength), Vector3.Forward * headLength, headLength * 0.33f );
			}

			if ( head == "box" )
			{
				Sandbox.Gizmo.Draw.SolidBox( BBox.FromPositionAndSize( Vector3.Forward * (lineLength - headLength), headLength * 0.5f ) );
			}

			if ( !Pressed.This )
				return false;

			// use a plane that follows the axis but that uses the camera's plane
			Gizmo.Transform = Gizmo.Transform.WithRotation( Rotation.LookAt( Transform.Rotation.Forward, Camera.Rotation.Forward ) );


			//
			// Get the delta between trace hits against a plane
			//
			var delta = Sandbox.Gizmo.GetMouseDelta( Vector3.Zero, Vector3.Up );

			distance = Vector3.Forward.Dot( delta );

			// restrict movement to the axis direction
			return distance != 0.0f;

		}

		public bool DragBox( string name, Vector3 size, Rotation rotation, out Vector3 movement )
		{
			movement = default;
			var box = new BBox( -size * 0.5f, size * 0.5f );

			using var x = Sandbox.Gizmo.Scope( name, new Transform( 0, rotation ) );

			// Hitbox
			Sandbox.Gizmo.Hitbox.BBox( box );

			// Draw
			if ( !Sandbox.Gizmo.IsHovered )
			{
				Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Draw.Color.Darken( 0.33f );
			}

			if ( !Sandbox.Gizmo.IsHovered ) Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Draw.Color.Darken( 0.03f );

			Sandbox.Gizmo.Draw.SolidBox( box );

			if ( !Pressed.This )
				return false;

			//
			// action
			//

			var localCameraRot = Transform.RotationToLocal( Camera.Rotation );

			var delta = Sandbox.Gizmo.GetMouseDelta( 0, Rotation.FromAxis( localCameraRot.Forward, 0 ).Forward );
			movement = Vector3.One.Dot( delta );

			// Optional: Debug drawing
			if ( Hitbox.Debug && Sandbox.Gizmo.IsHovered )
			{
				using var scope = Scope();

				var os = Transform;
				Transform = Transform.WithScale( 1.0f );
				Sandbox.Gizmo.Draw.LineThickness = 1.0f;
				Sandbox.Gizmo.Draw.Color = Color.White;
				Sandbox.Gizmo.Draw.Plane( 0, Vector3.Forward );
				Transform = os;
			}

			return movement != Vector3.Zero;
		}

		/// <summary>
		/// Manipulate a 2d value by moving on 2 axis
		/// </summary>
		public bool DragSquare( string name, Vector2 size, Rotation rotation, out Vector3 movement, Action drawHandle = null )
		{
			movement = default;
			var bbox = new BBox( new Vector3( -0.01f, -size.x, -size.y ), new Vector3( 0.01f, size.x, size.y ) );

			using var x = Sandbox.Gizmo.Scope( name, new Transform( 0, rotation ) );


			//
			// hitbox
			//

			Sandbox.Gizmo.Hitbox.BBox( bbox );

			//
			// drawing
			//

			if ( !Sandbox.Gizmo.IsHovered ) Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Draw.Color.Darken( 0.33f );

			if ( drawHandle == null )
			{
				Sandbox.Gizmo.Draw.LineThickness = 2.0f;
				Sandbox.Gizmo.Draw.LineBBox( bbox );
				//Scene.Draw.LineCircle( 0, size.x );
			}

			drawHandle?.Invoke();

			if ( !Pressed.This )
				return false;

			//
			// action
			//

			movement = Sandbox.Gizmo.GetMouseDelta( 0, Vector3.Forward );

			if ( Hitbox.Debug && Sandbox.Gizmo.IsHovered )
			{
				using var scope = Scope();

				var os = Transform;
				Transform = Transform.WithScale( 1.0f );
				Sandbox.Gizmo.Draw.LineThickness = 1.0f;
				Sandbox.Gizmo.Draw.Color = Color.White;
				Sandbox.Gizmo.Draw.Plane( 0, Vector3.Forward );
				Transform = os;
			}

			movement *= rotation;

			return movement != 0;

		}

		/// <summary>
		/// Scope this before drawing a control to obey Settings.GizmoScale
		/// </summary>
		public static IDisposable PushFixedScale( float? scale = null )
		{
			if ( !scale.HasValue )
				scale = Settings.GizmoScale;

			if ( Camera.Ortho )
			{
				scale *= Camera.OrthoHeight * 0.006f;
			}
			else
			{
				var screenSize = 1024.0f / Camera.Size.Length;
				scale *= screenSize;

				var dist = Camera.Position.Distance( Transform.Position );
				scale *= dist * Camera.FieldOfView.DegreeToRadian() * 0.006f;
			}

			var s = Scope( "WorldScale" );
			Transform = Transform.WithScale( scale.Value );
			return s;
		}

	}
}
