namespace Sandbox;

public static partial class Gizmo
{
	public sealed partial class GizmoControls
	{
		public bool Capsule( string name, Capsule capsule, out Capsule outCapsule, Color color )
		{
			outCapsule = capsule;

			var centerA = capsule.CenterA;
			var centerB = capsule.CenterB;

			var diff = centerB - centerA;
			if ( diff.IsNearZeroLength )
				return false;

			var rot = global::Rotation.LookAt( diff );
			var localCameraRot = Transform.RotationToLocal( Camera.Rotation );

			Draw.LineThickness = 4.0f;
			Draw.Color = color;

			var movement = 0.0f;
			var radius = outCapsule.Radius;

			Vector3 topCenter = 0.0f;
			Vector3 bottomCenter = 0.0f;

			using ( Scope( name ) )
			{
				Draw.IgnoreDepth = true;

				using ( Scope( "radiusCapsuleA", new Transform( centerA, rot ) ) )
				{
					using ( Hitbox.LineScope() )
					{
						var delta = GetMouseDistanceDelta( 0, Rotation.FromAxis( localCameraRot.Forward, 0 ).Forward );

						if ( Pressed.This )
						{
							movement += delta;
						}

						Draw.LineCircle( 0, radius );
					}
				}

				using ( Scope( "radiusCapsuleB", new Transform( centerB, rot ) ) )
				{
					using ( Hitbox.LineScope() )
					{
						var delta = GetMouseDistanceDelta( 0, Rotation.FromAxis( localCameraRot.Forward, 0 ).Forward );

						if ( Pressed.This )
						{
							movement += delta;
						}

						Draw.LineCircle( 0, radius );
					}
				}

				using ( Scope( "captop", new Transform( centerB, rot ) ) )
				{
					Draw.Color = Colors.Up;

					if ( Arrow( "toparrow", Vector3.Forward, out var dist, axisOffset: Vector3.Forward.z * radius, length: 5.0f, head: "cone", girth: 6.0f ) )
					{
						topCenter += diff.Normal * dist;
					}
				}

				using ( Scope( "capbottom", new Transform( centerA, rot ) ) )
				{
					Draw.Color = Colors.Up;

					if ( Arrow( "bottomarrow", Vector3.Backward, out var dist, axisOffset: Vector3.Backward.z * radius, length: 5.0f, head: "cone", girth: 6.0f ) )
					{
						bottomCenter -= diff.Normal * dist;
					}
				}

				if ( movement == 0.0f && topCenter == 0.0f && bottomCenter == 0.0f )
					return false;

				outCapsule.CenterA = centerA + bottomCenter;
				outCapsule.CenterB = centerB + topCenter;
				outCapsule.Radius = radius + movement;

				return true;
			}
		}
	}
}
