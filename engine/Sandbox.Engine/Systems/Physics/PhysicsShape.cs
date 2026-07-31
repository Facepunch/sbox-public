using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// Represents a basic, convex shape. A <see cref="PhysicsBody">PhysicsBody</see> consists of one or more of these.
/// </summary>
[Expose, ActionGraphIgnore]
public sealed partial class PhysicsShape : IHandle
{
	#region IHandle
	//
	// A pointer to the actual native object
	//
	internal IPhysicsShape native;

	//
	// IHandle implementation
	//
	void IHandle.HandleInit( IntPtr ptr ) => native = ptr;

	void IHandle.HandleDestroy()
	{
		native = IntPtr.Zero;
	}

	bool IHandle.HandleValid() => !native.IsNull;
	#endregion

	internal PhysicsShape( HandleCreationData _ )
	{
		Tags = new TagAccessor( this );
	}

	/// <summary>
	/// The physics body we belong to.
	/// </summary>
	[ActionGraphInclude]
	public PhysicsBody Body => native.GetBody();

	[Obsolete]
	public Vector3 Scale => 1.0f;

	internal PhysicsShapeType ShapeType => native.GetType_Native();

	internal BBox LocalBounds => native.LocalBounds();
	internal BBox BuildBounds() => native.BuildBounds();

	/// <summary>
	/// The bone index that this physics shape represents
	/// </summary>
	internal int BoneIndex { get; set; } = -1;

	/// <summary>
	/// The collider object that created / owns this shape
	/// </summary>
	[ActionGraphInclude]
	public Collider Collider { get; set; }

	/// <summary>
	/// This is a trigger (!)
	/// </summary>
	[ActionGraphInclude]
	public bool IsTrigger
	{
		get => native.IsTrigger();
		set => native.SetTrigger( value );
	}

	/// <summary>
	/// Set the local velocity of the surface so things can slide along it, like a conveyor belt
	/// </summary>
	public Vector3 SurfaceVelocity
	{
		get => native.GetLocalVelocity();
		set => native.SetLocalVelocity( value );
	}

	/// <summary>
	/// Enable contact, trace and touch
	/// </summary>
	public void EnableAllCollision()
	{
		var mask = CollisionFunctionMask.EnableSolidContact | CollisionFunctionMask.EnableTouchEvent;
		native.AddCollisionFunctionMask( (byte)mask );
	}

	/// <summary>
	/// Disable contact, trace and touch
	/// </summary>
	public void DisableAllCollision()
	{
		var mask = CollisionFunctionMask.EnableSolidContact | CollisionFunctionMask.EnableTouchEvent;
		native.RemoveCollisionFunctionMask( (byte)mask );
	}

	void SetCollisionFunctionFlag( CollisionFunctionMask flag, bool on ) { if ( on ) { native.AddCollisionFunctionMask( (byte)flag ); } else { native.RemoveCollisionFunctionMask( (byte)flag ); } }
	bool GetCollisionFunctionFlag( CollisionFunctionMask flag ) => (native.GetCollisionFunctionMask() & (byte)flag) != 0;

	/// <summary>
	/// Controls whether this shape has solid collisions.
	/// </summary>
	public bool EnableSolidCollisions
	{
		get { return GetCollisionFunctionFlag( CollisionFunctionMask.EnableSolidContact ); }
		set { SetCollisionFunctionFlag( CollisionFunctionMask.EnableSolidContact, value ); }
	}

	/// <summary>
	/// Controls whether this shape can fire touch events for its owning entity. (Entity.StartTouch, Touch and EndTouch)
	/// </summary>
	public bool EnableTouch
	{
		get { return GetCollisionFunctionFlag( CollisionFunctionMask.EnableTouchEvent ); }
		set { SetCollisionFunctionFlag( CollisionFunctionMask.EnableTouchEvent, value ); }
	}

	/// <summary>
	/// Controls whether this shape can fire continuous touch events for its owning entity (i.e. calling Entity.Touch every frame).
	/// Persist events are only sent while the contact is awake, sleeping contacts don't emit them.
	/// </summary>
	public bool EnableTouchPersists
	{
		get { return GetCollisionFunctionFlag( CollisionFunctionMask.EnableTouchPersists ); }
		set { SetCollisionFunctionFlag( CollisionFunctionMask.EnableTouchPersists, value ); }
	}

	/// <summary>
	/// Is this a MeshShape
	/// </summary>
	public bool IsMeshShape => ShapeType == PhysicsShapeType.SHAPE_MESH;

	/// <summary>
	/// Is this a HullShape
	/// </summary>
	public bool IsHullShape => ShapeType == PhysicsShapeType.SHAPE_HULL;

	/// <summary>
	/// Is this a SphereShape
	/// </summary>
	public bool IsSphereShape => ShapeType == PhysicsShapeType.SHAPE_SPHERE;

	/// <summary>
	/// Is this a CapsuleShape
	/// </summary>
	public bool IsCapsuleShape => ShapeType == PhysicsShapeType.SHAPE_CAPSULE;

	/// <summary>
	/// Is this a HeightfieldShape
	/// </summary>
	public bool IsHeightfieldShape => ShapeType == PhysicsShapeType.SHAPE_HEIGHTFIELD;

	/// <summary>
	/// Get sphere properties if we're a sphere type
	/// </summary>
	public Sphere Sphere
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
	public Capsule Capsule
	{
		get
		{
			if ( !IsCapsuleShape )
				throw new Exception( "PhysicsShape is not type Capsule" );

			return native.AsCapsule();
		}
	}

	/// <summary>
	/// Recreate the collision mesh (Only if this physics shape is type Capsule)
	/// </summary>
	internal void UpdateCapsuleShape( Vector3 center1, Vector3 center2, float radius )
	{
		if ( !IsCapsuleShape && !IsSphereShape )
			throw new Exception( "PhysicsShape is not type Capsule" );

		native.UpdateCapsuleShape( center1, center2, radius );

		Dirty();
	}

	/// <summary>
	/// Recreate the collision mesh (Only if this physics shape is type Hull)
	/// </summary>
	internal void UpdateBoxShape( Vector3 center, Rotation rotation, Vector3 extents )
	{
		if ( !IsHullShape )
			throw new Exception( "PhysicsShape is not type hull" );

		native.UpdateBoxShape( center, rotation, extents );

		Dirty();
	}

	/// <summary>
	/// Recreate the collision mesh (Only if this physics shape is type Mesh)
	/// </summary>
	public void UpdateMesh( List<Vector3> vertices, List<int> indices )
	{
		UpdateMesh( CollectionsMarshal.AsSpan( vertices ), CollectionsMarshal.AsSpan( indices ) );
	}

	/// <summary>
	/// Recreate the mesh of the shape (Only if this physics shape is type Mesh)
	/// </summary>
	public unsafe void UpdateMesh( Span<Vector3> vertices, Span<int> indices )
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
	public unsafe void UpdateHull( Vector3 position, Rotation rotation, Span<Vector3> points )
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

	/// <summary>
	/// Controls physical properties of this shape.
	/// </summary>
	public string SurfaceMaterial
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

	[ActionGraphInclude]
	public Surface Surface
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

	internal void UpdateSurface()
	{
		native.SetMaterialIndex( _surface?.ResourceName );
	}

	/// <summary>
	/// Multiple surfaces referenced by mesh or heightfield collision.
	/// </summary>
	[ActionGraphInclude]
	public Surface[] Surfaces
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
	/// Remove this shape. After calling this the shape should be considered released and not used again.
	/// </summary>
	public void Remove()
	{
		if ( !native.IsValid ) return;
		if ( !Body.IsValid() ) return;

		Body.RemoveShape( this );
	}

	/// <summary>
	/// Triangulate this shape.
	/// </summary>
	public void Triangulate( out Vector3[] positions, out uint[] indices )
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

	internal IEnumerable<Line> GetOutline()
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

	/// <summary>
	/// The friction value
	/// </summary>
	public float Friction
	{
		get => native.GetFriction();
		set => native.SetFriction( value );
	}

	internal float Elasticity
	{
		set => native.SetElasticity( value );
	}

	internal float RollingResistance
	{
		set => native.SetRollingResistance( value );
	}

	void Dirty()
	{
		OnDirty?.Invoke();
	}

	/// <summary>
	/// Called when anything significant changed about this physics object. Like its position,
	/// or its enabled status.
	/// </summary>
	internal Action OnDirty;

	internal bool IsTouching( PhysicsShape shape, bool triggersOnly )
	{
		if ( !shape.IsValid() )
			return false;

		return native.IsTouching( shape, triggersOnly );
	}
}
