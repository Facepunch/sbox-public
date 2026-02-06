namespace Sandbox;

public static partial class Gizmo
{
	public sealed partial class GizmoControls
	{
		/// <summary>
		/// A full 3d rotation gizmo. If rotated will return true and outValue will be the rotation delta.
		/// </summary>
		public bool Rotate( string name, out Rotation outValue )
		{
			using var scaler = Gizmo.GizmoControls.PushFixedScale();
			outValue = Rotation.Identity;

			bool hasValueChanged = false;

			using ( Sandbox.Gizmo.Scope( name ) )
			{
				Sandbox.Gizmo.Draw.IgnoreDepth = true;

				using ( Sandbox.Gizmo.Scope( "pitch", 0, Rotation.LookAt( Vector3.Left ) ) )
				{
					if ( RotateSingle( "pitch", Sandbox.Gizmo.Colors.Pitch, out var angleDelta ) )
					{
						outValue *= Rotation.FromAxis( Vector3.Left, angleDelta );
						hasValueChanged = true;
					}
				}

				using ( Sandbox.Gizmo.Scope( "yaw", 0, Rotation.LookAt( Vector3.Up ) ) )
				{
					if ( RotateSingle( "yaw", Sandbox.Gizmo.Colors.Yaw, out var angleDelta ) )
					{
						outValue *= Rotation.FromAxis( Vector3.Up, angleDelta );
						hasValueChanged = true;
					}
				}

				using ( Sandbox.Gizmo.Scope( "roll", 0, Rotation.LookAt( Vector3.Forward ) ) )
				{
					if ( RotateSingle( "roll", Sandbox.Gizmo.Colors.Roll, out var angleDelta ) )
					{
						outValue *= Rotation.FromAxis( Vector3.Forward, angleDelta );
						hasValueChanged = true;
					}
				}

				float _angleDelta;
				bool rotateSingle;

				using ( Sandbox.Gizmo.Scope( "view", 0, Gizmo.Transform.Rotation.Inverse * Gizmo.Camera.Rotation ) )
				{
					rotateSingle = RotateSingle( "view", Color.White, out _angleDelta, 22 );
				}

				using ( Sandbox.Gizmo.Scope( "view", 0, Rotation.Identity ) )
				{
					if ( rotateSingle )
					{
						var camForward = Gizmo.Transform.NormalToLocal( Gizmo.CameraTransform.Rotation.Forward );
						outValue *= Rotation.FromAxis( camForward, _angleDelta );
						hasValueChanged = true;
					}
				}

				if ( RotateTrackball( "trackball", out var trackballRotation ) )
				{
					outValue *= trackballRotation;
					hasValueChanged = true;
				}
			}

			return hasValueChanged;
		}

		/// <summary>
		/// A single rotation axis
		/// </summary>
		public bool RotateSingle( string name, Color color, out float angleDelta, float size = 19.0f )
		{
			angleDelta = 0;

			using var x = Sandbox.Gizmo.Scope( name );

			Sandbox.Gizmo.Draw.LineThickness = 3.0f;
			Sandbox.Gizmo.Draw.Color = color;

			if ( !Sandbox.Gizmo.IsHovered )
				Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Draw.Color.Darken( 0.33f );

			if ( Pressed.Any && !Pressed.This )
			{
				Sandbox.Gizmo.Draw.LineThickness = 2.0f;
				Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Draw.Color.WithAlphaMultiplied( 0.5f );
			}

			using ( Gizmo.Hitbox.LineScope() )
			{
				if ( Pressed.This )
				{
					Sandbox.Gizmo.Draw.LineCircle( 0, size, sections: 64 );
				}
				else
				{
					Sandbox.Gizmo.Draw.ScreenBiasedHalfCircle( 0, size );
				}
			}

			if ( !Sandbox.Gizmo.IsHovered || !Pressed.This )
				return false;

			var plane = new Plane( 0, Vector3.Forward );


			Sandbox.Gizmo.Draw.LineThickness = 3;

			Vector3 pressPoint = Vector3.Zero;
			if ( Camera.Ortho && Camera.Rotation.Forward.Abs() != Transform.Forward.Abs() )
			{
				pressPoint = Pressed.Ray.ToLocal( Gizmo.Transform ).Position;
			}
			else if ( !plane.TryTrace( Pressed.Ray.ToLocal( Gizmo.Transform ), out pressPoint, true ) ) return false;

			Transform localCameera = Gizmo.LocalCameraTransform;

			var pressNormal = pressPoint.Normal;

			// Get a direction adjacent to the circle part we grabbed
			var tangent = Vector3.Cross( pressNormal, Vector3.Forward );

			//Gizmo.Draw.Line( pressNormal * size - tangent * size * 10.0f, (pressNormal * size) + tangent * size * 10.0f );

			Vector3 dragNormal = localCameera.Forward;

			var facingCamera = localCameera.Forward.Dot( Vector3.Forward );

			if ( facingCamera > 0.5f )
				dragNormal = Vector3.Forward;

			var delta = Sandbox.Gizmo.GetMouseDelta( pressPoint, dragNormal );
			var angleDifference = delta.Dot( tangent ) * -4.0f;

			//Gizmo.Draw.Plane( pressPoint, dragNormal );

			// don't let scale affect the drag amounts
			angleDifference /= Gizmo.Transform.UniformScale;

			if ( angleDifference == 0.0f ) return false;

			angleDelta = angleDifference;
			return true;
		}

		/// <summary>
		/// Trackball rotation using camera-relative axes - allows free rotation by dragging a sphere in the center
		/// </summary>
		private bool RotateTrackball( string name, out Rotation rotationDelta )
		{
			rotationDelta = Rotation.Identity;
			float size = 16.0f;
			var sphere = new Sphere( Vector3.Zero, size );

			using var _ = Sandbox.Gizmo.Scope( name );

			// Draw solid white sphere with transparency
			Sandbox.Gizmo.Draw.Color = Color.White.WithAlpha( 0.15f );

			if ( Sandbox.Gizmo.IsHovered )
			{
				Sandbox.Gizmo.Draw.Color = Color.White.WithAlpha( 0.3f );
			}

			if ( Pressed.This )
			{
				Sandbox.Gizmo.Draw.Color = Color.White.WithAlpha( 0.5f );
			}

			// Draw solid sphere with higher polygon count (16 segments)
			Sandbox.Gizmo.Draw.SolidSphere( Vector3.Zero, size, 16, 16 );

			// Add sphere hitbox
			Gizmo.Hitbox.Sphere( sphere );

			if ( !Sandbox.Gizmo.IsHovered || !Pressed.This )
				return false;

			// Get mouse delta projected onto camera view plane
			// This gives us mouse movement in world space relative to the view
			var localCameraRot = Gizmo.LocalCameraTransform.Rotation;
			var delta = Sandbox.Gizmo.GetMouseDelta( 0, localCameraRot.Forward );
			
			// Apply rotation speed similar to PhysGun
			float rotateSpeed = 0.4f;
			
			// Build rotation just like PhysGun's DoRotate:
			// Mouse X (delta along Right axis) rotates around Up axis (yaw)
			// Mouse Y (delta along Up axis) rotates around Right axis (pitch)
			float yawAngle = delta.Dot( localCameraRot.Right ) * rotateSpeed;
			float pitchAngle = delta.Dot( localCameraRot.Up ) * rotateSpeed;
			
			var yawRot = Rotation.FromAxis( Vector3.Up, yawAngle );
			var pitchRot = Rotation.FromAxis( Vector3.Right, pitchAngle );
			
			// Combine rotations
			rotationDelta = yawRot * pitchRot;
			
			return true;
		}
	}
}
