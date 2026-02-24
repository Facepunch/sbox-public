namespace Sandbox;

public static partial class Gizmo
{
	public sealed partial class GizmoControls
	{
		/// <summary>
		/// A full 3d rotation gizmo. If rotated will return true and newValue will be the new rotation.
		/// </summary>
		public bool Rotate( string name, Rotation value, out Rotation newValue )
		{
			using var scaler = Gizmo.GizmoControls.PushFixedScale();
			newValue = value;

			bool hasValueChanged = false;
			Rotation delta = Rotation.Identity;

			using ( Sandbox.Gizmo.Scope( name ) )
			{
				Sandbox.Gizmo.Draw.IgnoreDepth = true;

				using ( Sandbox.Gizmo.Scope( "pitch", 0, Rotation.LookAt( Vector3.Left ) ) )
				{
					if ( RotateSingle( "pitch", Sandbox.Gizmo.Colors.Pitch, out var angleDelta ) )
					{
						delta *= Rotation.FromAxis( Vector3.Left, angleDelta );
						hasValueChanged = true;
					}
				}

				using ( Sandbox.Gizmo.Scope( "yaw", 0, Rotation.LookAt( Vector3.Up ) ) )
				{
					if ( RotateSingle( "yaw", Sandbox.Gizmo.Colors.Yaw, out var angleDelta ) )
					{
						delta *= Rotation.FromAxis( Vector3.Up, angleDelta );
						hasValueChanged = true;
					}
				}

				using ( Sandbox.Gizmo.Scope( "roll", 0, Rotation.LookAt( Vector3.Forward ) ) )
				{
					if ( RotateSingle( "roll", Sandbox.Gizmo.Colors.Roll, out var angleDelta ) )
					{
						delta *= Rotation.FromAxis( Vector3.Forward, angleDelta );
						hasValueChanged = true;
					}
				}

				float _angleDelta;
				bool rotateSingle;

				using ( Sandbox.Gizmo.Scope( "view", 0, Gizmo.Transform.Rotation.Inverse * Gizmo.Camera.Rotation ) )
				{
					rotateSingle = RotateSingle( "view", Color.White, out _angleDelta, 22, false );
				}

				using ( Sandbox.Gizmo.Scope( "view", 0, Rotation.Identity ) )
				{
					if ( rotateSingle )
					{
						var camForward = Gizmo.Transform.NormalToLocal( Gizmo.CameraTransform.Rotation.Forward );
						delta *= Rotation.FromAxis( camForward, _angleDelta );
						hasValueChanged = true;
					}
				}

				if ( RotateTrackball( "trackball", Color.White, out var trackballRotation ) )
				{
					delta *= trackballRotation;
					hasValueChanged = true;
				}
			}

			if ( hasValueChanged )
				newValue = value * delta;

			return hasValueChanged;
		}

		[Obsolete( "Use Rotate( string name, out Rotation outValue ) and WorldRotation = outValue rather than *=" )]
		public bool Rotate( string name, out Angles outValue )
		{
			using var scaler = Gizmo.GizmoControls.PushFixedScale();
			outValue = default;

			bool hasValueChanged = false;

			using ( Sandbox.Gizmo.Scope( name ) )
			{
				Sandbox.Gizmo.Draw.IgnoreDepth = true;

				using ( Sandbox.Gizmo.Scope( "pitch", 0, Rotation.LookAt( Vector3.Left ) ) )
				{
					if ( RotateSingle( "pitch", Sandbox.Gizmo.Colors.Pitch, out var angleDelta ) )
					{
						outValue += new Angles( angleDelta, 0, 0 );
						hasValueChanged = true;
					}
				}

				using ( Sandbox.Gizmo.Scope( "yaw", 0, Rotation.LookAt( Vector3.Up ) ) )
				{
					if ( RotateSingle( "yaw", Sandbox.Gizmo.Colors.Yaw, out var angleDelta ) )
					{
						outValue += new Angles( 0, angleDelta, 0 );
						hasValueChanged = true;
					}
				}

				using ( Sandbox.Gizmo.Scope( "roll", 0, Rotation.LookAt( Vector3.Forward ) ) )
				{
					if ( RotateSingle( "roll", Sandbox.Gizmo.Colors.Roll, out var angleDelta ) )
					{
						outValue += new Angles( 0, 0, angleDelta );
						hasValueChanged = true;
					}
				}
			}

			return hasValueChanged;
		}

		/// <summary>
		/// A single rotation axis
		/// </summary>
		public bool RotateSingle( string name, Color color, out float angleDelta, float size = 19.0f, bool useHalfCircle = true )
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
				else if ( useHalfCircle )
				{
					Sandbox.Gizmo.Draw.ScreenBiasedHalfCircle( 0, size );
				}
				else
				{
					Sandbox.Gizmo.Draw.LineCircle( 0, size, sections: 64 );
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

			var delta = Sandbox.Gizmo.GetMouseDistanceVector( pressPoint, dragNormal );
			var angleDifference = delta.Dot( tangent ) * -4.0f;

			//Gizmo.Draw.Plane( pressPoint, dragNormal );

			// don't let scale affect the drag amounts
			angleDifference /= Gizmo.Transform.UniformScale;

			if ( angleDifference == 0.0f ) return false;

			angleDelta = -angleDifference;
			return true;
		}

		/// <summary>
		/// Trackball rotation using camera-relative axes - allows free rotation by dragging a sphere in the center
		/// </summary>
		private bool RotateTrackball( string name, Color color, out Rotation rotationDelta, float size = 16.0f )
		{
			rotationDelta = Rotation.Identity;
			var sphere = new Sphere( Vector3.Zero, size );

			using var _ = Sandbox.Gizmo.Scope( name );

			Sandbox.Gizmo.Draw.Color = color.WithAlpha( 0.15f );

			if ( !Sandbox.Gizmo.IsHovered )
				Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Draw.Color.Darken( 0.33f );

			if ( Sandbox.Gizmo.IsHovered )
			{
				Sandbox.Gizmo.Draw.Color = color.WithAlpha( 0.3f );
			}

			if ( Pressed.Any && !Pressed.This )
			{
				Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Draw.Color.WithAlphaMultiplied( 0.5f );
			}

			if ( Pressed.This )
			{
				Sandbox.Gizmo.Draw.Color = color.WithAlpha( 0.5f );
			}

			Sandbox.Gizmo.Draw.SolidSphere( Vector3.Zero, size, 24, 24 );

			// Add sphere hitbox
			Gizmo.Hitbox.Sphere( sphere );

			if ( !Sandbox.Gizmo.IsHovered || !Pressed.This )
				return false;

			var localCameraRot = Gizmo.LocalCameraTransform.Rotation;

			// Use the same ray transformation as RotateSingle for consistent world/local space behavior
			Vector3 pressPoint = Vector3.Zero;
			var plane = new Plane( 0, Vector3.Forward );
			if ( Camera.Ortho && Camera.Rotation.Forward.Abs() != Transform.Forward.Abs() )
			{
				pressPoint = Pressed.Ray.ToLocal( Gizmo.Transform ).Position;
			}
			else if ( !plane.TryTrace( Pressed.Ray.ToLocal( Gizmo.Transform ), out pressPoint, true ) ) return false;

			var delta = Sandbox.Gizmo.GetMouseDistanceVector( pressPoint, localCameraRot.Forward );

			var dir = Vector3.Cross( delta, localCameraRot.Forward ).Normal;

			var angleDifference = delta.Length * 1.5f;

			// don't let scale affect the drag amounts
			angleDifference /= Gizmo.Transform.UniformScale;

			if ( angleDifference == 0.0f ) return false;

			rotationDelta = Rotation.FromAxis( dir, angleDifference ).Inverse;

			return true;
		}
	}
}
