namespace Sandbox;

public static partial class Gizmo
{
	static readonly GizmoControls _gizmo = new();
	public static GizmoControls Control => _gizmo;

	public sealed partial class GizmoControls
	{
		internal GizmoControls() { }

		public bool UpdatePosition( string name, Vector3 position, out Vector3 newPos, Rotation? axisRotation = null, float squareSize = 3.0f, Func<Vector3?> centerRaycast = null )
		{
			newPos = position;
			if ( position.IsNaN )
			{
				newPos = Vector3.Zero;
				return true;
			}

			var localRotation = Transform.Rotation;
			var localScale = Transform.UniformScale;
			using var scope = Scope( name, Vector3.Zero, axisRotation ?? Rotation.Identity );
			using var scaler = PushFixedScale();
			if ( Settings.GlobalSpace && axisRotation is null )
				Transform = Transform.WithRotation( Rotation.Identity );

			Vector3 movement = Vector3.Zero;
			if ( UpdateArrow( "up", Vector3.Up, out var up ) ) movement += Vector3.Up * up;
			if ( UpdateArrow( "left", Vector3.Left, out var left ) ) movement += Vector3.Left * left;
			if ( UpdateArrow( "forward", Vector3.Forward, out var forward ) ) movement += Vector3.Forward * forward;

			if ( squareSize > 0 )
			{
				using var squares = Scope( "squares" );
				var offset = squareSize * 3;
				if ( centerRaycast is not null )
				{
					if ( UpdateDragCenterRaycast( "drag-camera", centerRaycast, out var delta ) ) movement += delta;
				}
				else if ( UpdateDragSquare( "drag-camera", Transform.RotationToLocal( Camera.Rotation ), out var delta ) )
				{
					movement += delta;
				}

				using ( Scope() )
				{
					Transform = Transform.ToWorld( new Transform( (Vector3.Up + Vector3.Left) * offset ) );
					if ( UpdateDragSquare( "left-up", Rotation.LookAt( Vector3.Backward, Vector3.Up ), out var delta ) ) movement += delta;
				}
				using ( Scope() )
				{
					Transform = Transform.ToWorld( new Transform( (Vector3.Forward + Vector3.Left) * offset ) );
					if ( UpdateDragSquare( "forward-left", Rotation.LookAt( Vector3.Up, Vector3.Forward ), out var delta ) ) movement += delta;
				}
				using ( Scope() )
				{
					Transform = Transform.ToWorld( new Transform( (Vector3.Forward + Vector3.Up) * offset ) );
					if ( UpdateDragSquare( "forward-up", Rotation.LookAt( Vector3.Left, Vector3.Down ), out var delta ) ) movement += delta;
				}
			}

			movement = (movement * Transform.Rotation) * localRotation.Inverse;
			if ( localScale != 0 ) movement /= localScale;
			if ( movement.IsNearlyZero() ) return false;
			newPos += movement;
			return true;
		}

		public bool UpdateArrow( string name, Vector3 axis, out float distance, float axisOffset = 2, float cullAngle = 10 )
		{
			distance = 0;
			if ( !IsAxisVisible( axis, cullAngle ) ) return false;
			using var scope = Scope( name, axis * axisOffset, Rotation.LookAt( axis, Vector3.Up ) );
			if ( !Pressed.This ) return false;
			Transform = Transform.WithRotation( Rotation.LookAt( Transform.Rotation.Forward, Camera.Rotation.Forward ) );
			distance = Vector3.Forward.Dot( GetMouseDelta( Vector3.Zero, Vector3.Up ) );
			return distance != 0;
		}

		internal static bool IsAxisVisible( Vector3 axis, float cullAngle )
		{
			var angle = Vector3.GetAngle( axis, Transform.NormalToLocal( Camera.Rotation.Forward ) );
			return angle >= cullAngle && angle <= 180 - cullAngle;
		}

		public bool UpdateDragCenterRaycast( string name, Func<Vector3?> raycast, out Vector3 movement )
		{
			movement = default;
			using var scope = Scope( name );
			if ( !Pressed.This || raycast() is not Vector3 hit ) return false;
			movement = (hit - Transform.Position) * Transform.Rotation.Inverse;
			return true;
		}

		public bool UpdateDragBox( string name, Rotation rotation, out Vector3 movement )
		{
			movement = default;
			using var scope = Scope( name, new Transform( 0, rotation ) );
			if ( !Pressed.This ) return false;
			var cameraRotation = Transform.RotationToLocal( Camera.Rotation );
			movement = (cameraRotation.Right + cameraRotation.Up).Dot( GetMouseDelta( 0, cameraRotation.Forward ) );
			return movement != Vector3.Zero;
		}

		public bool UpdateDragSquare( string name, Rotation rotation, out Vector3 movement )
		{
			movement = default;
			using var scope = Scope( name, new Transform( 0, rotation ) );
			if ( !Pressed.This ) return false;
			movement = GetMouseDelta( 0, Vector3.Forward ) * rotation;
			return movement != Vector3.Zero;
		}

		public static IDisposable PushFixedScale( float? scale = null )
		{
			scale ??= Settings.GizmoScale;
			if ( Camera.Ortho )
			{
				var dpiScale = Camera.Attributes.GetFloat( "EditorGizmoDpiScale" );
				scale *= Camera.OrthoHeight * (dpiScale > 0
					? 6.0f * dpiScale / MathF.Max( Camera.Size.y, 1.0f )
					: 0.006f);
			}
			else
			{
				scale *= 1024.0f / Camera.Size.Length;
				scale *= Camera.Position.Distance( Transform.Position ) * Camera.FieldOfView.DegreeToRadian() * 0.006f;
			}

			var scope = Scope( "WorldScale" );
			Transform = Transform.WithScale( scale.Value );
			return scope;
		}
	}
}
