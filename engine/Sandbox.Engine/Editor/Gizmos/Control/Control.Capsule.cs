namespace Sandbox;

public static partial class Gizmo
{
	public sealed partial class GizmoControls
	{
		public bool UpdateCapsule( string name, Capsule capsule, out Capsule outCapsule )
		{
			outCapsule = capsule;
			var diff = capsule.CenterB - capsule.CenterA;
			if ( diff.IsNearZeroLength ) return false;
			var rotation = Rotation.LookAt( diff );
			using var scope = PushScope( name );
			using ( PushScope( "radiusCapsuleA", new Transform( capsule.CenterA, rotation ) ) )
			{
				if ( Pressed.This ) outCapsule.Radius += GetMouseDistanceDelta( 0, Vector3.Forward );
			}
			using ( PushScope( "radiusCapsuleB", new Transform( capsule.CenterB, rotation ) ) )
			{
				if ( Pressed.This ) outCapsule.Radius += GetMouseDistanceDelta( 0, Vector3.Forward );
			}
			using ( PushScope( "captop", new Transform( capsule.CenterB, rotation ) ) )
			{
				if ( UpdateArrow( "toparrow", Vector3.Forward, out var distance, axisOffset: 0 ) )
					outCapsule.CenterB += diff.Normal * distance;
			}
			using ( PushScope( "capbottom", new Transform( capsule.CenterA, rotation ) ) )
			{
				if ( UpdateArrow( "bottomarrow", Vector3.Backward, out var distance, axisOffset: 0 ) )
					outCapsule.CenterA -= diff.Normal * distance;
			}
			return outCapsule.CenterA != capsule.CenterA || outCapsule.CenterB != capsule.CenterB || outCapsule.Radius != capsule.Radius;
		}
	}

	public sealed partial class GizmoDraw
	{
		public void Capsule( string name, Capsule capsule, Color color )
		{
			var diff = capsule.CenterB - capsule.CenterA;
			if ( diff.IsNearZeroLength ) return;
			var rotation = Rotation.LookAt( diff );
			using var scope = PushScope( name );
			IgnoreDepth = true;
			LineThickness = 4;
			Color = color;
			using ( PushScope( "radiusCapsuleA", new Transform( capsule.CenterA, rotation ) ) )
			using ( Hitbox.PushLineScope() )
				LineCircle( 0, capsule.Radius );
			using ( PushScope( "radiusCapsuleB", new Transform( capsule.CenterB, rotation ) ) )
			using ( Hitbox.PushLineScope() )
				LineCircle( 0, capsule.Radius );
			Color = Colors.Up;
			using ( PushScope( "captop", new Transform( capsule.CenterB, rotation ) ) )
				Arrow( "toparrow", Vector3.Forward, axisOffset: 0, length: 5 );
			using ( PushScope( "capbottom", new Transform( capsule.CenterA, rotation ) ) )
				Arrow( "bottomarrow", Vector3.Backward, axisOffset: 0, length: 5 );
		}
	}
}
