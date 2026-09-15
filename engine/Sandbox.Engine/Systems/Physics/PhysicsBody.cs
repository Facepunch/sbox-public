namespace Sandbox;

/// <summary>
/// Represents a physics object. An entity can have multiple physics objects. See <see cref="PhysicsGroup">PhysicsGroup</see>.
/// A physics objects consists of one or more <see cref="PhysicsShape">PhysicsShape</see>s.
/// </summary>
[Expose]
public sealed partial class PhysicsBody : IValid
{
	internal PhysicsBodyInternal _body;

	internal PhysicsBody( PhysicsBodyInternal body )
	{
		_body = body;
		body.SetOwner( this );
	}

	/// <summary>
	/// Creates a new physics body in the given world.
	/// </summary>
	public PhysicsBody( PhysicsWorld world )
	{
		ArgumentNullException.ThrowIfNull( world );

		_body = world.CreateBodyInternal();
		_body.SetOwner( this );
	}

	/// <summary>
	/// Creates a new 3D physics body in the given world.
	/// </summary>
	[Obsolete( "Use world.CreateBody() instead" )]
	public static PhysicsBody Create( PhysicsWorld world ) => world.CreateBody();

	public bool IsValid => _body is not null && _body.IsValid;

	/// <summary>
	/// The GameObject that created this body
	/// </summary>
	public GameObject GameObject
	{
		get => _body.GameObject;
		set => _body.GameObject = value;
	}

	/// <summary>
	/// The component that created this body
	/// </summary>
	public Component Component
	{
		get => _body.Component;
		set => _body.Component = value;
	}

	[Obsolete( "Use Component property" )]
	public void SetComponentSource( Component c ) => _body.Component = c;

	[Obsolete( "Use GameObject property" )]
	public GameObject GetGameObject() => _body.GameObject;

	/// <summary>
	/// Position of this body in world coordinates.
	/// </summary>
	public Vector3 Position
	{
		get => _body.Position;
		set => _body.Position = value;
	}

	/// <summary>
	/// The physics world this body belongs to.
	/// </summary>
	public PhysicsWorld World => _body.World?.Owner;

	/// <summary>
	/// Rotation of the physics body in world space.
	/// </summary>
	public Rotation Rotation
	{
		get => _body.Rotation;
		set => _body.Rotation = value;
	}

	[Obsolete]
	public float Scale => _body.Scale;

	/// <summary>
	/// Linear velocity of this body in world space.
	/// </summary>
	public Vector3 Velocity
	{
		get => _body.Velocity;
		set => _body.Velocity = value;
	}

	/// <summary>
	/// Angular velocity of this body in world space.
	/// </summary>
	public Vector3 AngularVelocity
	{
		get => _body.AngularVelocity;
		set => _body.AngularVelocity = value;
	}

	/// <summary>
	/// Center of mass for this physics body in world space coordinates.
	/// </summary>
	public Vector3 MassCenter => _body.MassCenter;

	/// <summary>
	/// Center of mass for this physics body relative to its <see cref="Position">origin</see>.
	/// </summary>
	public Vector3 LocalMassCenter
	{
		get => _body.LocalMassCenter;
		set => _body.LocalMassCenter = value;
	}

	/// <summary>
	/// Is this physics body mass calculated or set directly.
	/// </summary>
	public bool OverrideMassCenter
	{
		get => _body.OverrideMassCenter;
		set => _body.OverrideMassCenter = value;
	}

	/// <summary>
	/// Mass of this physics body.
	/// </summary>
	public float Mass
	{
		get => _body.Mass;
		set => _body.Mass = value;
	}

	/// <summary>
	/// Whether gravity is enabled for this body or not.
	/// </summary>
	public bool GravityEnabled
	{
		get => _body.GravityEnabled;
		set => _body.GravityEnabled = value;
	}

	/// <summary>
	/// Whether to play collision sounds
	/// </summary>
	public bool EnableCollisionSounds
	{
		get => _body.EnableCollisionSounds;
		set => _body.EnableCollisionSounds = value;
	}

	/// <summary>
	/// Scale the gravity relative to <see cref="PhysicsWorld.Gravity"/>. 2 is double the gravity, etc.
	/// </summary>
	public float GravityScale
	{
		get => _body.GravityScale;
		set => _body.GravityScale = value;
	}

	/// <summary>
	/// If true we'll create a controller for this physics body.
	/// </summary>
	public bool UseController
	{
		get => _body.UseController;
		set => _body.UseController = value;
	}

	/// <summary>
	/// Enables Touch callbacks on all <see cref="PhysicsShape">PhysicsShapes</see> of this body.
	/// </summary>
	public bool EnableTouch
	{
		get => _body.EnableTouch;
		set => _body.EnableTouch = value;
	}

	/// <summary>
	/// Sets <see cref="PhysicsShape.EnableTouchPersists"/> on all shapes of this body.
	/// </summary>
	public bool EnableTouchPersists
	{
		get => _body.EnableTouchPersists;
		set => _body.EnableTouchPersists = value;
	}

	/// <summary>
	/// Sets <see cref="PhysicsShape.EnableSolidCollisions"/> on all shapes of this body.
	/// </summary>
	public bool EnableSolidCollisions
	{
		get => _body.EnableSolidCollisions;
		set => _body.EnableSolidCollisions = value;
	}

	/// <summary>
	/// Movement type of physics body, either Static, Keyframed, Dynamic
	/// </summary>
	public PhysicsBodyType BodyType
	{
		get => _body.BodyType;
		set => _body.BodyType = value;
	}

	/// <summary>
	/// Whether this body is allowed to automatically go into "sleep" after a certain amount of time of inactivity.
	/// </summary>
	public bool AutoSleep
	{
		set => _body.AutoSleep = value;
	}

	/// <summary>
	/// The speed threshold below which this body will be put to sleep. Units per second.
	/// </summary>
	public float SleepThreshold
	{
		get => _body.SleepThreshold;
		set => _body.SleepThreshold = value;
	}

	/// <summary>
	/// Transform of this physics body.
	/// </summary>
	public Transform Transform
	{
		get => _body.Transform;
		set => _body.Transform = value;
	}

	/// <summary>
	/// Move to a new position, sweeping the shadow if <see cref="UseController"/> is enabled.
	/// </summary>
	public void Move( Transform tx, float delta ) => _body.Move( tx, delta );

	/// <summary>
	/// How many shapes belong to this body.
	/// </summary>
	public int ShapeCount => _body.ShapeCount;

	/// <summary>
	/// All shapes that belong to this body.
	/// </summary>
	public IEnumerable<PhysicsShape> Shapes => _body.Shapes;

	/// <inheritdoc cref="PhysicsBodyInternal.AddSphereShape(Vector3, float, bool)"/>
	public PhysicsShape AddSphereShape( Vector3 center, float radius, bool rebuildMass = true ) => _body.AddSphereShape( center, radius, rebuildMass );

	/// <summary>
	/// Add a sphere shape to this body.
	/// </summary>
	public PhysicsShape AddSphereShape( in Sphere sphere, bool rebuildMass = true ) => _body.AddSphereShape( sphere, rebuildMass );

	/// <summary>
	/// Add a capsule shape to this body.
	/// </summary>
	public PhysicsShape AddCapsuleShape( Vector3 center, Vector3 center2, float radius, bool rebuildMass = true ) => _body.AddCapsuleShape( center, center2, radius, rebuildMass );

	/// <summary>
	/// Add a box shape to this body.
	/// </summary>
	public PhysicsShape AddBoxShape( Vector3 position, Rotation rotation, Vector3 extent, bool rebuildMass = true ) => _body.AddBoxShape( position, rotation, extent, rebuildMass );

	/// <summary>
	/// Add a box shape to this body.
	/// </summary>
	public PhysicsShape AddBoxShape( BBox box, Rotation rotation, bool rebuildMass = true ) => _body.AddBoxShape( box, rotation, rebuildMass );

	/// <summary>
	/// Add a plane shape to this body.
	/// </summary>
	public PhysicsShape AddPlaneShape( Vector3 center, Rotation rotation, Vector2 size, bool rebuildMass = true ) => _body.AddPlaneShape( center, rotation, size, rebuildMass );

	/// <inheritdoc cref="AddHullShape(Vector3, Rotation, Span{Vector3}, bool)"/>
	public PhysicsShape AddHullShape( Vector3 position, Rotation rotation, List<Vector3> points, bool rebuildMass = true ) => _body.AddHullShape( position, rotation, points, rebuildMass );

	/// <summary>
	/// Add a convex hull shape to this body.
	/// </summary>
	public PhysicsShape AddHullShape( Vector3 position, Rotation rotation, Span<Vector3> points, bool rebuildMass = true ) => _body.AddHullShape( position, rotation, points, rebuildMass );

	/// <summary>
	/// Add a cylinder shape to this body.
	/// </summary>
	public PhysicsShape AddCylinderShape( Vector3 position, Rotation rotation, float height, float radius, int slices = 16 ) => _body.AddCylinderShape( position, rotation, height, radius, slices );

	/// <summary>
	/// Add a cone shape to this body.
	/// </summary>
	public PhysicsShape AddConeShape( Vector3 position, Rotation rotation, float height, float radius1, float radius2 = 0.0f, int slices = 16 ) => _body.AddConeShape( position, rotation, height, radius1, radius2, slices );

	/// <summary>
	/// Add a cone shape to this body.
	/// </summary>
	public PhysicsShape AddConeShape( Vector3 a, Vector3 b, float radiusA, float radiusB, int slices = 16 ) => _body.AddConeShape( a, b, radiusA, radiusB, slices );

	/// <inheritdoc cref="AddMeshShape(Span{Vector3}, Span{int})"/>
	public PhysicsShape AddMeshShape( List<Vector3> vertices, List<int> indices ) => _body.AddMeshShape( vertices, indices );

	/// <summary>
	/// Adds a mesh type shape to this physics body. Mesh shapes cannot be physically simulated!
	/// </summary>
	public PhysicsShape AddMeshShape( Span<Vector3> vertices, Span<int> indices ) => _body.AddMeshShape( vertices, indices );

	/// <summary>
	/// Adds a heightfield shape to this physics body.
	/// </summary>
	public PhysicsShape AddHeightFieldShape( ushort[] heights, byte[] materials, int sizeX, int sizeY, float sizeScale, float heightScale ) => _body.AddHeightFieldShape( heights, materials, sizeX, sizeY, sizeScale, heightScale );

	internal PhysicsShape AddHeightFieldShape( ushort[] heights, byte[] materials, int sizeX, int sizeY, float sizeScale, float heightScale, int materialCount ) => _body.AddHeightFieldShape( heights, materials, sizeX, sizeY, sizeScale, heightScale, materialCount );

	[Obsolete]
	public PhysicsShape AddCloneShape( PhysicsShape shape ) => null;

	/// <summary>
	/// Add a shape from a physics hull
	/// </summary>
	public PhysicsShape AddShape( PhysicsGroupDescription.BodyPart.HullPart part, Transform transform, bool rebuildMass = true ) => _body.AddShape( part, transform, rebuildMass );

	/// <summary>
	/// Add a shape from a mesh hull
	/// </summary>
	public PhysicsShape AddShape( PhysicsGroupDescription.BodyPart.MeshPart part, Transform transform, bool convertToHull, bool rebuildMass = true ) => _body.AddShape( part, transform, convertToHull, rebuildMass );

	/// <summary>
	/// Remove all physics shapes, but not the physics body itself.
	/// </summary>
	public void ClearShapes() => _body.ClearShapes();

	internal IDisposable TriggerScope() => _body.TriggerScope();

	internal void RemoveShape( PhysicsShape shape ) => _body.RemoveShape( shape );

	/// <summary>
	/// Rebuilds mass from all shapes of this body based on their volume and physics properties.
	/// </summary>
	public void RebuildMass() => _body.RebuildMass();

	/// <summary>
	/// Completely removes this physics body.
	/// </summary>
	public void Remove() => _body.Remove();

	/// <summary>
	/// Applies instant linear impulse to this body at its center of mass.
	/// </summary>
	public void ApplyImpulse( Vector3 impulse ) => _body.ApplyImpulse( impulse );

	/// <summary>
	/// Applies instant linear impulse to this body at given position.
	/// </summary>
	public void ApplyImpulseAt( Vector3 position, Vector3 velocity ) => _body.ApplyImpulseAt( position, velocity );

	/// <summary>
	/// Applies instant angular impulse to this body.
	/// </summary>
	public void ApplyAngularImpulse( Vector3 impulse ) => _body.ApplyAngularImpulse( impulse );

	/// <summary>
	/// Applies force to this body at the center of mass.
	/// </summary>
	public void ApplyForce( Vector3 force ) => _body.ApplyForce( force );

	/// <summary>
	/// Applies force to this body at given position.
	/// </summary>
	public void ApplyForceAt( Vector3 position, Vector3 force ) => _body.ApplyForceAt( position, force );

	/// <summary>
	/// Applies angular velocity to this body.
	/// </summary>
	public void ApplyTorque( Vector3 force ) => _body.ApplyTorque( force );

	/// <summary>
	/// Clear accumulated linear forces during this physics frame that were not yet applied.
	/// </summary>
	public void ClearForces() => _body.ClearForces();

	/// <summary>
	/// Clear accumulated torque during this physics frame that was not yet applied.
	/// </summary>
	public void ClearTorque() => _body.ClearTorque();

	/// <summary>
	/// Returns the world space velocity of a point of the object.
	/// </summary>
	public Vector3 GetVelocityAtPoint( Vector3 point ) => _body.GetVelocityAtPoint( point );

	/// <summary>
	/// Whether this body is enabled or not.
	/// </summary>
	public bool Enabled
	{
		get => _body.Enabled;
		set => _body.Enabled = value;
	}

	/// <summary>
	/// Controls physics simulation on this body.
	/// </summary>
	public bool MotionEnabled
	{
		get => _body.MotionEnabled;
		set => _body.MotionEnabled = value;
	}

	/// <summary>
	/// You can use this to wake the body up, or prematurely send it to sleep.
	/// </summary>
	public bool Sleeping
	{
		get => _body.Sleeping;
		set => _body.Sleeping = value;
	}

	[Obsolete( "No longer exists" )]
	public bool SpeculativeContactEnabled
	{
		get => false;
		set { }
	}

	/// <summary>
	/// The physics body we are attached to, if any
	/// </summary>
	public PhysicsBody Parent
	{
		get => _body.Parent;
		set => _body.Parent = value;
	}

	/// <summary>
	/// A convenience property, returns <see cref="Parent">Parent</see>, or if there is no parent, returns itself.
	/// </summary>
	public PhysicsBody SelfOrParent => _body.SelfOrParent;

	/// <summary>
	/// The physics group we belong to.
	/// </summary>
	public PhysicsGroup PhysicsGroup => _body.PhysicsGroup;

	/// <summary>
	/// Returns the closest point to the given one between all shapes of this body.
	/// </summary>
	public Vector3 FindClosestPoint( Vector3 vec ) => _body.FindClosestPoint( vec );

	/// <summary>
	/// Generic linear damping, i.e. how much the physics body will slow down on its own.
	/// </summary>
	public float LinearDamping
	{
		get => _body.LinearDamping;
		set => _body.LinearDamping = value;
	}

	/// <summary>
	/// Generic angular damping, i.e. how much the physics body will slow down on its own.
	/// </summary>
	public float AngularDamping
	{
		get => _body.AngularDamping;
		set => _body.AngularDamping = value;
	}

	[Obsolete]
	public float LinearDrag { get => default; set { } }

	[Obsolete]
	public float AngularDrag { get => default; set { } }

	[Obsolete]
	public bool DragEnabled { get => default; set { } }

	/// <summary>
	/// The diagonal elements of the local inertia tensor matrix.
	/// </summary>
	public Vector3 Inertia => _body.Inertia;

	/// <summary>
	/// The orientation of the principal axes of local inertia tensor matrix.
	/// </summary>
	public Rotation InertiaRotation => _body.InertiaRotation;

	/// <summary>
	/// Sets the inertia tensor using the given moments and rotation.
	/// </summary>
	public void SetInertiaTensor( Vector3 inertia, Rotation rotation ) => _body.SetInertiaTensor( inertia, rotation );

	/// <summary>
	/// Resets the inertia tensor to its calculated values.
	/// </summary>
	public void ResetInertiaTensor() => _body.ResetInertiaTensor();

	/// <summary>
	/// Returns Axis-Aligned Bounding Box (AABB) of this physics body.
	/// </summary>
	public BBox GetBounds() => _body.GetBounds();

	/// <summary>
	/// Returns average of densities for all physics shapes of this body.
	/// </summary>
	public float Density => _body.Density;

	/// <summary>
	/// Time since last water splash effect. Used internally.
	/// </summary>
	public RealTimeSince LastWaterEffect
	{
		get => _body.LastWaterEffect;
		set => _body.LastWaterEffect = value;
	}

	/// <summary>
	/// Sets <see cref="PhysicsShape.SurfaceMaterial"/> on all child <see cref="PhysicsShape">PhysicsShape</see>s.
	/// </summary>
	public string SurfaceMaterial
	{
		get => _body.SurfaceMaterial;
		set => _body.SurfaceMaterial = value;
	}

	public Surface Surface
	{
		get => _body.Surface;
		set => _body.Surface = value;
	}

	/// <summary>
	/// Convenience function that returns a <see cref="PhysicsPoint"/> from a position relative to this body.
	/// </summary>
	public PhysicsPoint LocalPoint( Vector3 p ) => _body.LocalPoint( p );

	/// <summary>
	/// Convenience function that returns a <see cref="PhysicsPoint"/> for this body from a world space position.
	/// </summary>
	public PhysicsPoint WorldPoint( Vector3 p ) => _body.WorldPoint( p );

	/// <summary>
	/// Returns a <see cref="PhysicsPoint"/> at the center of mass of this body.
	/// </summary>
	public PhysicsPoint MassCenterPoint() => _body.MassCenterPoint();

	/// <summary>
	/// What is this body called in the group?
	/// </summary>
	public string GroupName => _body.GroupName;

	/// <summary>
	/// Return the index of this body in its PhysicsGroup
	/// </summary>
	public int GroupIndex => _body.GroupIndex;

	/// <summary>
	/// Checks if another body overlaps us, ignoring all collision rules
	/// </summary>
	public bool CheckOverlap( PhysicsBody body ) => _body.CheckOverlap( body );

	/// <summary>
	/// Checks if another body overlaps us at a given transform, ignoring all collision rules
	/// </summary>
	public bool CheckOverlap( PhysicsBody body, Transform transform ) => _body.CheckOverlap( body, transform );

	/// <summary>
	/// Finds the smallest move needed to separate us from another body, ignoring all collision rules.
	/// </summary>
	public bool ComputePenetration( PhysicsBody body, out Vector3 direction, out float distance ) => _body.ComputePenetration( body, out direction, out distance );

	/// <summary>
	/// Finds the smallest move needed to separate us from another body placed at a given transform.
	/// </summary>
	public bool ComputePenetration( PhysicsBody body, Transform transform, out Vector3 direction, out float distance ) => _body.ComputePenetration( body, transform, out direction, out distance );

	internal bool IsTouching( PhysicsBody body, bool triggersOnly ) => _body.IsTouching( body, triggersOnly );
	internal bool IsTouching( PhysicsShape shape, bool triggersOnly ) => _body.IsTouching( shape, triggersOnly );

	public Action<PhysicsIntersection> OnIntersectionStart
	{
		get => _body.OnIntersectionStart;
		set => _body.OnIntersectionStart = value;
	}

	public Action<PhysicsIntersection> OnIntersectionUpdate
	{
		get => _body.OnIntersectionUpdate;
		set => _body.OnIntersectionUpdate = value;
	}

	public Action<PhysicsIntersectionEnd> OnIntersectionEnd
	{
		get => _body.OnIntersectionEnd;
		set => _body.OnIntersectionEnd = value;
	}

	internal void DispatchIntersectionStart( PhysicsIntersection c ) => _body.DispatchIntersectionStart( c );
	internal void DispatchIntersectionUpdate( PhysicsIntersection c ) => _body.DispatchIntersectionUpdate( c );
	internal void DispatchIntersectionEnd( PhysicsIntersectionEnd c ) => _body.DispatchIntersectionEnd( c );
	internal void DispatchTriggerBegin( PhysicsIntersection c ) => _body.DispatchTriggerBegin( c );
	internal void DispatchTriggerEnd( PhysicsIntersectionEnd c ) => _body.DispatchTriggerEnd( c );

	/// <summary>
	/// Get the lerped transform between physics steps.
	/// </summary>
	public Transform GetLerpedTransform( double time ) => _body.GetLerpedTransform( time );

	/// <summary>
	/// Move body to this position in a way that cooperates with the physics system.
	/// </summary>
	public void SmoothMove( in Vector3 position, float timeToArrive, float timeDelta ) => _body.SmoothMove( position, timeToArrive, timeDelta );

	/// <summary>
	/// Move body to this transform in a way that cooperates with the physics system.
	/// </summary>
	public void SmoothMove( in Transform transform, float smoothTime, float timeDelta ) => _body.SmoothMove( transform, smoothTime, timeDelta );

	/// <summary>
	/// Rotate the body to this orientation in a way that cooperates with the physics system.
	/// </summary>
	public void SmoothRotate( in Rotation rotation, float smoothTime, float timeDelta ) => _body.SmoothRotate( rotation, smoothTime, timeDelta );

	/// <summary>
	/// Enable enhanced continuous collision detection (CCD) for this body.
	/// </summary>
	public bool EnhancedCcd
	{
		set => _body.EnhancedCcd = value;
	}

	/// <summary>
	/// Locks individual axes of motion for this body.
	/// </summary>
	public PhysicsLock Locking
	{
		get => _body.Locking;
		set => _body.Locking = value;
	}

	// --- internal surface used by the engine through the public body type ---

	internal object Hitbox
	{
		get => _body.Hitbox;
		set => _body.Hitbox = value;
	}

	internal PhysicsBodyType? NavmeshBodyTypeOverride
	{
		get => _body.NavmeshBodyTypeOverride;
		set => _body.NavmeshBodyTypeOverride = value;
	}

	internal float DefaultGravityScale
	{
		get => _body.DefaultGravityScale;
		set => _body.DefaultGravityScale = value;
	}

	internal CollisionEventSystem Listener
	{
		get => _body.Listener;
		set => _body.Listener = value;
	}

	internal HashSet<Joint> Joints => _body.Joints;
	internal void AddJoint( Joint joint ) => _body.AddJoint( joint );
	internal void RemoveJoint( Joint joint ) => _body.RemoveJoint( joint );
	internal void ResetProxy() => _body.ResetProxy();
}
