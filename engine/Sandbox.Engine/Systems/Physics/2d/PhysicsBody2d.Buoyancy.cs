using NativeEngine;

namespace Sandbox;

partial class PhysicsBody2d
{
	public override void ApplyBuoyancy( Plane plane, float fluidDensity, float linearDrag, float angularDrag, Vector3 fluidVelocity, Vector3 gravity, float dt )
	{
		if ( fluidDensity <= 0f || dt <= 0f )
			return;

		if ( BodyType != PhysicsBodyType.Dynamic )
			return;

		float mass = Mass;
		if ( mass <= 0f )
			return;

		var surfaceNormal = new Vector2( plane.Normal.x, plane.Normal.y );
		if ( surfaceNormal.LengthSquared < 1e-6f )
			return;

		surfaceNormal = surfaceNormal.Normal;
		var surfaceOrigin = new Vector2( plane.Position.x, plane.Position.y );

		if ( !ComputeSubmergedArea( surfaceNormal, surfaceOrigin, out float totalArea, out float submergedArea, out Vector2 centerOfBuoyancy ) )
			return;

		if ( totalArea <= 0f || submergedArea <= 0f )
			return;

		float submergedFraction = Math.Clamp( submergedArea / totalArea, 0f, 1f );
		float lengthUnits = Box2d.b2GetLengthUnitsPerMeter();

		float fluidDensityPerVolume = fluidDensity / (lengthUnits * lengthUnits * lengthUnits);
		float displacedVolume = totalArea * lengthUnits;
		float displacedMass = fluidDensityPerVolume * displacedVolume;

		const float minSubmergedFraction = 0.4f;
		displacedMass = MathF.Min( displacedMass, mass / minSubmergedFraction );

		float gravityScale = GravityEnabled ? GravityScale : 0f;
		var buoyancyImpulse = -(displacedMass * submergedFraction * gravityScale * dt) * gravity;
		ApplyImpulseAt( new Vector3( centerOfBuoyancy.x, centerOfBuoyancy.y, 0f ), buoyancyImpulse );

		ApplyFluidDrag( totalArea, submergedFraction, fluidVelocity, gravity, linearDrag, angularDrag, lengthUnits, mass, dt );
	}

	void ApplyFluidDrag( float totalArea, float submergedFraction, Vector3 fluidVelocity, Vector3 gravity, float linearDrag, float angularDrag, float lengthUnits, float mass, float dt )
	{
		var relativeVelocity = (Velocity - fluidVelocity).WithZ( 0f );

		float bodySize = MathF.Sqrt( totalArea ) / lengthUnits;
		float sizeDragScale = 1f / MathF.Max( bodySize, 0.01f );

		const float linearDragScale = 30f;
		float linearDragFactor = Math.Clamp( linearDrag * linearDragScale * submergedFraction * sizeDragScale * dt, 0f, 1f );
		if ( linearDragFactor > 0f )
			ApplyImpulse( -linearDragFactor * mass * relativeVelocity );

		const float angularDragScale = 15f;
		float angularDragFactor = Math.Clamp( angularDrag * angularDragScale * submergedFraction * dt, 0f, 1f );
		if ( angularDragFactor > 0f )
		{
			float inertia = Box2d.b2Body_GetRotationalInertia( BodyId );
			float angularVelocity = Box2d.b2Body_GetAngularVelocity( BodyId );
			ApplyAngularImpulse( new Vector3( 0f, 0f, -angularDragFactor * inertia * angularVelocity ) );
		}

		ApplyRiseDrag( relativeVelocity, gravity, submergedFraction, sizeDragScale, lengthUnits, mass, dt );
	}

	void ApplyRiseDrag( Vector3 relativeVelocity, Vector3 gravity, float submergedFraction, float sizeDragScale, float lengthUnits, float mass, float dt )
	{
		float gravityMagnitude = gravity.Length;
		if ( gravityMagnitude < 1e-6f )
			return;

		var up = gravity * (-1f / gravityMagnitude);
		float riseSpeed = Vector3.Dot( relativeVelocity, up );
		if ( riseSpeed <= 0f )
			return;

		const float quadraticDragPerMeter = 0.5f;
		float dragCoefficient = (quadraticDragPerMeter / lengthUnits) * submergedFraction * sizeDragScale;
		float riseDragFactor = 1f / (1f + dragCoefficient * riseSpeed * dt) - 1f;
		ApplyImpulse( (mass * riseDragFactor * riseSpeed) * up );
	}

	bool ComputeSubmergedArea( Vector2 normal, Vector2 origin, out float totalArea, out float submergedArea, out Vector2 centerOfBuoyancy )
	{
		totalArea = 0f;
		submergedArea = 0f;
		centerOfBuoyancy = default;

		var transform = Box2d.b2Body_GetTransform( BodyId );

		Span<Vector2> outline = stackalloc Vector2[36];
		Span<Vector2> submerged = stackalloc Vector2[40];

		var weightedCentroid = Vector2.Zero;
		bool any = false;

		foreach ( var shape in _shapes )
		{
			if ( !shape.IsValid )
				continue;

			int outlineCount = GetWorldOutline( shape, transform, outline );
			if ( outlineCount < 3 )
				continue;

			var (fullArea, _) = PolygonAreaCentroid( outline[..outlineCount] );
			totalArea += MathF.Abs( fullArea );
			any = true;

			int submergedCount = ClipSubmerged( outline[..outlineCount], normal, origin, submerged );
			if ( submergedCount < 3 )
				continue;

			var (area, centroid) = PolygonAreaCentroid( submerged[..submergedCount] );
			area = MathF.Abs( area );
			if ( area <= 0f )
				continue;

			submergedArea += area;
			weightedCentroid += centroid * area;
		}

		if ( submergedArea > 0f )
			centerOfBuoyancy = weightedCentroid * (1f / submergedArea);

		return any;
	}

	static int GetWorldOutline( PhysicsShape2d shape, b2Transform transform, Span<Vector2> buffer )
	{
		switch ( Box2d.b2Shape_GetType( shape.ShapeId ) )
		{
			case b2ShapeType.b2_polygonShape:
				{
					var polygon = Box2d.b2Shape_GetPolygon( shape.ShapeId );
					int count = Math.Min( polygon.count, 8 );

					Span<b2Vec2> vertices =
					[
						polygon.v0, polygon.v1, polygon.v2, polygon.v3,
						polygon.v4, polygon.v5, polygon.v6, polygon.v7,
					];

					for ( int i = 0; i < count; i++ )
					{
						Vector2 world = transform.TransformPoint( vertices[i] );
						buffer[i] = world;
					}

					return count;
				}
			case b2ShapeType.b2_circleShape:
				{
					var circle = Box2d.b2Shape_GetCircle( shape.ShapeId );
					Vector2 center = transform.TransformPoint( circle.center );
					return TessellateCircle( center, circle.radius, buffer );
				}
			case b2ShapeType.b2_capsuleShape:
				{
					var capsule = Box2d.b2Shape_GetCapsule( shape.ShapeId );
					Vector2 c1 = transform.TransformPoint( capsule.center1 );
					Vector2 c2 = transform.TransformPoint( capsule.center2 );
					return TessellateCapsule( c1, c2, capsule.radius, buffer );
				}
			default:
				return 0;
		}
	}

	static int TessellateCircle( Vector2 center, float radius, Span<Vector2> buffer )
	{
		const int segments = 16;
		for ( int i = 0; i < segments; i++ )
		{
			float angle = 2f * MathF.PI * i / segments;
			buffer[i] = center + new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) ) * radius;
		}

		return segments;
	}

	static int TessellateCapsule( Vector2 c1, Vector2 c2, float radius, Span<Vector2> buffer )
	{
		var axis = c2 - c1;
		float length = axis.Length;
		var direction = length > 1e-6f ? axis / length : new Vector2( 1f, 0f );
		float baseAngle = MathF.Atan2( direction.y, direction.x );

		const int capSegments = 8;
		int count = 0;

		for ( int i = 0; i <= capSegments; i++ )
		{
			float angle = baseAngle - MathF.PI * 0.5f + MathF.PI * i / capSegments;
			buffer[count++] = c2 + new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) ) * radius;
		}

		for ( int i = 0; i <= capSegments; i++ )
		{
			float angle = baseAngle + MathF.PI * 0.5f + MathF.PI * i / capSegments;
			buffer[count++] = c1 + new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) ) * radius;
		}

		return count;
	}

	static (float Area, Vector2 Centroid) PolygonAreaCentroid( ReadOnlySpan<Vector2> points )
	{
		int n = points.Length;
		if ( n < 3 )
			return (0f, points.Length > 0 ? points[0] : default);

		float area = 0f;
		float cx = 0f;
		float cy = 0f;

		for ( int i = 0; i < n; i++ )
		{
			var a = points[i];
			var b = points[(i + 1) % n];
			float cross = a.x * b.y - b.x * a.y;
			area += cross;
			cx += (a.x + b.x) * cross;
			cy += (a.y + b.y) * cross;
		}

		area *= 0.5f;
		if ( MathF.Abs( area ) < 1e-9f )
			return (0f, points[0]);

		float inv = 1f / (6f * area);
		return (area, new Vector2( cx * inv, cy * inv ));
	}

	static int ClipSubmerged( ReadOnlySpan<Vector2> points, Vector2 normal, Vector2 origin, Span<Vector2> output )
	{
		int n = points.Length;
		int count = 0;

		for ( int i = 0; i < n; i++ )
		{
			var current = points[i];
			var next = points[(i + 1) % n];

			float distCurrent = (current.x - origin.x) * normal.x + (current.y - origin.y) * normal.y;
			float distNext = (next.x - origin.x) * normal.x + (next.y - origin.y) * normal.y;

			bool currentInside = distCurrent <= 0f;
			bool nextInside = distNext <= 0f;

			if ( currentInside )
				output[count++] = current;

			if ( currentInside != nextInside )
			{
				float t = distCurrent / (distCurrent - distNext);
				output[count++] = current + (next - current) * t;
			}
		}

		return count;
	}
}
