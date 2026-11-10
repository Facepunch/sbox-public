namespace Sandbox;

/// <summary>
/// Internal base class for physics shapes. Contains all shape functions and properties.
/// The public <see cref="PhysicsShape"/> wraps one of these (2D or 3D).
/// </summary>
internal abstract partial class PhysicsShapeInternal : IValid
{
	/// <summary>
	/// The public wrapper for this shape. Lazily created so shapes that originate
	/// on the native side (e.g. from an aggregate) still resolve to a stable wrapper.
	/// </summary>
	PhysicsShape _owner;
	internal PhysicsShape Owner => _owner ??= new PhysicsShape( this );
	internal void SetOwner( PhysicsShape owner ) => _owner ??= owner;

	public abstract bool IsValid { get; }

	/// <summary>
	/// The physics body we belong to.
	/// </summary>
	public abstract PhysicsBody Body { get; }

	[Obsolete]
	public virtual Vector3 Scale => 1.0f;

	internal virtual PhysicsShapeType ShapeType => default;

	internal virtual BBox LocalBounds => default;
	internal virtual BBox BuildBounds() => default;

	/// <summary>
	/// The bone index that this physics shape represents
	/// </summary>
	internal int BoneIndex { get; set; } = -1;

	/// <summary>
	/// The collider object that created / owns this shape
	/// </summary>
	public Collider Collider { get; set; }

	/// <summary>
	/// This is a trigger (!)
	/// </summary>
	public abstract bool IsTrigger { get; set; }

	/// <summary>
	/// Set the local velocity of the surface so things can slide along it, like a conveyor belt
	/// </summary>
	public virtual Vector3 SurfaceVelocity { get; set; }

	/// <summary>
	/// Enable contact, trace and touch
	/// </summary>
	public virtual void EnableAllCollision()
	{
		EnableSolidCollisions = true;
		EnableTouch = true;
	}

	/// <summary>
	/// Disable contact, trace and touch
	/// </summary>
	public virtual void DisableAllCollision()
	{
		EnableSolidCollisions = false;
		EnableTouch = false;
	}

	/// <summary>
	/// Controls whether this shape has solid collisions.
	/// </summary>
	public abstract bool EnableSolidCollisions { get; set; }

	/// <summary>
	/// Controls whether this shape can fire touch events for its owning entity. (Entity.StartTouch, Touch and EndTouch)
	/// </summary>
	public abstract bool EnableTouch { get; set; }

	/// <summary>
	/// Controls whether this shape can fire continuous touch events for its owning entity (i.e. calling Entity.Touch every frame)
	/// </summary>
	public abstract bool EnableTouchPersists { get; set; }

	/// <summary>
	/// When set, traces will never hit this shape (see <see cref="ColliderFlags.IgnoreTraces"/>).
	/// </summary>
	internal virtual bool IgnoreTraces { set { } }

	/// <summary>
	/// When set, this shape contributes no mass to its body (see <see cref="ColliderFlags.IgnoreMass"/>).
	/// </summary>
	internal virtual bool HasNoMass { set { } }

	public ITagSet Tags { get; protected set; }

	/// <summary>
	/// Controls physical properties of this shape.
	/// </summary>
	public abstract string SurfaceMaterial { get; set; }

	public abstract Surface Surface { get; set; }

	/// <summary>
	/// The friction value
	/// </summary>
	public abstract float Friction { get; set; }
	internal abstract float Elasticity { set; }
	internal abstract float RollingResistance { set; }

	/// <summary>
	/// Remove this shape. After calling this the shape should be considered released and not used again.
	/// </summary>
	public abstract void Remove();

	/// <summary>
	/// Called when anything significant changed about this physics object. Like its position,
	/// or its enabled status.
	/// </summary>
	internal Action OnDirty;
	protected void Dirty() => OnDirty?.Invoke();

	// 3D-specific members exposed as virtual for consumer compatibility.
	// Overridden in PhysicsShape3d with native implementations.

	/// <summary>
	/// Is this a MeshShape
	/// </summary>
	public virtual bool IsMeshShape => false;

	/// <summary>
	/// Is this a HullShape
	/// </summary>
	public virtual bool IsHullShape => false;

	/// <summary>
	/// Is this a SphereShape
	/// </summary>
	public virtual bool IsSphereShape => false;

	/// <summary>
	/// Is this a CapsuleShape
	/// </summary>
	public virtual bool IsCapsuleShape => false;

	/// <summary>
	/// Is this a HeightfieldShape
	/// </summary>
	public virtual bool IsHeightfieldShape => false;

	/// <summary>
	/// Get sphere properties if we're a sphere type
	/// </summary>
	public virtual Sphere Sphere => throw new NotSupportedException();

	/// <summary>
	/// Get capsule properties if we're a capsule type
	/// </summary>
	public virtual Capsule Capsule => throw new NotSupportedException();

	internal virtual void UpdateSphereShape( Vector3 center, float radius ) => throw new NotSupportedException();

	/// <summary>
	/// Recreate the collision mesh (Only if this physics shape is type Capsule)
	/// </summary>
	internal virtual void UpdateCapsuleShape( Vector3 center1, Vector3 center2, float radius ) => throw new NotSupportedException();

	/// <summary>
	/// Recreate the collision mesh (Only if this physics shape is type Hull)
	/// </summary>
	internal virtual void UpdateBoxShape( Vector3 center, Rotation rotation, Vector3 extents ) => throw new NotSupportedException();

	/// <summary>
	/// Recreate the collision mesh (Only if this physics shape is type Mesh)
	/// </summary>
	public virtual void UpdateMesh( List<Vector3> vertices, List<int> indices ) => throw new NotSupportedException();

	/// <summary>
	/// Recreate the mesh of the shape (Only if this physics shape is type Mesh)
	/// </summary>
	public virtual unsafe void UpdateMesh( Span<Vector3> vertices, Span<int> indices ) => throw new NotSupportedException();

	/// <summary>
	/// Recreate the hull of the shape (Only if this physics shape is type Hull)
	/// </summary>
	public virtual unsafe void UpdateHull( Vector3 position, Rotation rotation, Span<Vector3> points ) => throw new NotSupportedException();

	/// <summary>
	/// Update a plane shape's center, rotation and size.
	/// </summary>
	public virtual void UpdatePlane( Vector3 center, Rotation rotation, Vector2 size ) => throw new NotSupportedException();

	/// <summary>
	/// Triangulate this shape.
	/// </summary>
	public virtual void Triangulate( out Vector3[] positions, out uint[] indices ) => throw new NotSupportedException();

	internal virtual IEnumerable<Line> GetOutline() => throw new NotSupportedException();

	/// <summary>
	/// Multiple surfaces referenced by mesh or heightfield collision.
	/// </summary>
	public virtual Surface[] Surfaces { set { } }

	internal virtual void UpdateSurface() { }

	internal virtual bool IsTouching( PhysicsShape shape, bool triggersOnly ) => false;
}
