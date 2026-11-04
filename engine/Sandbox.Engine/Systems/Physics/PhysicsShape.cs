namespace Sandbox;

/// <summary>
/// Represents a basic, convex shape. A <see cref="PhysicsBody">PhysicsBody</see> consists of one or more of these.
/// </summary>
[Expose]
public sealed partial class PhysicsShape : IValid
{
	internal PhysicsShapeInternal _shape;

	internal PhysicsShape( PhysicsShapeInternal shape )
	{
		_shape = shape;
		shape.SetOwner( this );
	}

	public bool IsValid => _shape is not null && _shape.IsValid;

	/// <summary>
	/// The physics body we belong to.
	/// </summary>
	public PhysicsBody Body => _shape?.Body;

	[Obsolete]
	public Vector3 Scale => _shape?.Scale ?? 1.0f;

	/// <summary>
	/// The collider object that created / owns this shape
	/// </summary>
	public Collider Collider
	{
		get => _shape?.Collider;
		set => _shape?.Collider = value;
	}

	/// <summary>
	/// This is a trigger (!)
	/// </summary>
	public bool IsTrigger
	{
		get => _shape?.IsTrigger ?? false;
		set => _shape?.IsTrigger = value;
	}

	/// <summary>
	/// Set the local velocity of the surface so things can slide along it, like a conveyor belt
	/// </summary>
	public Vector3 SurfaceVelocity
	{
		get => _shape?.SurfaceVelocity ?? default;
		set => _shape?.SurfaceVelocity = value;
	}

	/// <summary>
	/// Enable contact, trace and touch
	/// </summary>
	public void EnableAllCollision() => _shape?.EnableAllCollision();

	/// <summary>
	/// Disable contact, trace and touch
	/// </summary>
	public void DisableAllCollision() => _shape?.DisableAllCollision();

	/// <summary>
	/// Controls whether this shape has solid collisions.
	/// </summary>
	public bool EnableSolidCollisions
	{
		get => _shape?.EnableSolidCollisions ?? false;
		set => _shape?.EnableSolidCollisions = value;
	}

	/// <summary>
	/// Controls whether this shape can fire touch events for its owning entity. (Entity.StartTouch, Touch and EndTouch)
	/// </summary>
	public bool EnableTouch
	{
		get => _shape?.EnableTouch ?? false;
		set => _shape?.EnableTouch = value;
	}

	/// <summary>
	/// Controls whether this shape can fire continuous touch events for its owning entity (i.e. calling Entity.Touch every frame).
	/// Persist events are only sent while the contact is awake, sleeping contacts don't emit them.
	/// </summary>
	public bool EnableTouchPersists
	{
		get => _shape?.EnableTouchPersists ?? false;
		set => _shape?.EnableTouchPersists = value;
	}

	public ITagSet Tags => _shape?.Tags;

	/// <summary>
	/// Controls physical properties of this shape.
	/// </summary>
	public string SurfaceMaterial
	{
		get => _shape?.SurfaceMaterial;
		set => _shape?.SurfaceMaterial = value;
	}

	public Surface Surface
	{
		get => _shape?.Surface;
		set => _shape?.Surface = value;
	}

	/// <summary>
	/// The friction value
	/// </summary>
	public float Friction
	{
		get => _shape?.Friction ?? 0;
		set => _shape?.Friction = value;
	}

	internal bool IgnoreTraces { set => _shape?.IgnoreTraces = value; }
	internal bool HasNoMass { set => _shape?.HasNoMass = value; }

	/// <summary>
	/// Multiple surfaces referenced by mesh or heightfield collision.
	/// </summary>
	public Surface[] Surfaces
	{
		set => _shape?.Surfaces = value;
	}

	/// <summary>
	/// Remove this shape. After calling this the shape should be considered released and not used again.
	/// </summary>
	public void Remove() => _shape?.Remove();

	/// <summary>
	/// Is this a MeshShape
	/// </summary>
	public bool IsMeshShape => _shape?.IsMeshShape ?? false;

	/// <summary>
	/// Is this a HullShape
	/// </summary>
	public bool IsHullShape => _shape?.IsHullShape ?? false;

	/// <summary>
	/// Is this a SphereShape
	/// </summary>
	public bool IsSphereShape => _shape?.IsSphereShape ?? false;

	/// <summary>
	/// Is this a CapsuleShape
	/// </summary>
	public bool IsCapsuleShape => _shape?.IsCapsuleShape ?? false;

	/// <summary>
	/// Is this a HeightfieldShape
	/// </summary>
	public bool IsHeightfieldShape => _shape?.IsHeightfieldShape ?? false;

	/// <summary>
	/// Get sphere properties if we're a sphere type
	/// </summary>
	public Sphere Sphere => _shape.Sphere;

	/// <summary>
	/// Get capsule properties if we're a capsule type
	/// </summary>
	public Capsule Capsule => _shape.Capsule;

	/// <summary>
	/// Recreate the collision mesh (Only if this physics shape is type Mesh)
	/// </summary>
	public void UpdateMesh( List<Vector3> vertices, List<int> indices ) => _shape?.UpdateMesh( vertices, indices );

	/// <summary>
	/// Recreate the mesh of the shape (Only if this physics shape is type Mesh)
	/// </summary>
	public void UpdateMesh( Span<Vector3> vertices, Span<int> indices ) => _shape?.UpdateMesh( vertices, indices );

	/// <summary>
	/// Recreate the hull of the shape (Only if this physics shape is type Hull)
	/// </summary>
	public void UpdateHull( Vector3 position, Rotation rotation, Span<Vector3> points ) => _shape?.UpdateHull( position, rotation, points );

	/// <summary>
	/// Update a plane shape's center, rotation and size.
	/// </summary>
	public void UpdatePlane( Vector3 center, Rotation rotation, Vector2 size ) => _shape?.UpdatePlane( center, rotation, size );

	/// <summary>
	/// Triangulate this shape.
	/// </summary>
	public void Triangulate( out Vector3[] positions, out uint[] indices )
	{
		if ( _shape is null )
		{
			positions = [];
			indices = [];
			return;
		}

		_shape.Triangulate( out positions, out indices );
	}

	// --- internal surface used by the engine through the public shape type ---

	internal PhysicsShapeType ShapeType => _shape?.ShapeType ?? default;
	internal BBox LocalBounds => _shape?.LocalBounds ?? default;
	internal BBox BuildBounds() => _shape?.BuildBounds() ?? default;

	internal int BoneIndex
	{
		get => _shape?.BoneIndex ?? -1;
		set => _shape?.BoneIndex = value;
	}

	internal Action OnDirty
	{
		get => _shape?.OnDirty;
		set => _shape?.OnDirty = value;
	}

	internal float Elasticity
	{
		set => _shape?.Elasticity = value;
	}

	internal float RollingResistance
	{
		set => _shape?.RollingResistance = value;
	}

	internal void UpdateSphereShape( Vector3 center, float radius ) => _shape?.UpdateSphereShape( center, radius );
	internal void UpdateCapsuleShape( Vector3 center1, Vector3 center2, float radius ) => _shape?.UpdateCapsuleShape( center1, center2, radius );
	internal void UpdateBoxShape( Vector3 center, Rotation rotation, Vector3 extents ) => _shape?.UpdateBoxShape( center, rotation, extents );
	internal void UpdateSurface() => _shape?.UpdateSurface();
	internal IEnumerable<Line> GetOutline() => _shape?.GetOutline();
	internal bool IsTouching( PhysicsShape shape, bool triggersOnly ) => _shape?.IsTouching( shape, triggersOnly ) ?? false;

	// --- obsolete tag helpers (kept for source compatibility) ---

	[Obsolete( "Use Tags" )]
	public bool HasTag( string tag ) => Tags.Has( tag );

	[Obsolete( "Use Tags" )]
	public bool AddTag( string tag )
	{
		Tags.Add( tag );
		return true;
	}

	[Obsolete( "Use Tags" )]
	public bool RemoveTag( string tag )
	{
		Tags.Remove( tag );
		return true;
	}

	[Obsolete( "Use Tags" )]
	public bool ClearTags()
	{
		Tags.RemoveAll();
		return true;
	}
}
