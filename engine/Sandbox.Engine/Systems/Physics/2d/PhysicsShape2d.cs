using NativeEngine;
using System.Buffers;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// 2D physics shape backed by a Box2D <see cref="b2ShapeId"/>.
/// </summary>
internal sealed class PhysicsShape2d : PhysicsShapeInternal
{
	const int MaxStackOverlaps = 32;

	b2ShapeId _shapeId;
	internal b2ShapeId ShapeId
	{
		get => Box2d.b2Shape_IsValid( _shapeId )
			? _shapeId
			: throw new InvalidOperationException( "This PhysicsShape has been removed and is no longer valid." );
		set => _shapeId = value;
	}
	GCHandle _handle;

	internal PhysicsShape2d( b2ShapeId shapeId, PhysicsBody body )
	{
		ShapeId = shapeId;
		_body = body;
		Tags = new ManagedTagAccessor();

		_handle = GCHandle.Alloc( this );
		Box2d.b2Shape_SetUserData( ShapeId, GCHandle.ToIntPtr( _handle ) );
	}

	internal static PhysicsShape2d FromUserData( b2ShapeId shapeId )
	{
		if ( !Box2d.b2Shape_IsValid( shapeId ) )
			return null;

		var ptr = Box2d.b2Shape_GetUserData( shapeId );
		if ( ptr == IntPtr.Zero ) return null;
		return GCHandle.FromIntPtr( ptr ).Target as PhysicsShape2d;
	}

	public override bool IsValid => Box2d.b2Shape_IsValid( _shapeId );

	readonly PhysicsBody _body;
	public override PhysicsBody Body => _body;

	public override bool IsTrigger
	{
		get => Box2d.b2Shape_IsSensor( ShapeId );
		set { }
	}

	public override bool EnableSolidCollisions { get; set; } = true;

	public override bool EnableTouch
	{
		get => Box2d.b2Shape_AreContactEventsEnabled( ShapeId );
		set
		{
			Box2d.b2Shape_EnableContactEvents( ShapeId, value );
			Box2d.b2Shape_EnableHitEvents( ShapeId, value );
		}
	}

	public override bool EnableTouchPersists
	{
		get => Box2d.b2Shape_ArePreSolveEventsEnabled( ShapeId );
		set => Box2d.b2Shape_EnablePreSolveEvents( ShapeId, value );
	}

	internal override bool HasNoMass
	{
		set
		{
			_hasNoMass = value;
			ApplyDensity();
		}
	}

	bool _hasNoMass;

	Surface _surface;

	public override Surface Surface
	{
		get => _surface;
		set
		{
			_surface = value;
			UpdateSurface();
		}
	}

	public override string SurfaceMaterial
	{
		get => _surface?.ResourceName;
		set
		{
			_surface = string.IsNullOrWhiteSpace( value ) ? null : Surface.FindByName( value );
			UpdateSurface();
		}
	}

	public override Vector3 SurfaceVelocity
	{
		get => new( _surfaceTangentSpeed, 0, 0 );
		set { _surfaceTangentSpeed = value.x; ApplyMaterial(); }
	}

	float _surfaceTangentSpeed;
	float _friction = -1;
	float _elasticity = -1;
	float _rollingResistance = -1;

	unsafe void ApplyMaterial()
	{
		if ( !IsValid )
			return;

		var mat = Box2d.b2DefaultSurfaceMaterial();
		mat.friction = _friction < 0 ? (_surface?.Friction ?? mat.friction) : _friction;
		mat.restitution = _elasticity < 0 ? (_surface?.Elasticity ?? mat.restitution) : _elasticity;
		mat.rollingResistance = _rollingResistance < 0 ? (_surface?.RollingResistance ?? mat.rollingResistance) : _rollingResistance;
		mat.tangentSpeed = _surfaceTangentSpeed;

		Box2d.b2Shape_SetSurfaceMaterial( ShapeId, (IntPtr)(&mat) );
	}

	internal override void UpdateSurface()
	{
		ApplyMaterial();
		ApplyDensity();
	}

	void ApplyDensity()
	{
		if ( !IsValid )
			return;

		float density = 1f;
		if ( _hasNoMass )
			density = 0f;
		else if ( _surface is { } surface )
		{
			float lengthUnits = Box2d.b2GetLengthUnitsPerMeter();
			density = surface.Density / (lengthUnits * lengthUnits);
		}

		Box2d.b2Shape_SetDensity( ShapeId, density, false );
		_body?.RebuildMass();
	}

	public override float Friction
	{
		get => IsValid ? Box2d.b2Shape_GetFriction( ShapeId ) : 0;
		set { _friction = value; ApplyMaterial(); }
	}

	internal override float Elasticity
	{
		set { _elasticity = value; ApplyMaterial(); }
	}

	internal override float RollingResistance
	{
		set { _rollingResistance = value; ApplyMaterial(); }
	}

	b2ShapeType ShapeType2d => Box2d.b2Shape_GetType( ShapeId );

	public override bool IsSphereShape => ShapeType2d == b2ShapeType.b2_circleShape;
	public override bool IsCapsuleShape => ShapeType2d == b2ShapeType.b2_capsuleShape;

	public override Sphere Sphere
	{
		get
		{
			if ( !IsSphereShape )
				throw new Exception( "PhysicsShape is not type Sphere" );

			var c = Box2d.b2Shape_GetCircle( ShapeId );
			return new Sphere( (Vector3)c.center, c.radius );
		}
	}

	public override Capsule Capsule
	{
		get
		{
			if ( !IsCapsuleShape )
				throw new Exception( "PhysicsShape is not type Capsule" );

			var c = Box2d.b2Shape_GetCapsule( ShapeId );
			return new Capsule( (Vector3)c.center1, (Vector3)c.center2, c.radius );
		}
	}

	internal override BBox LocalBounds
	{
		get
		{
			var tx = Body.Transform;
			var aabb = Box2d.b2Shape_GetAABB( ShapeId );
			return new( tx.PointToLocal( aabb.lowerBound ), tx.PointToLocal( aabb.upperBound ) );
		}
	}

	internal override BBox BuildBounds() => Box2d.b2Shape_GetAABB( ShapeId );

	internal override unsafe void UpdateSphereShape( Vector3 center, float radius )
	{
		var circle = new b2Circle { center = center, radius = radius };
		Box2d.b2Shape_SetCircle( ShapeId, (IntPtr)(&circle) );
		Dirty();
	}

	internal override unsafe void UpdateCapsuleShape( Vector3 center1, Vector3 center2, float radius )
	{
		var capsule = new b2Capsule { center1 = center1, center2 = center2, radius = radius };
		Box2d.b2Shape_SetCapsule( ShapeId, (IntPtr)(&capsule) );
		Dirty();
	}

	internal override unsafe void UpdateBoxShape( Vector3 center, Rotation rotation, Vector3 extents )
	{
		var polygon = Box2d.b2MakeOffsetBox( extents.x, extents.y, center, rotation );
		Box2d.b2Shape_SetPolygon( ShapeId, (IntPtr)(&polygon) );
		Dirty();
	}

	public override unsafe void UpdatePlane( Vector3 center, Rotation rotation, Vector2 size )
	{
		UpdateBoxShape( center, rotation, size * 0.5f );
	}

	public override void Remove()
	{
		if ( _handle.IsAllocated )
			_handle.Free();

		if ( !Box2d.b2Shape_IsValid( _shapeId ) ) return;

		Box2d.b2DestroyShape( ShapeId, true );
		ShapeId = default;
	}

	internal override unsafe bool IsTouching( PhysicsShape shape, bool triggersOnly )
	{
		if ( shape?._shape is not PhysicsShape2d other || !other.IsValid || !IsValid )
			return false;

		var sensorId = other.ShapeId;
		var visitorId = ShapeId;

		if ( !Box2d.b2Shape_IsSensor( sensorId ) )
		{
			sensorId = ShapeId;
			visitorId = other.ShapeId;
		}

		if ( !Box2d.b2Shape_IsSensor( sensorId ) )
			return false;

		var capacity = Box2d.b2Shape_GetSensorCapacity( sensorId );
		if ( capacity <= 0 )
			return false;

		b2ShapeId[] rented = null;
		Span<b2ShapeId> overlaps = capacity <= MaxStackOverlaps
			? stackalloc b2ShapeId[MaxStackOverlaps]
			: (rented = ArrayPool<b2ShapeId>.Shared.Rent( capacity ));

		try
		{
			int count;
			fixed ( b2ShapeId* ptr = overlaps )
				count = Box2d.b2Shape_GetSensorData( sensorId, (IntPtr)ptr, capacity );

			for ( int i = 0; i < count; i++ )
			{
				if ( overlaps[i] == visitorId )
					return true;
			}

			return false;
		}
		finally
		{
			if ( rented is not null )
				ArrayPool<b2ShapeId>.Shared.Return( rented );
		}
	}
}
