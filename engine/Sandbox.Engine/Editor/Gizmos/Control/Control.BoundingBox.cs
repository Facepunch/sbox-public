namespace Sandbox;

public static partial class Gizmo
{
	public sealed partial class GizmoControls
	{
		private static bool ArrowPoint( string name, Vector3 direction, float length, Color color, out float distance, ref bool pressed )
		{
			distance = 0.0f;
			var rotation = Rotation.LookAt( direction, Vector3.Up );
			using var x = Scope( name, direction * length, rotation );
			var worldBoxScale = Transform.UniformScale;
			using var scaler = PushFixedScale();
			length /= Transform.UniformScale;

			const float sphereRadius = 1.3f;
			const float hoverSphereRadius = sphereRadius * 1.25f;
			float actualSphereRadius = IsHovered ? hoverSphereRadius : sphereRadius;

			float arrowHeadRadius = sphereRadius * 0.9f;
			float hoverArrowHeadRadius = arrowHeadRadius * 1.25f;
			float actualArrowHeadRadius = IsHovered ? hoverArrowHeadRadius : arrowHeadRadius;
			float actualArrowHeadLength = actualArrowHeadRadius * 2;

			if ( !Pressed.This && length.AlmostEqual( 0 ) )
				return false;

			var db = Gizmo.Hitbox.DepthBias;
			Gizmo.Hitbox.DepthBias = 0.01f;

			Hitbox.Sphere( new Sphere( 0, hoverSphereRadius ) );

			Vector3 arrowFrom = Vector3.Forward * -(actualArrowHeadLength + actualSphereRadius);
			Vector3 arrowTo = Vector3.Forward * -actualSphereRadius;
			Hitbox.Sphere( new Sphere( arrowFrom / 2, hoverArrowHeadRadius ) );

			Gizmo.Hitbox.DepthBias = db;
			color = IsHovered || Pressed.This ? Colors.Active : color;
			Draw.IgnoreDepth = true;
			Draw.Color = color;
			Draw.SolidSphere( Vector3.Zero, actualSphereRadius );

			// Draw an arrow pointing inward from the box to the sphere
			Draw.Arrow( arrowFrom, arrowTo, actualArrowHeadLength, actualArrowHeadRadius );

			if ( Pressed.This )
			{
				pressed = true;

				Transform = Transform.ToWorld( new Transform( Vector3.Backward * (length * 2.0f) ) );
				using ( PushFixedScale() )
				{
					Transform = Transform.WithRotation( Rotation.LookAt( Transform.Rotation.Forward, Camera.Rotation.Forward ) );
					var delta = GetMouseDelta( Vector3.Zero, Vector3.Up );
					distance = Vector3.Forward.Dot( GetMouseDelta( Vector3.Zero, Vector3.Up ) );
					distance /= worldBoxScale;
				}
			}

			return distance != 0.0f;
		}

		public bool BoundingBox( string name, BBox value, out BBox outValue )
		{
			return BoundingBox( name, value, out outValue, out _, out _ );
		}

		public bool BoundingBox( string name, BBox value, out BBox outValue, out bool outPressed )
		{
			return BoundingBox( name, value, out outValue, out outPressed, out _ );
		}

		public bool BoundingBox( string name, BBox value, out BBox outValue, out bool outPressed, out Vector3 outResizeAxis )
		{
			outValue = value;
			outResizeAxis = default;

			using ( Scope( name ) )
			{
				Transform = Transform.ToWorld( new Transform( value.Center ) );

				var halfSize = value.Size * 0.5f;
				var resizeDist = 0.0f;
				var resizeAxis = Vector3.Zero;
				var pressed = false;

				if ( ArrowPoint( "Forward", Vector3.Forward, halfSize.x, Colors.Forward, out var fd, ref pressed ) )
					(resizeDist, resizeAxis) = (fd, Vector3.Forward);

				if ( ArrowPoint( "Backward", Vector3.Backward, halfSize.x, Colors.Forward, out var bd, ref pressed ) )
					(resizeDist, resizeAxis) = (bd, Vector3.Backward);

				if ( ArrowPoint( "Up", Vector3.Up, halfSize.z, Colors.Up, out var ud, ref pressed ) )
					(resizeDist, resizeAxis) = (ud, Vector3.Up);

				if ( ArrowPoint( "Down", Vector3.Down, halfSize.z, Colors.Up, out var dd, ref pressed ) )
					(resizeDist, resizeAxis) = (dd, Vector3.Down);

				if ( ArrowPoint( "Left", Vector3.Left, halfSize.y, Colors.Left, out var ld, ref pressed ) )
					(resizeDist, resizeAxis) = (ld, Vector3.Left);

				if ( ArrowPoint( "Right", Vector3.Right, halfSize.y, Colors.Left, out var rd, ref pressed ) )
					(resizeDist, resizeAxis) = (rd, Vector3.Right);

				outPressed = pressed;

				if ( resizeDist.AlmostEqual( 0 ) )
					return false;

				outResizeAxis = resizeAxis;
				outValue.Maxs += Vector3.Max( resizeAxis, Vector3.Zero ) * resizeDist;
				outValue.Mins += Vector3.Min( resizeAxis, Vector3.Zero ) * resizeDist;
				return true;
			}
		}
	}
}

