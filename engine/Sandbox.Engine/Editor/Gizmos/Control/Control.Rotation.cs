namespace Sandbox;

public static partial class Gizmo
{
	public sealed partial class GizmoControls
	{
		/// <summary>
		/// A full 3d rotation gizmo. If rotated will return true and newValue will be the new rotation.
		/// </summary>
		public bool UpdateRotation( string name, Rotation value, out Rotation newValue )
		{
			using var scaler = Gizmo.GizmoControls.PushFixedScale();
			newValue = value;

			bool hasValueChanged = false;
			Rotation delta = Rotation.Identity;

			using ( Sandbox.Gizmo.Scope( name ) )
			{
				using ( Sandbox.Gizmo.Scope( "pitch", 0, Rotation.LookAt( Vector3.Left ) ) )
				{
					if ( UpdateRotationAxis( "pitch", out var angleDelta ) )
					{
						delta *= Rotation.FromAxis( Vector3.Left, angleDelta );
						hasValueChanged = true;
					}
				}

				using ( Sandbox.Gizmo.Scope( "yaw", 0, Rotation.LookAt( Vector3.Up ) ) )
				{
					if ( UpdateRotationAxis( "yaw", out var angleDelta ) )
					{
						delta *= Rotation.FromAxis( Vector3.Up, angleDelta );
						hasValueChanged = true;
					}
				}

				using ( Sandbox.Gizmo.Scope( "roll", 0, Rotation.LookAt( Vector3.Forward ) ) )
				{
					if ( UpdateRotationAxis( "roll", out var angleDelta ) )
					{
						delta *= Rotation.FromAxis( Vector3.Forward, angleDelta );
						hasValueChanged = true;
					}
				}

				float _angleDelta;
				bool rotateSingle;

				using ( Sandbox.Gizmo.Scope( "view", 0, Gizmo.Transform.Rotation.Inverse * Gizmo.Camera.Rotation ) )
				{
					rotateSingle = UpdateRotationAxis( "view", out _angleDelta );
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
		public bool UpdateRotationAxis( string name, out float angleDelta )
		{
			angleDelta = 0;

			using var x = Sandbox.Gizmo.Scope( name );

			if ( !Pressed.This )
				return false;

			var plane = new Plane( 0, Vector3.Forward );


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

			Vector3 dragNormal = localCameera.Forward;

			var facingCamera = localCameera.Forward.Dot( Vector3.Forward );

			if ( facingCamera > 0.5f )
				dragNormal = Vector3.Forward;

			var delta = Sandbox.Gizmo.GetMouseDistanceVector( pressPoint, dragNormal );
			var angleDifference = delta.Dot( tangent ) * -4.0f;

			angleDelta = -angleDifference;
			return CurrentRay != (WasLeftMousePressed ? Pressed.Ray : PreviousRay);
		}
	}

	public sealed partial class GizmoDraw
	{
		public void Rotate( string name )
		{
			using var scaler = GizmoControls.PushFixedScale();
			using var scope = Scope( name );
			IgnoreDepth = true;
			using ( Scope( "pitch", 0, Rotation.LookAt( Vector3.Left ) ) )
				RotationAxis( "pitch", Colors.Pitch );
			using ( Scope( "yaw", 0, Rotation.LookAt( Vector3.Up ) ) )
				RotationAxis( "yaw", Colors.Yaw );
			using ( Scope( "roll", 0, Rotation.LookAt( Vector3.Forward ) ) )
				RotationAxis( "roll", Colors.Roll );
			using ( Scope( "view", 0, Transform.Rotation.Inverse * Camera.Rotation ) )
				RotationAxis( "view", Color.White, 22, false );
		}

		public void RotationAxis( string name, Color color, float size = 19, bool useHalfCircle = true )
		{
			using var scope = Scope( name );
			LineThickness = 3;
			Color = IsHovered ? color : color.Darken( 0.33f );
			if ( Pressed.Any && !Pressed.This )
			{
				LineThickness = 2;
				Color = Color.WithAlphaMultiplied( 0.5f );
			}
			using ( Hitbox.LineScope() )
			{
				if ( Pressed.This || !useHalfCircle )
					LineCircle( 0, size, sections: 64 );
				else
					ScreenBiasedHalfCircle( 0, size );
			}
		}
	}
}
