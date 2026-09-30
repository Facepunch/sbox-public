using NativeEngine;
using Sandbox.Utility;
using System.Linq;
using System.Runtime.InteropServices;

namespace Sandbox;

partial class PhysicsBody2d
{
	bool _isSensor;

	b2ShapeDef DefaultShapeDef()
	{
		var def = Box2d.b2DefaultShapeDef();
		def.density = 1f;
		def.enableContactEvents = true;
		def.enableHitEvents = true;
		def.enableSensorEvents = true;
		def.enableCustomFiltering = true;
		def.isSensor = _isSensor;
		return def;
	}

	PhysicsShape2d TryCreateShape( b2ShapeId shapeId, bool rebuildMass = true )
	{
		if ( !Box2d.b2Shape_IsValid( shapeId ) )
			return null;

		var shape = new PhysicsShape2d( shapeId, Owner );
		_shapes.Add( shape );

		if ( rebuildMass )
			RebuildMass();

		return shape;
	}

	public override PhysicsShape AddBoxShape( Vector3 position, Rotation rotation, Vector3 extent, bool rebuildMass = true )
	{
		var polygon = Box2d.b2MakeOffsetBox( MathF.Abs( extent.x ), MathF.Abs( extent.y ), position, rotation );
		var shapeId = Box2d.b2CreatePolygonShape( BodyId, DefaultShapeDef(), polygon );
		return TryCreateShape( shapeId, rebuildMass )?.Owner;
	}

	public override PhysicsShape AddBoxShape( BBox box, Rotation rotation, bool rebuildMass = true )
	{
		var center = box.Center;
		var half = box.Size * 0.5f;
		var polygon = Box2d.b2MakeOffsetBox( MathF.Abs( half.x ), MathF.Abs( half.y ), center, rotation );
		var shapeId = Box2d.b2CreatePolygonShape( BodyId, DefaultShapeDef(), polygon );
		return TryCreateShape( shapeId, rebuildMass )?.Owner;
	}

	public override PhysicsShape AddSphereShape( Vector3 center, float radius, bool rebuildMass = true )
	{
		var circle = new b2Circle
		{
			center = center,
			radius = radius
		};
		var shapeId = Box2d.b2CreateCircleShape( BodyId, DefaultShapeDef(), circle );
		return TryCreateShape( shapeId, rebuildMass )?.Owner;
	}

	public override PhysicsShape AddSphereShape( in Sphere sphere, bool rebuildMass = true )
		=> AddSphereShape( sphere.Center, sphere.Radius, rebuildMass );

	public override unsafe PhysicsShape AddHullShape( Vector3 position, Rotation rotation, Span<Vector3> points, bool rebuildMass = true )
	{
		var count = Math.Min( points.Length, 8 );
		var pts = stackalloc b2Vec2[count];

		for ( int i = 0; i < count; i++ )
			pts[i] = points[i];

		var hull = Box2d.b2ComputeHull( (nint)pts, count );
		if ( hull.count < 3 )
			return null;

		var polygon = Box2d.b2MakeOffsetPolygon( (nint)(&hull), position, rotation );
		var shapeId = Box2d.b2CreatePolygonShape( BodyId, DefaultShapeDef(), polygon );
		return TryCreateShape( shapeId, rebuildMass )?.Owner;
	}

	public override PhysicsShape AddHullShape( Vector3 position, Rotation rotation, List<Vector3> points, bool rebuildMass = true )
		=> AddHullShape( position, rotation, CollectionsMarshal.AsSpan( points ), rebuildMass );

	public override PhysicsShape AddCapsuleShape( Vector3 center, Vector3 center2, float radius, bool rebuildMass = true )
	{
		b2Vec2 c1 = center;
		b2Vec2 c2 = center2;
		// If both centers project to the same 2D point, fall back to a circle
		if ( (c2.x - c1.x).AlmostEqual( 0 ) && (c2.y - c1.y).AlmostEqual( 0 ) )
			return AddSphereShape( center, radius, rebuildMass );

		var capsule = new b2Capsule
		{
			center1 = c1,
			center2 = c2,
			radius = radius
		};
		var shapeId = Box2d.b2CreateCapsuleShape( BodyId, DefaultShapeDef(), capsule );
		return TryCreateShape( shapeId, rebuildMass )?.Owner;
	}

	public override PhysicsShape AddPlaneShape( Vector3 center, Rotation rotation, Vector2 size, bool rebuildMass = true )
	{
		return AddBoxShape( BBox.FromPositionAndSize( center, size ), rotation, rebuildMass );
	}

	public override int ShapeCount => _shapes.Count;

	public override IEnumerable<PhysicsShape> Shapes => _shapes.Select( s => s.Owner );

	public override void ClearShapes()
	{
		foreach ( var shape in _shapes.ToArray() )
		{
			shape.Remove();
		}

		_shapes.Clear();
		RebuildMass();
	}

	internal override IDisposable TriggerScope()
	{
		_isSensor = true;
		return new DisposeAction( () => _isSensor = false );
	}

	unsafe PhysicsShape2d AddConvexPolygon( ReadOnlySpan<Vector2> points, bool rebuildMass = true )
	{
		if ( points.Length < 3 || points.Length > 8 )
			return null;

		var pts = stackalloc b2Vec2[points.Length];
		for ( int i = 0; i < points.Length; i++ )
			pts[i] = points[i];

		var hull = Box2d.b2ComputeHull( (nint)pts, points.Length );
		if ( hull.count < 3 )
			return null;

		var polygon = Box2d.b2MakePolygon( (nint)(&hull), 0f );
		var shapeId = Box2d.b2CreatePolygonShape( BodyId, DefaultShapeDef(), polygon );
		return TryCreateShape( shapeId, rebuildMass );
	}

	internal List<PhysicsShape> AddDecomposedPolygons( List<Vector2[]> polygons )
	{
		var shapes = new List<PhysicsShape>( polygons.Count );

		foreach ( var poly in polygons )
		{
			var shape = AddConvexPolygon( poly, rebuildMass: false );
			if ( shape is not null )
				shapes.Add( shape.Owner );
		}

		if ( shapes.Count > 0 )
			RebuildMass();

		return shapes;
	}

	internal IEnumerable<PhysicsShape> AddHullPartShapes( PhysicsGroupDescription.BodyPart.HullPart part, Transform transform )
	{
		var points = part.GetPoints().ToArray();
		var polygons = ConvexDecomposition2D.DecomposeAsHull( points, transform );
		return AddDecomposedPolygons( polygons );
	}

	internal IEnumerable<PhysicsShape> AddMeshPartShapes( PhysicsGroupDescription.BodyPart.MeshPart part, Transform transform )
	{
		var vertices = part.GetVertices();
		var indices = part.GetIndices();
		var triangles = ConvexDecomposition2D.DecomposeAsTriangles( vertices, indices, transform );
		return AddDecomposedPolygons( triangles );
	}

	internal override void RemoveShape( PhysicsShape shape )
	{
		if ( shape?._shape is not PhysicsShape2d shape2d )
			return;

		if ( !shape2d.IsValid )
			return;

		shape2d.Remove();
		_shapes.Remove( shape2d );
		RebuildMass();
	}
}
