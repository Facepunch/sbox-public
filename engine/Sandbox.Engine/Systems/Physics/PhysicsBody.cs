using NativeEngine;
using System.Runtime.InteropServices;
using static Sandbox.PhysicsGroupDescription.BodyPart;

namespace Sandbox;

/// <summary>
/// Represents a physics object. An entity can have multiple physics objects. See <see cref="PhysicsGroup">PhysicsGroup</see>.
/// A physics objects consists of one or more <see cref="PhysicsShape">PhysicsShape</see>s.
/// </summary>
[Expose, ActionGraphIgnore]
public sealed partial class PhysicsBody : IHandle
{
	#region IHandle
	//
	// A pointer to the actual native object
	//
	internal IPhysicsBody native;

	//
	// IHandle implementation
	//
	void IHandle.HandleInit( IntPtr ptr )
	{
		native = ptr;

		World ??= native.GetWorld();
		World.RegisterBody( this );
	}

	void IHandle.HandleDestroy() => native = IntPtr.Zero;
	bool IHandle.HandleValid() => !native.IsNull;
	#endregion

	internal PhysicsBody( HandleCreationData _ ) { }

	public PhysicsBody( PhysicsWorld world )
	{
		World = world;

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			world.world.AddBody();
		}
	}

	Component _component;

	/// <summary>
	/// The GameObject that created this body
	/// </summary>
	public GameObject GameObject { get; set; }

	/// <summary>
	/// The component that created this body
	/// </summary>
	public Component Component
	{
		get => _component;
		set
		{
			_component = value;
			GameObject = _component?.GameObject;
		}
	}

	[Obsolete( "Use Component property" )]
	public void SetComponentSource( Component c )
	{
		Component = c;
	}

	[Obsolete( "Use GameObject property" )]
	public GameObject GetGameObject() => GameObject;

	/// <summary>
	/// The Hitbox that this physics body represents
	/// </summary>
	internal object Hitbox { get; set; }

	/// <summary>
	/// Position of this body in world coordinates.
	/// </summary>
	public Vector3 Position
	{
		get => native.GetPosition();
		set
		{
			native.SetPosition( value );
			Dirty();
		}
	}

	/// <summary>
	/// The physics world this body belongs to.
	/// </summary>
	[ActionGraphInclude]
	public PhysicsWorld World { get; internal set; }

	/// <summary>
	/// Rotation of the physics body in world space.
	/// </summary>
	public Rotation Rotation
	{
		get => native.GetOrientation();
		set
		{
			native.SetOrientation( value );
			Dirty();
		}
	}

	[Obsolete]
	public float Scale => 1.0f;

	/// <summary>
	/// Linear velocity of this body in world space.
	/// </summary>
	[ActionGraphInclude]
	public Vector3 Velocity
	{
		get => native.GetLinearVelocity();
		set => native.SetLinearVelocity( value );
	}

	/// <summary>
	/// Angular velocity of this body in world space.
	/// </summary>
	[ActionGraphInclude]
	public Vector3 AngularVelocity
	{
		get => native.GetAngularVelocity();
		set => native.SetAngularVelocity( value );
	}

	/// <summary>
	/// Center of mass for this physics body in world space coordinates.
	/// </summary>
	[ActionGraphInclude]
	public Vector3 MassCenter
	{
		get => native.GetMassCenter();
	}

	/// <summary>
	/// Center of mass for this physics body relative to its <see cref="Position">origin</see>.
	/// </summary>
	[ActionGraphInclude]
	public Vector3 LocalMassCenter
	{
		get => native.GetLocalMassCenter();
		set => native.SetLocalMassCenter( value );
	}

	/// <summary>
	/// Is this physics body mass calculated or set directly.
	/// </summary>
	[ActionGraphInclude]
	public bool OverrideMassCenter
	{
		get => native.GetOverrideMassCenter();
		set => native.SetOverrideMassCenter( value );
	}

	/// <summary>
	/// Mass of this physics body.
	/// </summary>
	[ActionGraphInclude]
	public float Mass
	{
		get => native.GetMass();
		set => native.SetMass( value );
	}

	/// <summary>
	/// Whether gravity is enabled for this body or not.
	/// </summary>
	[ActionGraphInclude]
	public bool GravityEnabled
	{
		get => native.IsGravityEnabled();
		set => native.EnableGravity( value );
	}

	/// <summary>
	/// Whether to play collision sounds 
	/// </summary>
	[ActionGraphInclude]
	public bool EnableCollisionSounds { get; set; } = true;

	/// <summary>
	/// Scale the gravity relative to <see cref="PhysicsWorld.Gravity"/>. 2 is double the gravity, etc.
	/// </summary>
	[ActionGraphInclude]
	public float GravityScale
	{
		get => native.GetGravityScale();
		set => native.SetGravityScale( value * DefaultGravityScale );
	}

	internal float DefaultGravityScale { get; set; } = 1.0f;

	/// <summary>
	/// If true we'll create a controller for this physics body. This is useful
	/// for keyframed physics objects that need to push things. The controller will
	/// sweep as the entity moves, rather than teleporting the object.. which works better
	/// when pushing dynamic objects etc.
	/// </summary>
	public bool UseController { get; set; }

	/// <summary>
	/// Enables Touch callbacks on all <see cref="PhysicsShape">PhysicsShapes</see> of this body.
	/// Returns true if ANY of the physics shapes have touch events enabled.
	/// </summary>
	public bool EnableTouch
	{
		get => native.IsTouchEventEnabled();
		set
		{
			if ( value )
			{
				native.EnableTouchEvents();
			}
			else
			{
				native.DisableTouchEvents();
			}
		}
	}

	/// <summary>
	/// Sets <see cref="PhysicsShape.EnableTouchPersists"/> on all shapes of this body.
	/// <br/><br/>
	/// Returns true if ANY of the physics shapes have persistent touch events enabled.
	/// </summary>
	public bool EnableTouchPersists
	{
		get
		{
			foreach ( var body in Shapes )
			{
				if ( body.EnableTouchPersists ) return true;
			}

			return false;
		}
		set
		{
			foreach ( var shape in Shapes )
			{
				shape.EnableTouchPersists = value;
			}
		}
	}

	/// <summary>
	/// Sets <see cref="PhysicsShape.EnableSolidCollisions"/> on all shapes of this body.
	/// <br/><br/>
	/// Returns true if ANY of the physics shapes have solid collisions enabled.
	/// </summary>
	public bool EnableSolidCollisions
	{
		get
		{
			foreach ( var body in Shapes )
			{
				if ( body.EnableSolidCollisions ) return true;
			}

			return false;
		}
		set
		{
			foreach ( var shape in Shapes )
			{
				shape.EnableSolidCollisions = value;
			}
		}
	}

	// cache this, since it's called so much
	PhysicsBodyType? _bodyType;

	/// <summary>
	/// Movement type of physics body, either Static, Keyframed, Dynamic
	/// Note: If this body is networked and dynamic, it will return Keyframed on the client
	/// </summary>
	public PhysicsBodyType BodyType
	{
		get
		{
			if ( !_bodyType.HasValue )
			{
				_bodyType = native.GetType_Native();
			}

			return _bodyType.Value;
		}
		set
		{
			if ( value == BodyType )
				return;

			native.SetType( value );
			_bodyType = default;

			Dirty();
		}
	}

	/// <summary>
	/// The bodytype may change between edit and game time.
	/// For navmesh generation we always need to know the bodytype at game time.
	/// This override can be set to inform the navmesh generation of the correct game time bodytype.
	/// </summary>
	internal PhysicsBodyType? NavmeshBodyTypeOverride { get; set; }

	/// <summary>
	/// Whether this body is allowed to automatically go into "sleep" after a certain amount of time of inactivity.
	/// <see cref="Sleeping"/> for more info on the sleep mechanic.
	/// </summary>
	public bool AutoSleep
	{
		set
		{
			if ( value ) native.EnableAutoSleeping();
			else native.DisableAutoSleeping();
		}
	}

	/// <summary>
	/// Transform of this physics body.
	/// </summary>
	[ActionGraphInclude]
	public Transform Transform
	{
		get => native.GetTransform();
		set
		{
			var tx = value.WithScale( 1 );
			if ( tx.AlmostEqual( Transform ) )
				return;

			native.SetTransform( tx.Position, tx.Rotation );
			Dirty();
		}
	}

	/// <summary>
	/// Move to a new position. Unlike Transform, if you have `UseController` enabled, this will sweep the shadow
	/// to the new position, rather than teleporting there.
	/// </summary>
	public void Move( Transform tx, float delta )
	{
		if ( UseController )
		{
			native.SetTargetTransform( tx.Position, tx.Rotation, delta );
		}
		else
		{
			bool transformChanged = !tx.AlmostEqual( Transform );

			native.SetTransform( tx.Position, tx.Rotation );

			if ( transformChanged )
			{
				Dirty();
			}
		}
	}

	/// <summary>
	/// How many shapes belong to this body.
	/// </summary>
	public int ShapeCount => native.GetShapeCount();

	/// <summary>
	/// All shapes that belong to this body.
	/// </summary>
	[ActionGraphInclude]
	public IEnumerable<PhysicsShape> Shapes
	{
		get
		{
			var shapeCount = native.GetShapeCount();

			for ( int i = 0; i < shapeCount; ++i )
			{
				yield return native.GetShape( i );
			}
		}
	}

	/// <summary>
	/// Add a sphere shape to this body.
	/// </summary>
	/// <param name="center">Center of the sphere, relative to <see cref="Position"/> of this body.</param>
	/// <param name="radius">Radius of the sphere.</param>
	/// <param name="rebuildMass">Whether the mass should be <see cref="RebuildMass">recalculated</see> after adding the shape.</param>
	/// <returns>The newly created shape, if any.</returns>
	public PhysicsShape AddSphereShape( Vector3 center, float radius, bool rebuildMass = true )
	{
		var shape = native.AddSphereShape( center, radius );
		Dirty();
		return shape;
	}

	/// <summary>
	/// Add a sphere shape to this body.
	/// </summary>
	public PhysicsShape AddSphereShape( in Sphere sphere, bool rebuildMass = true )
	{
		var shape = native.AddSphereShape( sphere.Center, sphere.Radius );
		Dirty();
		return shape;
	}

	/// <summary>
	/// Add a capsule shape to this body.
	/// </summary>
	/// <param name="center">Point A of the capsule, relative to <see cref="Position"/> of this body.</param>
	/// <param name="center2">Point B of the capsule, relative to <see cref="Position"/> of this body.</param>
	/// <param name="radius">Radius of the capsule end caps.</param>
	/// <param name="rebuildMass">Whether the mass should be <see cref="RebuildMass">recalculated</see> after adding the shape.</param>
	/// <returns>The newly created shape, or null on failure.</returns>
	public PhysicsShape AddCapsuleShape( Vector3 center, Vector3 center2, float radius, bool rebuildMass = true )
	{
		var shape = native.AddCapsuleShape( center, center2, radius );
		Dirty();
		return shape;
	}

	/// <summary>
	/// Add a box shape to this body.
	/// </summary>
	/// <param name="position">Center of the box, relative to <see cref="Position"/> of this body.</param>
	/// <param name="rotation">Rotation of the box, relative to <see cref="Rotation"/> of this body.</param>
	/// <param name="extent">The extents of the box. The box will extend from its center by this much in both negative and positive directions of each axis.</param>
	/// <param name="rebuildMass">Whether the mass should be <see cref="RebuildMass">recalculated</see> after adding the shape.</param>
	/// <returns>The newly created shape, or null on failure.</returns>
	public PhysicsShape AddBoxShape( Vector3 position, Rotation rotation, Vector3 extent, bool rebuildMass = true )
	{
		var shape = native.AddBoxShape( position, rotation, extent.Abs() );
		Dirty();
		return shape;
	}

	/// <summary>
	/// Add a box shape to this body.
	/// </summary>
	public PhysicsShape AddBoxShape( BBox box, Rotation rotation, bool rebuildMass = true )
	{
		var shape = native.AddBoxShape( box.Center, rotation, box.Size * 0.5f );
		Dirty();
		return shape;
	}

	/// <inheritdoc cref="AddHullShape(Vector3, Rotation, Span{Vector3}, bool)"/>
	public PhysicsShape AddHullShape( Vector3 position, Rotation rotation, List<Vector3> points, bool rebuildMass = true )
	{
		return AddHullShape( position, rotation, CollectionsMarshal.AsSpan( points ), rebuildMass );
	}

	/// <summary>
	/// Add a convex hull shape to this body.
	/// </summary>
	/// <param name="position">Center of the hull, relative to <see cref="Position"/> of this body.</param>
	/// <param name="rotation">Rotation of the hull, relative to <see cref="Rotation"/> of this body.</param>
	/// <param name="points">Points for the hull. They will be used to generate a convex shape.</param>
	/// <param name="rebuildMass">Whether the mass should be <see cref="RebuildMass">recalculated</see> after adding the shape.</param>
	/// <returns>The newly created shape, or null on failure.</returns>
	public unsafe PhysicsShape AddHullShape( Vector3 position, Rotation rotation, Span<Vector3> points, bool rebuildMass = true )
	{
		if ( points.Length == 0 )
			return null;

		PhysicsShape shape;

		fixed ( Vector3* points_ptr = points )
		{
			shape = native.AddHullShape( position, rotation, points.Length, (IntPtr)points_ptr );
		}

		if ( !shape.IsValid() || shape.ShapeType == PhysicsShapeType.SHAPE_SPHERE )
		{
			Log.Warning( "Unable to create hull shape" );
		}

		Dirty();

		return shape;
	}

	/// <summary>
	/// Add a cylinder shape to this body.
	/// </summary>
	public PhysicsShape AddCylinderShape( Vector3 position, Rotation rotation, float height, float radius, int slices = 16 )
	{
		return AddConeShape( position, rotation, height, radius, radius, slices );
	}

	/// <summary>
	/// Add a cone shape to this body.
	/// </summary>
	public PhysicsShape AddConeShape( Vector3 position, Rotation rotation, float height, float radius1, float radius2 = 0.0f, int slices = 16 )
	{
		slices = slices.Clamp( 4, 128 );

		var vertexCount = 2 * slices;
		var points = new Vector3[vertexCount];

		var alpha = 0.0f;
		var deltaAlpha = MathF.PI * 2 / slices;
		var halfHeight = height * 0.5f;

		for ( int i = 0; i < slices; ++i )
		{
			var sinAlpha = MathF.Sin( alpha );
			var cosAlpha = MathF.Cos( alpha );

			points[2 * i + 0] = new Vector3( radius1 * cosAlpha, radius1 * sinAlpha, -halfHeight );
			points[2 * i + 1] = new Vector3( radius2 * cosAlpha, radius2 * sinAlpha, halfHeight );

			alpha += deltaAlpha;
		}

		return AddHullShape( position, rotation, points );
	}

	/// <inheritdoc cref="AddMeshShape(Span{Vector3}, Span{int})"/>
	public PhysicsShape AddMeshShape( List<Vector3> vertices, List<int> indices )
	{
		return AddMeshShape( CollectionsMarshal.AsSpan( vertices ), CollectionsMarshal.AsSpan( indices ) );
	}

	/// <summary>
	/// Adds a mesh type shape to this physics body. Mesh shapes cannot be physically simulated!
	/// </summary>
	/// <param name="vertices">Vertices of the mesh.</param>
	/// <param name="indices">Indices of the mesh.</param>
	/// <returns>The created shape, or null on failure.</returns>
	public unsafe PhysicsShape AddMeshShape( Span<Vector3> vertices, Span<int> indices )
	{
		if ( vertices.Length == 0 )
			return null;

		if ( indices.Length == 0 )
			return null;

		var vertexCount = vertices.Length;

		foreach ( var i in indices )
		{
			if ( i < 0 || i >= vertexCount )
				throw new ArgumentOutOfRangeException( $"Index ({i}) out of range ({vertexCount - 1})" );
		}

		PhysicsShape shape;

		fixed ( Vector3* vertices_ptr = vertices )
		fixed ( int* indices_ptr = indices )
		{
			shape = native.AddMeshShape( vertexCount, (IntPtr)vertices_ptr, indices.Length, (IntPtr)indices_ptr, 0 );
		}

		if ( !shape.IsValid() || shape.ShapeType == PhysicsShapeType.SHAPE_SPHERE )
		{
			Log.Warning( "Unable to create mesh shape" );
		}

		Dirty();

		return shape;
	}

	public unsafe PhysicsShape AddHeightFieldShape( ushort[] heights, byte[] materials, int sizeX, int sizeY, float sizeScale, float heightScale )
	{
		return AddHeightFieldShape( heights, materials, sizeX, sizeY, sizeScale, heightScale, 0 );
	}

	internal unsafe PhysicsShape AddHeightFieldShape( ushort[] heights, byte[] materials, int sizeX, int sizeY, float sizeScale, float heightScale, int materialCount )
	{
		if ( heights == null )
			throw new ArgumentException( "Height data is null" );

		var cellCount = sizeX * sizeY;
		if ( cellCount <= 0 )
			throw new ArgumentOutOfRangeException( "Size needs to be non zero" );

		if ( heights.Length != cellCount )
			throw new ArgumentOutOfRangeException( $"Height data length is {heights.Length}, should be {cellCount}" );

		if ( materials != null && materials.Length != cellCount )
			throw new ArgumentOutOfRangeException( $"Material data length is {materials.Length}, should be {cellCount}" );

		fixed ( ushort* pHeights = heights )
		fixed ( byte* pMaterials = materials )
		{
			var shape = native.AddHeightFieldShape(
				(IntPtr)pHeights,
				(IntPtr)pMaterials,
				sizeX, sizeY,
				sizeScale, heightScale,
				materialCount );

			Dirty();

			return shape;
		}
	}

	[Obsolete]
	public PhysicsShape AddCloneShape( PhysicsShape shape )
	{
		return null;
	}

	/// <summary>
	/// Remove all physics shapes, but not the physics body itself.
	/// </summary>
	public void ClearShapes()
	{
		native.PurgeShapes();
	}

	/// <summary>
	/// Called from Shape.Remove()
	/// </summary>
	internal void RemoveShape( PhysicsShape shape )
	{
		if ( !shape.IsValid() )
			return;

		if ( !this.IsValid() )
			return;

		if ( !World.IsValid() )
			return;

		native.RemoveShape( shape );
	}

	/// <summary>
	/// Meant to be only used on <b>dynamic</b> bodies, rebuilds mass from all shapes of this body based on their volume and <see cref="Surface">physics properties</see>, for cases where they may have changed.
	/// </summary>
	public void RebuildMass() => native.BuildMass();

	/// <summary>
	/// Completely removes this physics body.
	/// </summary>
	public void Remove()
	{
		if ( !this.IsValid() ) return;

		if ( World.IsValid() )
		{
			World.UnregisterBody( this );
		}

		native = default;
		World = default;
	}

	/// <summary>
	/// Applies instant linear impulse (i.e. a bullet impact) to this body at its center of mass.
	/// For continuous force (i.e. a moving car), use <see cref="ApplyForce"/>
	/// </summary>
	[ActionGraphInclude]
	public void ApplyImpulse( Vector3 impulse )
	{
		native.ApplyLinearImpulse( impulse );
	}

	/// <summary>
	/// Applies instant linear impulse (i.e. a bullet impact) to this body at given position.
	/// For continuous force (i.e. a moving car), use <see cref="ApplyForceAt"/>
	/// </summary>
	[ActionGraphInclude]
	public void ApplyImpulseAt( Vector3 position, Vector3 velocity )
	{
		native.ApplyLinearImpulseAtWorldSpace( velocity, position );
	}

	/// <summary>
	/// Applies instant angular impulse (i.e. a bullet impact) to this body.
	/// For continuous force (i.e. a moving car), use <see cref="ApplyTorque"/>
	/// </summary>
	[ActionGraphInclude]
	public void ApplyAngularImpulse( Vector3 impulse )
	{
		native.ApplyAngularImpulse( impulse );
	}

	/// <summary>
	/// Applies force to this body at the center of mass.
	/// This force will only be applied on the next physics frame and is scaled with physics timestep.
	/// </summary>
	[ActionGraphInclude]
	public void ApplyForce( Vector3 force ) => native.ApplyForce( force );

	/// <summary>
	/// Applies force to this body at given position.
	/// This force will only be applied on the next physics frame and is scaled with physics timestep.
	/// </summary>
	[ActionGraphInclude]
	public void ApplyForceAt( Vector3 position, Vector3 force ) => native.ApplyForceAt( force, position );

	/// <summary>
	/// Applies angular velocity to this body.
	/// This force will only be applied on the next physics frame and is scaled with physics timestep.
	/// </summary>
	/// <param name="force"></param>
	[ActionGraphInclude]
	public void ApplyTorque( Vector3 force ) => native.ApplyTorque( force );

	/// <summary>
	/// Clear accumulated linear forces (<see cref="ApplyForce"/> and <see cref="ApplyForceAt"/>) during this physics frame that were not yet applied to the physics body.
	/// </summary>
	public void ClearForces()
	{
		native.ClearForces();
	}

	/// <summary>
	/// Clear accumulated torque (angular force, <see cref="ApplyTorque"/>) during this physics frame that were not yet applied to the physics body.
	/// </summary>
	public void ClearTorque()
	{
		native.ClearTorque();
	}

	/// <summary>
	/// Returns the world space velocity of a point of the object. This is useful for objects rotating around their own axis/origin.
	/// </summary>
	/// <param name="point">The point to test, in world coordinates.</param>
	/// <returns>Velocity at the given point.</returns>
	[ActionGraphInclude, Pure]
	public Vector3 GetVelocityAtPoint( Vector3 point )
	{
		return native.GetVelocityAtPoint( point );
	}

	/// <summary>
	/// Whether this body is enabled or not. Disables collisions, physics simulation, touch events, trace queries, etc.
	/// </summary>
	[ActionGraphInclude]
	public bool Enabled
	{
		get => native.IsEnabled();
		set
		{
			if ( native.IsNull )
				return;

			if ( value ) native.Enable();
			else native.Disable();

			Dirty();
		}
	}

	/// <summary>
	/// Controls physics simulation on this body.
	/// </summary>
	[ActionGraphInclude]
	public bool MotionEnabled
	{
		get => BodyType == PhysicsBodyType.Dynamic;
		set
		{
			if ( value )
			{
				BodyType = PhysicsBodyType.Dynamic;

				return;
			}

			// Clear velocity when disabling motion.
			// We do this here (not in BodyType setter) in case preserving velocity is intentional.
			// Also, disabling motion implies that all motion stops.
			if ( BodyType == PhysicsBodyType.Dynamic )
			{
				Velocity = 0;
				AngularVelocity = 0;
			}

			BodyType = PhysicsBodyType.Keyframed;
		}
	}

	/// <summary>
	/// Physics bodies automatically go to sleep after a certain amount of time of inactivity to save on performance.
	/// You can use this to wake the body up, or prematurely send it to sleep.
	/// </summary>
	[ActionGraphInclude]
	public bool Sleeping
	{
		get => native.IsSleeping();

		set
		{
			if ( value ) native.Sleep();
			else native.Wake();
		}
	}

	/// <summary>
	/// If enabled, this physics body will move slightly ahead each frame based on its velocities.
	/// </summary>
	[Obsolete( "No longer exists" )]
	public bool SpeculativeContactEnabled
	{
		get => false;
		set { }
	}

	/// <summary>
	/// The physics body we are attached to, if any
	/// </summary>
	[ActionGraphInclude]
	public PhysicsBody Parent { get; set; }

	/// <summary>
	/// A convenience property, returns <see cref="Parent">Parent</see>, or if there is no parent, returns itself.
	/// </summary>
	public PhysicsBody SelfOrParent => Parent ?? this;

	/// <summary>
	/// The physics group we belong to.
	/// </summary>
	[ActionGraphInclude]
	public PhysicsGroup PhysicsGroup
	{
		get
		{
			if ( native.IsNull ) return null;
			return native.GetAggregate();
		}
	}

	/// <summary>
	/// Returns the closest point to the given one between all shapes of this body.
	/// </summary>
	/// <param name="vec">Input position.</param>
	/// <returns>The closest possible position on the surface of the physics body to the given position.</returns>
	[ActionGraphInclude, Pure]
	public Vector3 FindClosestPoint( Vector3 vec )
	{
		return native.GetClosestPoint( vec );
	}

	/// <summary>
	/// Generic linear damping, i.e. how much the physics body will slow down on its own.
	/// </summary>
	[ActionGraphInclude]
	public float LinearDamping
	{
		get => native.GetLinearDamping();
		set => native.SetLinearDamping( value );
	}

	/// <summary>
	/// Generic angular damping, i.e. how much the physics body will slow down on its own.
	/// </summary>
	[ActionGraphInclude]
	public float AngularDamping
	{
		get => native.GetAngularDamping();
		set => native.SetAngularDamping( value );
	}

	[Obsolete]
	public float LinearDrag
	{
		get => default;
		set { }
	}

	[Obsolete]
	public float AngularDrag
	{
		get => default;
		set { }
	}

	[Obsolete]
	public bool DragEnabled
	{
		get => default;
		set { }
	}

	/// <summary>
	/// The diagonal elements of the local inertia tensor matrix.
	/// </summary>
	[ActionGraphInclude]
	public Vector3 Inertia
	{
		get => native.GetLocalInertiaVector();
	}

	/// <summary>
	/// The orientation of the principal axes of local inertia tensor matrix.
	/// </summary>
	[ActionGraphInclude]
	public Rotation InertiaRotation
	{
		get => native.GetLocalInertiaOrientation();
	}

	/// <summary>
	/// Sets the inertia tensor using the given moments and rotation.
	/// </summary>
	/// <param name="inertia">Principal moments (Ixx, Iyy, Izz).</param>
	/// <param name="rotation">Rotation of the principal axes.</param>
	public void SetInertiaTensor( Vector3 inertia, Rotation rotation )
	{
		native.SetLocalInertia( inertia, rotation );
	}

	/// <summary>
	/// Resets the inertia tensor to its calculated values.
	/// </summary>
	public void ResetInertiaTensor()
	{
		native.ResetLocalInertia();
	}

	/// <summary>
	/// Returns Axis-Aligned Bounding Box (AABB) of this physics body.
	/// </summary>
	[ActionGraphInclude, Pure]
	public BBox GetBounds()
	{
		return native.BuildBounds();
	}

	/// <summary>
	/// Returns average of densities for all physics shapes of this body. This is based on <see cref="PhysicsShape.SurfaceMaterial"/> of each shape.
	/// </summary>
	[ActionGraphInclude]
	public float Density
	{
		get => native.GetDensity();
	}

	/// <summary>
	/// Time since last water splash effect. Used internally.
	/// </summary>
	public RealTimeSince LastWaterEffect { get; set; }

	/// <summary>
	/// Sets <see cref="PhysicsShape.SurfaceMaterial"/> on all child <see cref="PhysicsShape">PhysicsShape</see>s.
	/// </summary>
	/// <returns>
	/// The most commonly occurring surface name between all <see cref="PhysicsShape">PhysicsShape</see>s of this <see cref="PhysicsShape">PhysicsBody</see>.
	/// </returns>
	[ActionGraphInclude]
	public string SurfaceMaterial
	{
		get
		{
			if ( !Shapes.Any() ) return "default";

			return Shapes.Select( s => s.SurfaceMaterial )
					.GroupBy( v => v )
					.OrderByDescending( g => g.Count() )
					.First().Key;
		}

		set
		{
			native.SetMaterialIndex( value );
		}
	}

	Surface _surface;

	public Surface Surface
	{
		get
		{
			// todo - if _surface is null, look up from GetMaterialName()
			return _surface;
		}
		set
		{
			if ( _surface == value ) return;

			_surface = value;
			native.SetMaterialIndex( _surface?.ResourceName );
		}
	}

	/// <summary>
	/// Convenience function that returns a <see cref="PhysicsPoint"/> from a position relative to this body.
	/// </summary>
	public PhysicsPoint LocalPoint( Vector3 p ) => PhysicsPoint.Local( this, p );

	/// <summary>
	/// Convenience function that returns a <see cref="PhysicsPoint"/> for this body from a world space position.
	/// </summary>
	public PhysicsPoint WorldPoint( Vector3 p ) => PhysicsPoint.World( this, p );

	/// <summary>
	/// Returns a <see cref="PhysicsPoint"/> at the center of mass of this body.
	/// </summary>
	public PhysicsPoint MassCenterPoint() => PhysicsPoint.Local( this, LocalMassCenter );


	/// <summary>
	/// What is this body called in the group?
	/// </summary>
	public string GroupName
	{
		get
		{
			// should we be caching this stuff? When should it invalidate?
			return PhysicsGroup?.native.GetBodyName( GroupIndex );
		}
	}

	/// <summary>
	/// Return the index of this body in its PhysicsGroup
	/// </summary>
	public int GroupIndex
	{
		get
		{
			// should we be caching this stuff? When should it invalidate?
			return PhysicsGroup?.native.GetBodyIndex( this ) ?? 0;
		}
	}

	/// <summary>
	/// Checks if another body overlaps us, ignoring all collision rules
	/// </summary>
	public bool CheckOverlap( PhysicsBody body )
	{
		if ( !body.IsValid() )
			return false;

		return CheckOverlap( body, body.Transform );
	}

	/// <summary>
	/// Checks if another body overlaps us at a given transform, ignoring all collision rules
	/// </summary>
	public bool CheckOverlap( PhysicsBody body, Transform transform )
	{
		if ( !this.IsValid() || !body.IsValid() )
			return false;

		return native.CheckOverlap( body, transform );
	}

	/// <summary>
	/// Checks if there's any contact points with another body
	/// </summary>
	internal bool IsTouching( PhysicsBody body, bool triggersOnly )
	{
		if ( !body.IsValid() )
			return false;

		return native.IsTouching( body, triggersOnly );
	}

	/// <summary>
	/// Checks if there's any contact points with another shape
	/// </summary>
	internal bool IsTouching( PhysicsShape shape, bool triggersOnly )
	{
		if ( !shape.IsValid() )
			return false;

		return native.IsTouching( shape, triggersOnly );
	}

	/// <summary>
	/// Add a shape from a physics hull
	/// </summary>
	public PhysicsShape AddShape( HullPart part, Transform transform, bool rebuildMass = true )
	{
		var shape = native.AddHullShape( part.hull, transform );

		if ( !shape.IsValid() || shape.ShapeType == PhysicsShapeType.SHAPE_SPHERE )
		{
			Log.Warning( "Unable to create hull shape" );
		}

		Dirty();

		return shape;
	}

	/// <summary>
	/// Add a shape from a mesh hull
	/// </summary>
	public PhysicsShape AddShape( MeshPart part, Transform transform, bool convertToHull, bool rebuildMass = true )
	{
		PhysicsShape shape;

		if ( convertToHull )
		{
			shape = native.AddHullShape( part.mesh, transform );
		}
		else
		{
			shape = native.AddMeshShape( part.mesh, transform, part.Surfaces is null ? 0 : part.Surfaces.Length );
		}

		if ( !shape.IsValid() || shape.ShapeType == PhysicsShapeType.SHAPE_SPHERE )
		{
			Log.Warning( $"Unable to create {(convertToHull ? "hull" : "mesh")} shape" );
		}

		Dirty();

		return shape;
	}

	public Action<PhysicsIntersection> OnIntersectionStart { get; set; }
	public Action<PhysicsIntersection> OnIntersectionUpdate { get; set; }
	public Action<PhysicsIntersectionEnd> OnIntersectionEnd { get; set; }
	internal Action<PhysicsIntersection> OnTriggerBegin { get; set; }
	internal Action<PhysicsIntersectionEnd> OnTriggerEnd { get; set; }

	/// <summary>
	/// Transform, on previous step
	/// </summary>
	Transform prevStepTransform;
	float prevStepTime;

	/// <summary>
	/// Transform on current step
	/// </summary>
	Transform stepTransform;
	float stepTime;


	/// <summary>
	/// Called on each active body after a "step"
	/// </summary>
	internal void OnActive( in Transform transform, in Vector3 velocity, in Vector3 linearVelocity )
	{
		prevStepTime = stepTime;
		prevStepTransform = stepTime > 0 ? stepTransform : transform;

		stepTransform = transform;
		stepTime = World.CurrentTime;

		Dirty();
	}

	/// <summary>
	/// When the physics world is run at a fixed timestep, getting the positions of bodies will not be smooth.
	/// You can use this function to get the lerped position between steps, to make things super awesome.
	/// </summary>
	public Transform GetLerpedTransform( float time )
	{
		if ( stepTime == 0 )
			return Transform;

		// lerp gap is too big
		if ( stepTime - prevStepTime > 0.5f )
			return Transform;

		time -= World.CurrentDelta;

		var delta = MathX.Remap( time, prevStepTime, stepTime, 0.0f, 1.0f );
		return Transform.Lerp( prevStepTransform, stepTransform, delta, true );
	}

	/// <summary>
	/// Move body to this position in a way that cooperates with the physics system. This is quite
	/// good for things like grabbing and moving objects.
	/// </summary>
	[ActionGraphInclude]
	public void SmoothMove( in Vector3 position, float timeToArrive, float timeDelta )
	{
		var velocity = Velocity;
		Vector3.SmoothDamp( Position, position, ref velocity, timeToArrive, timeDelta );
		Velocity = velocity;
	}

	/// <summary>
	/// Move body to this position in a way that cooperates with the physics system. This is quite
	/// good for things like grabbing and moving objects.
	/// </summary>
	[ActionGraphInclude]
	public void SmoothMove( in Transform transform, float smoothTime, float timeDelta )
	{
		SmoothMove( transform.Position, smoothTime, timeDelta );
		SmoothRotate( transform.Rotation, smoothTime, timeDelta );
	}

	/// <summary>
	/// Rotate the body to this position in a way that cooperates with the physics system.
	/// </summary>
	[ActionGraphInclude]
	public void SmoothRotate( in Rotation rotation, float smoothTime, float timeDelta )
	{
		var angVelocity = AngularVelocity;
		Rotation.SmoothDamp( Rotation, rotation, ref angVelocity, smoothTime, timeDelta );
		AngularVelocity = angVelocity;
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


	internal HashSet<Joint> Joints = new HashSet<Joint>();

	internal void AddJoint( Joint joint )
	{
		Joints.Add( joint );
	}

	internal void RemoveJoint( Joint joint )
	{
		Joints.Remove( joint );
	}
}
