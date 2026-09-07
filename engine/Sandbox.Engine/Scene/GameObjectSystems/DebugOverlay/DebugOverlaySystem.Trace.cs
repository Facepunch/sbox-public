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

		if ( Scene.Is2D )
		{
			DrawTraceShape2d( trace.StartPosition, shape, Color.Red, duration, overlay );
			DrawTraceShape2d( trace.EndPosition, shape, trace.Hit ? Color.Green : Color.Red, duration, overlay );
		}
		else
		{
			DrawTraceShape3d( trace.StartPosition, shape, Color.Red, duration, overlay );
			DrawTraceShape3d( trace.EndPosition, shape, trace.Hit ? Color.Green : Color.Red, duration, overlay );
		}
	}

	void DrawTraceShape3d( Vector3 position, PhysicsTrace.Request.Shape shape, Color color, float duration, bool overlay )
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

	void DrawTraceShape2d( Vector3 position, PhysicsTrace.Request.Shape shape, Color color, float duration, bool overlay )
	{
		var rot = shape.StartRot;

		switch ( shape.Type )
		{
			case PhysicsTrace.Request.ShapeType.Sphere:
				DrawCircle2d( position, shape.Radius.x, color, duration, overlay );
				break;

			case PhysicsTrace.Request.ShapeType.Box:
				{
					var mins = shape.Mins;
					var maxs = shape.Maxs;
					var p0 = position + rot * new Vector3( mins.x, mins.y, 0f );
					var p1 = position + rot * new Vector3( maxs.x, mins.y, 0f );
					var p2 = position + rot * new Vector3( maxs.x, maxs.y, 0f );
					var p3 = position + rot * new Vector3( mins.x, maxs.y, 0f );
					Line( p0, p1, color, duration, Transform.Zero, overlay );
					Line( p1, p2, color, duration, Transform.Zero, overlay );
					Line( p2, p3, color, duration, Transform.Zero, overlay );
					Line( p3, p0, color, duration, Transform.Zero, overlay );
					break;
				}

			case PhysicsTrace.Request.ShapeType.Capsule:
				{
					var a = position + rot * shape.Mins;
					var b = position + rot * shape.Maxs;
					var radius = shape.Radius.x;
					DrawCapsule2d( a, b, radius, color, duration, overlay );
					break;
				}
		}
	}

	void DrawCircle2d( Vector3 center, float radius, Color color, float duration, bool overlay, int segments = 24 )
	{
		var step = MathF.PI * 2f / segments;
		var prev = center + new Vector3( radius, 0f, 0f );

		for ( int i = 1; i <= segments; i++ )
		{
			var angle = step * i;
			var next = center + new Vector3( MathF.Cos( angle ) * radius, MathF.Sin( angle ) * radius, 0f );
			Line( prev, next, color, duration, Transform.Zero, overlay );
			prev = next;
		}
	}

	void DrawCapsule2d( Vector3 a, Vector3 b, float radius, Color color, float duration, bool overlay, int segments = 12 )
	{
		var diff = b - a;
		var dir = diff.IsNearZeroLength ? Vector3.Up : diff.Normal;
		var perp = new Vector3( -dir.y, dir.x, 0f );

		Line( a + perp * radius, b + perp * radius, color, duration, Transform.Zero, overlay );
		Line( a - perp * radius, b - perp * radius, color, duration, Transform.Zero, overlay );

		DrawArc2d( a, perp, -dir, radius, segments, color, duration, overlay );
		DrawArc2d( b, perp, dir, radius, segments, color, duration, overlay );
	}

	void DrawArc2d( Vector3 center, Vector3 right, Vector3 forward, float radius, int segments, Color color, float duration, bool overlay )
	{
		for ( int i = 0; i < segments; i++ )
		{
			float a0 = (i / (float)segments) * MathF.PI;
			float a1 = ((i + 1) / (float)segments) * MathF.PI;

			var p0 = center + (right * MathF.Cos( a0 ) + forward * MathF.Sin( a0 )) * radius;
			var p1 = center + (right * MathF.Cos( a1 ) + forward * MathF.Sin( a1 )) * radius;
			Line( p0, p1, color, duration, Transform.Zero, overlay );
		}
	}
}
