using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// 3D physics shape backed by a native Box3D/Source 2 <see cref="IPhysicsShape"/> handle.
/// </summary>
internal sealed partial class PhysicsShape3d : PhysicsShapeInternal, IHandle
{
	internal IPhysicsShape native;

	void IHandle.HandleInit( IntPtr ptr ) => native = ptr;
	void IHandle.HandleDestroy() => native = IntPtr.Zero;
	bool IHandle.HandleValid() => !native.IsNull;

	public override bool IsValid => !native.IsNull;

	internal PhysicsShape3d( HandleCreationData _ )
	{
		Tags = new TagAccessor( this );
	}

	/// <summary>
	/// The physics body we belong to.
	/// </summary>
	public override PhysicsBody Body => native.GetBody()?.Owner;

	[Obsolete]
	public override Vector3 Scale => 1.0f;

	internal override PhysicsShapeType ShapeType => native.GetType_Native();

	internal override BBox LocalBounds => native.LocalBounds();
	internal override BBox BuildBounds() => native.BuildBounds();

	internal override bool IgnoreTraces { set => native.SetIgnoreTraces( value ); }
	internal override bool HasNoMass { set => native.SetHasNoMass( value ); }

	/// <summary>
	/// This is a trigger (!)
	/// </summary>
	public override bool IsTrigger
	{
		get => native.IsTrigger();
		set => native.SetTrigger( value );
	}

	/// <summary>
	/// Set the local velocity of the surface so things can slide along it, like a conveyor belt
	/// </summary>
	public override Vector3 SurfaceVelocity
	{
		get => native.GetLocalVelocity();
		set => native.SetLocalVelocity( value );
	}

	/// <summary>
	/// Enable contact, trace and touch
	/// </summary>
	public override void EnableAllCollision()
	{
		var mask = CollisionFunctionMask.EnableSolidContact | CollisionFunctionMask.EnableTouchEvent;
		native.AddCollisionFunctionMask( (byte)mask );
	}

	/// <summary>
	/// Disable contact, trace and touch
	/// </summary>
	public override void DisableAllCollision()
	{
		var mask = CollisionFunctionMask.EnableSolidContact | CollisionFunctionMask.EnableTouchEvent;
		native.RemoveCollisionFunctionMask( (byte)mask );
	}

	void SetCollisionFunctionFlag( CollisionFunctionMask flag, bool on ) { if ( on ) { native.AddCollisionFunctionMask( (byte)flag ); } else { native.RemoveCollisionFunctionMask( (byte)flag ); } }
	bool GetCollisionFunctionFlag( CollisionFunctionMask flag ) => (native.GetCollisionFunctionMask() & (byte)flag) != 0;

	/// <summary>
	/// Controls whether this shape has solid collisions.
	/// </summary>
	public override bool EnableSolidCollisions
	{
		get => GetCollisionFunctionFlag( CollisionFunctionMask.EnableSolidContact );
		set => SetCollisionFunctionFlag( CollisionFunctionMask.EnableSolidContact, value );
	}

	/// <summary>
	/// Controls whether this shape can fire touch events for its owning entity. (Entity.StartTouch, Touch and EndTouch)
	/// </summary>
	public override bool EnableTouch
	{
		get => GetCollisionFunctionFlag( CollisionFunctionMask.EnableTouchEvent );
		set => SetCollisionFunctionFlag( CollisionFunctionMask.EnableTouchEvent, value );
	}

	/// <summary>
	/// Controls whether this shape can fire continuous touch events for its owning entity (i.e. calling Entity.Touch every frame)
	/// </summary>
	public override bool EnableTouchPersists
	{
		get => GetCollisionFunctionFlag( CollisionFunctionMask.EnableTouchPersists );
		set => SetCollisionFunctionFlag( CollisionFunctionMask.EnableTouchPersists, value );
	}

	/// <summary>
	/// Is this a MeshShape
	/// </summary>
	public override bool IsMeshShape => ShapeType == PhysicsShapeType.SHAPE_MESH;

	/// <summary>
	/// Is this a HullShape
	/// </summary>
	public override bool IsHullShape => ShapeType == PhysicsShapeType.SHAPE_HULL;

	/// <summary>
	/// Is this a SphereShape
	/// </summary>
	public override bool IsSphereShape => ShapeType == PhysicsShapeType.SHAPE_SPHERE;

	/// <summary>
	/// Is this a CapsuleShape
	/// </summary>
	public override bool IsCapsuleShape => ShapeType == PhysicsShapeType.SHAPE_CAPSULE;

	/// <summary>
	/// Is this a HeightfieldShape
	/// </summary>
	public override bool IsHeightfieldShape => ShapeType == PhysicsShapeType.SHAPE_HEIGHTFIELD;

	/// <summary>
	/// Get sphere properties if we're a sphere type
	/// </summary>
	public override Sphere Sphere
	{
		get
		{
			if ( !IsSphereShape )
				throw new Exception( "PhysicsShape is not type Sphere" );

			return native.AsSphere();
		}
	}

	/// <summary>
	/// Get capsule properties if we're a capsule type
	/// </summary>
	public override Capsule Capsule
	{
		get
		{
			if ( !IsCapsuleShape )
				throw new Exception( "PhysicsShape is not type Capsule" );

			return native.AsCapsule();
		}
	}

	internal override void UpdateSphereShape( Vector3 center, float radius )
	{
		if ( !IsSphereShape )
			throw new Exception( "PhysicsShape is not type Sphere" );

		native.UpdateSphereShape( center, radius );

		Dirty();
	}

	/// <summary>
	/// Recreate the collision mesh (Only if this physics shape is type Capsule)
	/// </summary>
	internal override void UpdateCapsuleShape( Vector3 center1, Vector3 center2, float radius )
	{
		if ( !IsCapsuleShape && !IsSphereShape )
			throw new Exception( "PhysicsShape is not type Capsule" );

		native.UpdateCapsuleShape( center1, center2, radius );

		Dirty();
	}

	/// <summary>
	/// Recreate the collision mesh (Only if this physics shape is type Hull)
	/// </summary>
	internal override void UpdateBoxShape( Vector3 center, Rotation rotation, Vector3 extents )
	{
		if ( !IsHullShape )
			throw new Exception( "PhysicsShape is not type hull" );

		native.UpdateBoxShape( center, rotation, extents );

		Dirty();
	}

	/// <summary>
	/// Recreate the collision mesh (Only if this physics shape is type Mesh)
	/// </summary>
	public override void UpdateMesh( List<Vector3> vertices, List<int> indices )
	{
		UpdateMesh( CollectionsMarshal.AsSpan( vertices ), CollectionsMarshal.AsSpan( indices ) );
	}

	/// <summary>
	/// Recreate the mesh of the shape (Only if this physics shape is type Mesh)
	/// </summary>
	public override unsafe void UpdateMesh( Span<Vector3> vertices, Span<int> indices )
	{
		if ( ShapeType != PhysicsShapeType.SHAPE_MESH )
			throw new Exception( "PhysicsShape is not type Mesh" );

		if ( vertices.Length == 0 )
			return;

		if ( indices.Length == 0 )
			return;

		var vertexCount = vertices.Length;

		foreach ( var i in indices )
		{
			if ( i < 0 || i >= vertexCount )
				throw new ArgumentOutOfRangeException( $"Index ({i}) out of range ({vertexCount - 1})" );
		}

		fixed ( Vector3* vertices_ptr = vertices )
		fixed ( int* indices_ptr = indices )
		{
			native.UpdateMeshShape( vertices.Length, (IntPtr)vertices_ptr, indices.Length, (IntPtr)indices_ptr );
		}

		Dirty();
	}

	/// <summary>
	/// Recreate the hull of the shape (Only if this physics shape is type Hull)
	/// </summary>
	public override unsafe void UpdateHull( Vector3 position, Rotation rotation, Span<Vector3> points )
	{
		if ( ShapeType != PhysicsShapeType.SHAPE_HULL )
			throw new Exception( "PhysicsShape is not type Hull" );

		if ( points.Length == 0 )
			return;

		fixed ( Vector3* points_ptr = points )
		{
			native.UpdateHullShape( position, rotation, points.Length, (IntPtr)points_ptr );
		}

		Dirty();
	}

	private static readonly int[] PlaneIndices = [0, 1, 2, 2, 3, 0];

	public override void UpdatePlane( Vector3 center, Rotation rotation, Vector2 size )
	{
		var tangent = rotation.Right;
		var bitangent = rotation.Down;

		var halfX = size.x * 0.5f;
		var halfY = size.y * 0.5f;

		Span<Vector3> vertices =
		[
			center - tangent * halfX - bitangent * halfY,
			center + tangent * halfX - bitangent * halfY,
			center + tangent * halfX + bitangent * halfY,
			center - tangent * halfX + bitangent * halfY
		];

		UpdateMesh( vertices, PlaneIndices );
	}

	/// <summary>
	/// Controls physical properties of this shape.
	/// </summary>
	public override string SurfaceMaterial
	{
		get => native.GetMaterialName();
		set
		{
			native.SetMaterialIndex( value );

			// Because we're setting the surface on the native side,
			// we also need to update the cached surface to keep it in sync!
			_surface = string.IsNullOrWhiteSpace( SurfaceMaterial ) ? null : Surface.FindByName( SurfaceMaterial );
		}
	}

	Surface _surface;

	public override Surface Surface
	{
		get
		{
			if ( _surface is null && !string.IsNullOrWhiteSpace( SurfaceMaterial ) )
			{
				_surface = Surface.FindByName( SurfaceMaterial );
			}

			return _surface;
		}
		set
		{
			_surface = value;
			UpdateSurface();
		}
	}

	internal override void UpdateSurface()
	{
		native.SetMaterialIndex( _surface?.ResourceName );
	}

	/// <summary>
	/// Multiple surfaces referenced by mesh or heightfield collision.
	/// </summary>
	public override Surface[] Surfaces
	{
		set => SetSurfaces( value );
	}

	private unsafe void SetSurfaces( Surface[] surfaces )
	{
		if ( surfaces is null )
			return;

		for ( var i = 0; i < surfaces.Length; i++ )
		{
			var surface = surfaces[i];
			native.SetSurfaceIndex( surface is null ? -1 : surface.Index, i );
		}
	}

	/// <summary>
	/// The friction value
	/// </summary>
	public override float Friction
	{
		get => native.GetFriction();
		set => native.SetFriction( value );
	}

	internal override float Elasticity
	{
		set => native.SetElasticity( value );
	}

	internal override float RollingResistance
	{
		set => native.SetRollingResistance( value );
	}

	/// <summary>
	/// Remove this shape. After calling this the shape should be considered released and not used again.
	/// </summary>
	public override void Remove()
	{
		if ( !native.IsValid ) return;
		if ( !Body.IsValid() ) return;

		Body.RemoveShape( Owner );
	}

	/// <summary>
	/// Triangulate this shape.
	/// </summary>
	public override void Triangulate( out Vector3[] positions, out uint[] indices )
	{
		var arrVectors = CUtlVectorVector.Create( 0, 0 );
		var arrIndices = CUtlVectorUInt32.Create( 0, 0 );
		native.GetTriangulation( arrVectors, arrIndices );

		positions = new Vector3[arrVectors.Count()];
		indices = new uint[arrIndices.Count()];

		for ( var i = 0; i < positions.Length; ++i )
			positions[i] = arrVectors.Element( i );

		for ( var i = 0; i < indices.Length; ++i )
			indices[i] = arrIndices.Element( i );

		arrVectors.DeleteThis();
		arrIndices.DeleteThis();
	}

	internal override IEnumerable<Line> GetOutline()
	{
		var arrVectors = CUtlVectorVector.Create( 0, 0 );
		native.GetOutline( arrVectors );
		var count = arrVectors.Count();

		for ( int i = 0; i < count; i += 2 )
		{
			yield return new Line( arrVectors.Element( i ), arrVectors.Element( i + 1 ) );
		}

		arrVectors.DeleteThis();
	}

	internal override bool IsTouching( PhysicsShape shape, bool triggersOnly )
	{
		if ( !shape.IsValid() )
			return false;

		return native.IsTouching( (PhysicsShape3d)shape._shape, triggersOnly );
	}
}
