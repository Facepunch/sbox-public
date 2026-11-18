namespace Sandbox;

public static partial class Gizmo
{
	public sealed partial class GizmoControls
	{
		public bool UpdateSphere( string name, float radius, out float outRadius )
		{
			outRadius = radius;
			using var scope = PushScope( name, new Transform( 0, Transform.RotationToLocal( Camera.Rotation ) ) );
			if ( !Pressed.This ) return false;
			outRadius += GetMouseDistanceDelta( 0, Vector3.Forward );
			return outRadius != radius;
		}
	}

	public sealed partial class GizmoDraw
	{
		public void Sphere( string name, float radius, Color color )
		{
			using var scope = PushScope( name, new Transform( 0, Transform.RotationToLocal( Camera.Rotation ) ) );
			if ( Pressed.Any && !Pressed.This ) return;
			IgnoreDepth = true;
			LineThickness = 4;
			Color = IsHovered ? color : color.Darken( 0.33f );
			using ( Hitbox.PushLineScope() )
				LineCircle( 0, radius, sections: (int)radius * 2 );
		}
	}
}
