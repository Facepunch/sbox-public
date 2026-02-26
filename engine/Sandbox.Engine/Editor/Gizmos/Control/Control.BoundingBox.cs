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

			var db = Gizmo.Hitbox.DepthBias;
			Gizmo.Hitbox.DepthBias = 0.01f;

			Hitbox.Sphere( new Sphere( 0, hoverSphereRadius * Transform.UniformScale ) );

			Vector3 arrowFrom = Vector3.Forward * -(actualArrowHeadLength + actualSphereRadius);
			Vector3 arrowTo = Vector3.Forward * -actualSphereRadius;
			Hitbox.Sphere( new Sphere( arrowFrom / 2, hoverArrowHeadRadius * Transform.UniformScale ) );

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
			return BoundingBox( name, value, out outValue, out _ );
		}

		public bool BoundingBox( string name, BBox value, out BBox outValue, out bool outPressed )
		{
			outValue = value;

			using ( Scope( name ) )
			{
				Transform = Transform.ToWorld( new Transform( value.Center ) );

				var halfSize = value.Size * 0.5f;
				var resized = false;
				var resizeDist = 0.0f;
				var resizeAxis = Vector3.Zero;
				var pressed = false;

				if ( ArrowPoint( "Forward", Vector3.Forward, halfSize.x, Colors.Forward, out var forwardDist, ref pressed ) )
				{
					resized = true;
					resizeDist = forwardDist;
					resizeAxis = Vector3.Forward;
				}

				if ( ArrowPoint( "Backward", Vector3.Backward, halfSize.x, Colors.Forward, out var backwardDist, ref pressed ) )
				{
					resized = true;
					resizeDist = backwardDist;
					resizeAxis = Vector3.Backward;
				}

				if ( ArrowPoint( "Up", Vector3.Up, halfSize.z, Colors.Up, out var upDist, ref pressed ) )
				{
					resized = true;
					resizeDist = upDist;
					resizeAxis = Vector3.Up;
				}

				if ( ArrowPoint( "Down", Vector3.Down, halfSize.z, Colors.Up, out var downDist, ref pressed ) )
				{
					resized = true;
					resizeDist = downDist;
					resizeAxis = Vector3.Down;
				}

				if ( ArrowPoint( "Left", Vector3.Left, halfSize.y, Colors.Left, out var leftDist, ref pressed ) )
				{
					resized = true;
					resizeDist = leftDist;
					resizeAxis = Vector3.Left;
				}

				if ( ArrowPoint( "Right", Vector3.Right, halfSize.y, Colors.Left, out var rightDist, ref pressed ) )
				{
					resized = true;
					resizeDist = rightDist;
					resizeAxis = Vector3.Right;
				}

				outPressed = pressed;

				if ( resized && !resizeDist.AlmostEqual( 0 ) )
				{
					var center = value.Center + resizeAxis * (resizeDist * 0.5f);
					halfSize = value.Size + resizeAxis.Abs() * resizeDist;
					outValue = BBox.FromPositionAndSize( center, halfSize );

					return true;
				}
			}

			return false;
		}
	}
}



