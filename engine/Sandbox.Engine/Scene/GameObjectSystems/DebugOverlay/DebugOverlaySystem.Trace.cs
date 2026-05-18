namespace Sandbox;

public partial class DebugOverlaySystem
{
	/// <summary>
	/// Draws the result of a physics trace, showing the start and end points, the hit location and normal (if any),
	/// and the traced shape (ray, sphere, box, capsule, cylinder) at both the start and end positions.
	/// </summary>
	public void Trace( SceneTraceResult trace, float duration = 0, bool overlay = false )
	{
		Line( trace.StartPosition, trace.EndPosition, Color.White, duration, Transform.Zero, overlay );
		Point( trace.StartPosition, 1, Color.White, duration, overlay );
		Point( trace.EndPosition, 1, Color.White, duration, overlay );

		if ( trace.Hit )
		{
			Point( trace.HitPosition, 2, Color.Red, duration, overlay );
			Line( trace.HitPosition, trace.HitPosition + trace.Normal * 10, Color.Red, duration, Transform.Zero, overlay );
		}

		var shape = trace.StartShape;

		void DrawShape( Vector3 position, Color color )
		{
			switch ( shape.Type )
			{
				case PhysicsTrace.Request.ShapeType.Sphere:
					Sphere( new Sphere( position, shape.Radius.x ), color, duration, Transform.Zero, overlay );
					break;

				case PhysicsTrace.Request.ShapeType.Box:
					Box( new BBox( shape.Mins, shape.Maxs ), color, duration, new Transform( position, shape.StartRot ), overlay );
					break;

				case PhysicsTrace.Request.ShapeType.Capsule:
					Capsule( new Capsule( shape.Mins, shape.Maxs, shape.Radius.x ), color, duration, new Transform( position, shape.StartRot ), overlay );
					break;

				case PhysicsTrace.Request.ShapeType.Cylinder:
					TaperedCylinder( shape.Mins, shape.Maxs, shape.Radius.x, shape.Radius.y, color, duration, new Transform( position, shape.StartRot ), overlay, 16 );
					break;
			}
		}

		DrawShape( trace.StartPosition, Color.Red );
		DrawShape( trace.EndPosition, trace.Hit ? Color.Green : Color.Red );
	}
}
