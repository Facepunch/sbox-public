namespace Sandbox;

/// <summary>
/// Internal base class for physics bodies. Contains all body functions and properties.
/// The public <see cref="PhysicsBody"/> wrapper delegates to an instance of this.
/// </summary>
internal abstract partial class PhysicsBodyInternal : IValid
{
	public abstract bool IsValid { get; }

	PhysicsBody _owner;

	/// <summary>
	/// The public wrapper for this body. Created lazily for bodies that originate native-side.
	/// </summary>
	internal PhysicsBody Owner => _owner ??= new PhysicsBody( this );

	/// <summary>
	/// Associates this internal body with its public wrapper (only sets it once).
	/// </summary>
	internal void SetOwner( PhysicsBody owner ) => _owner ??= owner;

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
	public virtual Vector3 Position { get; set; }

	/// <summary>
	/// The physics world this body belongs to.
	/// </summary>
	internal PhysicsWorldInternal World { get; set; }

	/// <summary>
	/// Rotation of the physics body in world space.
	/// </summary>
	public virtual Rotation Rotation { get; set; }

	[Obsolete]
	public virtual float Scale => 1.0f;

	/// <summary>
	/// Linear velocity of this body in world space.
	/// </summary>
	public virtual Vector3 Velocity { get; set; }

	/// <summary>
	/// Angular velocity of this body in world space.
	/// </summary>
	public virtual Vector3 AngularVelocity { get; set; }

	/// <summary>
	/// Center of mass for this physics body in world space coordinates.
	/// </summary>
	public virtual Vector3 MassCenter { get; }

	/// <summary>
	/// Center of mass for this physics body relative to its <see cref="Position">origin</see>.
	/// </summary>
	public virtual Vector3 LocalMassCenter { get; set; }

	/// <summary>
	/// Is this physics body mass calculated or set directly.
	/// </summary>
	public virtual bool OverrideMassCenter { get; set; }

	/// <summary>
	/// Mass of this physics body.
	/// </summary>
	public virtual float Mass { get; set; }

	/// <summary>
	/// Whether gravity is enabled for this body or not.
	/// </summary>
	public virtual bool GravityEnabled { get; set; } = true;

	/// <summary>
	/// Whether to play collision sounds
	/// </summary>
	public bool EnableCollisionSounds { get; set; } = true;

	/// <summary>
	/// Scale the gravity relative to <see cref="PhysicsWorld.Gravity"/>. 2 is double the gravity, etc.
	/// </summary>
	public virtual float GravityScale { get; set; } = 1.0f;

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
	public virtual bool EnableTouch { get; set; }

	/// <summary>
	/// Sets <see cref="PhysicsShape.EnableTouchPersists"/> on all shapes of this body.
	/// <br/><br/>
	/// Returns true if ANY of the physics shapes have persistent touch events enabled.
	/// </summary>
	public virtual bool EnableTouchPersists
	{
		get
		{
			foreach ( var shape in Shapes )
			{
				if ( shape.EnableTouchPersists ) return true;
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
	public virtual bool EnableSolidCollisions
	{
		get
		{
			foreach ( var shape in Shapes )
			{
				if ( shape.EnableSolidCollisions ) return true;
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

	PhysicsBodyType? _bodyType;

	/// <summary>
	/// Movement type of physics body, either Static, Keyframed, Dynamic
	/// Note: If this body is networked and dynamic, it will return Keyframed on the client
	/// </summary>
	public virtual PhysicsBodyType BodyType
	{
		get => _bodyType ?? PhysicsBodyType.Static;
		set
		{
			_bodyType = value;
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
	public virtual bool AutoSleep { set { } }

	/// <summary>
	/// The speed threshold below which this body will be put to sleep. Units per second.
	/// The default is about 2 units/sec. Increase this to make bodies sleep sooner, which is useful for stacking stability.
	/// </summary>
	public virtual float SleepThreshold { get; set; }

	/// <summary>
	/// Transform of this physics body.
	/// </summary>
	public virtual Transform Transform { get; set; }

	/// <summary>
	/// Move to a new position. Unlike Transform, if you have `UseController` enabled, this will sweep the shadow
	/// to the new position, rather than teleporting there.
	/// </summary>
	public virtual void Move( Transform tx, float delta )
	{
		Transform = tx;
	}

	/// <summary>
	/// How many shapes belong to this body.
	/// </summary>
	public virtual int ShapeCount => 0;

	/// <summary>
	/// All shapes that belong to this body.
	/// </summary>
	public virtual IEnumerable<PhysicsShape> Shapes => Enumerable.Empty<PhysicsShape>();

	/// <summary>
	/// Add a sphere shape to this body.
	/// </summary>
	/// <param name="center">Center of the sphere, relative to <see cref="Position"/> of this body.</param>
	/// <param name="radius">Radius of the sphere.</param>
	/// <param name="rebuildMass">Whether the mass should be <see cref="RebuildMass">recalculated</see> after adding the shape.</param>
	/// <returns>The newly created shape, if any.</returns>
	public virtual PhysicsShape AddSphereShape( Vector3 center, float radius, bool rebuildMass = true ) => throw new NotSupportedException();

	/// <summary>
	/// Add a sphere shape to this body.
	/// </summary>
	public virtual PhysicsShape AddSphereShape( in Sphere sphere, bool rebuildMass = true ) => throw new NotSupportedException();

	/// <summary>
	/// Add a capsule shape to this body.
	/// </summary>
	/// <param name="center">Point A of the capsule, relative to <see cref="Position"/> of this body.</param>
	/// <param name="center2">Point B of the capsule, relative to <see cref="Position"/> of this body.</param>
	/// <param name="radius">Radius of the capsule end caps.</param>
	/// <param name="rebuildMass">Whether the mass should be <see cref="RebuildMass">recalculated</see> after adding the shape.</param>
	/// <returns>The newly created shape, or null on failure.</returns>
	public virtual PhysicsShape AddCapsuleShape( Vector3 center, Vector3 center2, float radius, bool rebuildMass = true ) => throw new NotSupportedException();

	/// <summary>
	/// Add a box shape to this body.
	/// </summary>
	/// <param name="position">Center of the box, relative to <see cref="Position"/> of this body.</param>
	/// <param name="rotation">Rotation of the box, relative to <see cref="Rotation"/> of this body.</param>
	/// <param name="extent">The extents of the box. The box will extend from its center by this much in both negative and positive directions of each axis.</param>
	/// <param name="rebuildMass">Whether the mass should be <see cref="RebuildMass">recalculated</see> after adding the shape.</param>
	/// <returns>The newly created shape, or null on failure.</returns>
	public virtual PhysicsShape AddBoxShape( Vector3 position, Rotation rotation, Vector3 extent, bool rebuildMass = true ) => throw new NotSupportedException();

	/// <summary>
	/// Add a box shape to this body.
	/// </summary>
	public virtual PhysicsShape AddBoxShape( BBox box, Rotation rotation, bool rebuildMass = true ) => throw new NotSupportedException();

	/// <summary>
	/// Add a plane shape to this body.
	/// </summary>
	/// <param name="center">Center of the plane, relative to this body.</param>
	/// <param name="rotation">Orientation of the plane.</param>
	/// <param name="size">Width and height of the plane.</param>
	/// <param name="rebuildMass">Whether the mass should be recalculated after adding the shape.</param>
	public virtual PhysicsShape AddPlaneShape( Vector3 center, Rotation rotation, Vector2 size, bool rebuildMass = true ) => throw new NotSupportedException();

	/// <inheritdoc cref="AddHullShape(Vector3, Rotation, Span{Vector3}, bool)"/>
	public virtual PhysicsShape AddHullShape( Vector3 position, Rotation rotation, List<Vector3> points, bool rebuildMass = true ) => throw new NotSupportedException();

	/// <summary>
	/// Add a convex hull shape to this body.
	/// </summary>
	/// <param name="position">Center of the hull, relative to <see cref="Position"/> of this body.</param>
	/// <param name="rotation">Rotation of the hull, relative to <see cref="Rotation"/> of this body.</param>
	/// <param name="points">Points for the hull. They will be used to generate a convex shape.</param>
	/// <param name="rebuildMass">Whether the mass should be <see cref="RebuildMass">recalculated</see> after adding the shape.</param>
	/// <returns>The newly created shape, or null on failure.</returns>
	public virtual unsafe PhysicsShape AddHullShape( Vector3 position, Rotation rotation, Span<Vector3> points, bool rebuildMass = true ) => throw new NotSupportedException();

	/// <summary>
	/// Add a cylinder shape to this body.
	/// </summary>
	public virtual PhysicsShape AddCylinderShape( Vector3 position, Rotation rotation, float height, float radius, int slices = 16 )
	{
		return AddConeShape( position, rotation, height, radius, radius, slices );
	}

	/// <summary>
	/// Add a cone shape to this body.
	/// </summary>
	public virtual PhysicsShape AddConeShape( Vector3 position, Rotation rotation, float height, float radius1, float radius2 = 0.0f, int slices = 16 )
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

			points[2 * i + 0] = new Vector3( -halfHeight, radius1 * cosAlpha, radius1 * sinAlpha );
			points[2 * i + 1] = new Vector3( halfHeight, radius2 * cosAlpha, radius2 * sinAlpha );

			alpha += deltaAlpha;
		}

		return AddHullShape( position, rotation, points );
	}

	/// <summary>
	/// Add a cone shape to this body.
	/// </summary>
	public virtual PhysicsShape AddConeShape( Vector3 a, Vector3 b, float radiusA, float radiusB, int slices = 16 )
	{
		slices = slices.Clamp( 4, 128 );

		var axis = b - a;
		var length = axis.Length;

		if ( length <= 0 )
			return AddSphereShape( a, radiusA );

		var rotation = Rotation.LookAt( axis.Normal );
		var position = (a + b) * 0.5f;

		return AddConeShape( position, rotation, length, radiusA, radiusB, slices );
	}

	/// <inheritdoc cref="AddMeshShape(Span{Vector3}, Span{int})"/>
	public virtual PhysicsShape AddMeshShape( List<Vector3> vertices, List<int> indices ) => throw new NotSupportedException();

	/// <summary>
	/// Adds a mesh type shape to this physics body. Mesh shapes cannot be physically simulated!
	/// </summary>
	/// <param name="vertices">Vertices of the mesh.</param>
	/// <param name="indices">Indices of the mesh.</param>
	/// <returns>The created shape, or null on failure.</returns>
	public virtual unsafe PhysicsShape AddMeshShape( Span<Vector3> vertices, Span<int> indices ) => throw new NotSupportedException();

	public virtual unsafe PhysicsShape AddHeightFieldShape( ushort[] heights, byte[] materials, int sizeX, int sizeY, float sizeScale, float heightScale )
	{
		return AddHeightFieldShape( heights, materials, sizeX, sizeY, sizeScale, heightScale, 0 );
	}

	internal virtual unsafe PhysicsShape AddHeightFieldShape( ushort[] heights, byte[] materials, int sizeX, int sizeY, float sizeScale, float heightScale, int materialCount ) => throw new NotSupportedException();

	[Obsolete]
	public PhysicsShape AddCloneShape( PhysicsShape shape ) => null;

	/// <summary>
	/// Add a shape from a physics hull
	/// </summary>
	public virtual PhysicsShape AddShape( PhysicsGroupDescription.BodyPart.HullPart part, Transform transform, bool rebuildMass = true ) => throw new NotSupportedException();

	/// <summary>
	/// Add a shape from a mesh hull
	/// </summary>
	public virtual PhysicsShape AddShape( PhysicsGroupDescription.BodyPart.MeshPart part, Transform transform, bool convertToHull, bool rebuildMass = true ) => throw new NotSupportedException();

	/// <summary>
	/// Remove all physics shapes, but not the physics body itself.
	/// </summary>
	public virtual void ClearShapes() { }

	/// <summary>
	/// Returns a scope that marks subsequently created shapes as triggers.
	/// Dispose the scope to restore the default non-trigger state.
	/// </summary>
	internal virtual IDisposable TriggerScope() => null;

	/// <summary>
	/// Called from Shape.Remove()
	/// </summary>
	internal virtual void RemoveShape( PhysicsShape shape ) { }

	/// <summary>
	/// Meant to be only used on <b>dynamic</b> bodies, rebuilds mass from all shapes of this body based on their volume and <see cref="Surface">physics properties</see>, for cases where they may have changed.
	/// </summary>
	public virtual void RebuildMass() { }

	/// <summary>
	/// Completely removes this physics body.
	/// </summary>
	public virtual void Remove()
	{
		if ( World.IsValid() )
		{
			World.UnregisterBody( this );
		}

		World = default;
	}

	/// <summary>
	/// Applies instant linear impulse (i.e. a bullet impact) to this body at its center of mass.
	/// For continuous force (i.e. a moving car), use <see cref="ApplyForce"/>
	/// </summary>
	public virtual void ApplyImpulse( Vector3 impulse ) { }

	/// <summary>
	/// Applies instant linear impulse (i.e. a bullet impact) to this body at given position.
	/// For continuous force (i.e. a moving car), use <see cref="ApplyForceAt"/>
	/// </summary>
	public virtual void ApplyImpulseAt( Vector3 position, Vector3 velocity ) { }

	/// <summary>
	/// Applies instant angular impulse (i.e. a bullet impact) to this body.
	/// For continuous force (i.e. a moving car), use <see cref="ApplyTorque"/>
	/// </summary>
	public virtual void ApplyAngularImpulse( Vector3 impulse ) { }

	/// <summary>
	/// Applies force to this body at the center of mass.
	/// This force will only be applied on the next physics frame and is scaled with physics timestep.
	/// </summary>
	public virtual void ApplyForce( Vector3 force ) { }

	/// <summary>
	/// Applies force to this body at given position.
	/// This force will only be applied on the next physics frame and is scaled with physics timestep.
	/// </summary>
	public virtual void ApplyForceAt( Vector3 position, Vector3 force ) { }

	/// <summary>
	/// Applies angular velocity to this body.
	/// This force will only be applied on the next physics frame and is scaled with physics timestep.
	/// </summary>
	public virtual void ApplyTorque( Vector3 force ) { }

	/// <summary>
	/// Applies a buoyancy and drag impulse to this body for a fluid bounded by the given plane.
	/// </summary>
	public virtual void ApplyBuoyancy( Plane plane, float fluidDensity, float linearDrag, float angularDrag, Vector3 fluidVelocity, Vector3 gravity, float dt ) { }

	/// <summary>
	/// Clear accumulated linear forces (<see cref="ApplyForce"/> and <see cref="ApplyForceAt"/>) during this physics frame that were not yet applied to the physics body.
	/// </summary>
	public virtual void ClearForces() { }

	/// <summary>
	/// Clear accumulated torque (angular force, <see cref="ApplyTorque"/>) during this physics frame that were not yet applied to the physics body.
	/// </summary>
	public virtual void ClearTorque() { }

	/// <summary>
	/// Returns the world space velocity of a point of the object. This is useful for objects rotating around their own axis/origin.
	/// </summary>
	/// <param name="point">The point to test, in world coordinates.</param>
	/// <returns>Velocity at the given point.</returns>
	[Pure]
	public virtual Vector3 GetVelocityAtPoint( Vector3 point ) => default;

	/// <summary>
	/// Whether this body is enabled or not. Disables collisions, physics simulation, touch events, trace queries, etc.
	/// </summary>
	public virtual bool Enabled { get; set; } = true;

	/// <summary>
	/// Controls physics simulation on this body.
	/// </summary>
	public virtual bool MotionEnabled
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
	public virtual bool Sleeping { get; set; }

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
	public PhysicsBody Parent { get; set; }

	/// <summary>
	/// A convenience property, returns <see cref="Parent">Parent</see>, or if there is no parent, returns itself.
	/// </summary>
	public PhysicsBody SelfOrParent => Parent ?? Owner;

	/// <summary>
	/// The physics group we belong to.
	/// </summary>
	public virtual PhysicsGroup PhysicsGroup => null;

	/// <summary>
	/// Returns the closest point to the given one between all shapes of this body.
	/// </summary>
	/// <param name="vec">Input position.</param>
	/// <returns>The closest possible position on the surface of the physics body to the given position.</returns>
	[Pure]
	public virtual Vector3 FindClosestPoint( Vector3 vec ) => vec;

	/// <summary>
	/// Generic linear damping, i.e. how much the physics body will slow down on its own.
	/// </summary>
	public virtual float LinearDamping { get; set; }

	/// <summary>
	/// Generic angular damping, i.e. how much the physics body will slow down on its own.
	/// </summary>
	public virtual float AngularDamping { get; set; }

	[Obsolete]
	public float LinearDrag { get => default; set { } }

	[Obsolete]
	public float AngularDrag { get => default; set { } }

	[Obsolete]
	public bool DragEnabled { get => default; set { } }

	/// <summary>
	/// The diagonal elements of the local inertia tensor matrix.
	/// </summary>
	public virtual Vector3 Inertia => default;

	/// <summary>
	/// The orientation of the principal axes of local inertia tensor matrix.
	/// </summary>
	public virtual Rotation InertiaRotation => Rotation.Identity;

	/// <summary>
	/// Sets the inertia tensor using the given moments and rotation.
	/// </summary>
	/// <param name="inertia">Principal moments (Ixx, Iyy, Izz).</param>
	/// <param name="rotation">Rotation of the principal axes.</param>
	public virtual void SetInertiaTensor( Vector3 inertia, Rotation rotation ) { }

	/// <summary>
	/// Resets the inertia tensor to its calculated values.
	/// </summary>
	public virtual void ResetInertiaTensor() { }

	/// <summary>
	/// Returns Axis-Aligned Bounding Box (AABB) of this physics body.
	/// </summary>
	[Pure]
	public virtual BBox GetBounds() => default;

	/// <summary>
	/// Returns average of densities for all physics shapes of this body. This is based on <see cref="PhysicsShape.SurfaceMaterial"/> of each shape.
	/// </summary>
	public virtual float Density => 0;

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
	public virtual string SurfaceMaterial
	{
		get
		{
			if ( !Shapes.Any() ) return "default";

			return Shapes.Select( s => s.SurfaceMaterial )
					.GroupBy( v => v )
					.OrderByDescending( g => g.Count() )
					.First().Key;
		}
		set { }
	}

	protected Surface _surface;

	public virtual Surface Surface
	{
		get => _surface;
		set => _surface = value;
	}
	/// <summary>
	/// Convenience function that returns a <see cref="PhysicsPoint"/> from a position relative to this body.
	/// </summary>
	public PhysicsPoint LocalPoint( Vector3 p ) => PhysicsPoint.Local( Owner, p );

	/// <summary>
	/// Convenience function that returns a <see cref="PhysicsPoint"/> for this body from a world space position.
	/// </summary>
	public PhysicsPoint WorldPoint( Vector3 p ) => PhysicsPoint.World( Owner, p );

	/// <summary>
	/// Returns a <see cref="PhysicsPoint"/> at the center of mass of this body.
	/// </summary>
	public PhysicsPoint MassCenterPoint() => PhysicsPoint.Local( Owner, LocalMassCenter );

	/// <summary>
	/// What is this body called in the group?
	/// </summary>
	public virtual string GroupName => null;

	/// <summary>
	/// Return the index of this body in its PhysicsGroup
	/// </summary>
	public virtual int GroupIndex => 0;

	/// <summary>
	/// Checks if another body overlaps us, ignoring all collision rules
	/// </summary>
	public virtual bool CheckOverlap( PhysicsBody body ) => false;

	/// <summary>
	/// Checks if another body overlaps us at a given transform, ignoring all collision rules
	/// </summary>
	public virtual bool CheckOverlap( PhysicsBody body, Transform transform ) => false;

	/// <summary>
	/// Finds the smallest move needed to separate us from another body, ignoring all collision rules.
	/// Returns true if we're overlapping; moving us by <paramref name="direction"/> * <paramref name="distance"/> pushes us clear.
	/// </summary>
	public virtual bool ComputePenetration( PhysicsBody body, out Vector3 direction, out float distance )
	{
		direction = default;
		distance = default;
		return false;
	}

	/// <summary>
	/// Finds the smallest move needed to separate us from another body placed at a given transform, ignoring
	/// all collision rules. Returns true if we're overlapping; moving us by <paramref name="direction"/> *
	/// <paramref name="distance"/> pushes us clear.
	/// </summary>
	public virtual bool ComputePenetration( PhysicsBody body, Transform transform, out Vector3 direction, out float distance )
	{
		direction = default;
		distance = default;
		return false;
	}

	/// <summary>
	/// Checks if there's any contact points with another body
	/// </summary>
	internal virtual bool IsTouching( PhysicsBody body, bool triggersOnly ) => false;

	/// <summary>
	/// Checks if there's any contact points with another shape
	/// </summary>
	internal virtual bool IsTouching( PhysicsShape shape, bool triggersOnly ) => false;

	public Action<PhysicsIntersection> OnIntersectionStart { get; set; }
	public Action<PhysicsIntersection> OnIntersectionUpdate { get; set; }
	public Action<PhysicsIntersectionEnd> OnIntersectionEnd { get; set; }

	internal CollisionEventSystem Listener { get; set; }

	internal void DispatchIntersectionStart( PhysicsIntersection c )
	{
		Listener?.OnIntersectionStart( c );
		OnIntersectionStart?.InvokeWithWarning( c );
	}

	internal void DispatchIntersectionUpdate( PhysicsIntersection c )
	{
		Listener?.OnIntersectionUpdate( c );
		OnIntersectionUpdate?.InvokeWithWarning( c );
	}

	internal void DispatchIntersectionEnd( PhysicsIntersectionEnd c )
	{
		Listener?.OnIntersectionEnd( c );
		OnIntersectionEnd?.InvokeWithWarning( c );
	}

	internal void DispatchTriggerBegin( PhysicsIntersection c ) => Listener?.OnTriggerBegin( c );
	internal void DispatchTriggerEnd( PhysicsIntersectionEnd c ) => Listener?.OnTriggerEnd( c );

	/// <summary>
	/// Transform, on previous step
	/// </summary>
	Transform prevStepTransform;
	double prevStepTime;

	/// <summary>
	/// Transform on current step
	/// </summary>
	Transform stepTransform;
	double stepTime;

	/// <summary>
	/// Called on each active body after a "step"
	/// </summary>
	internal virtual void OnActive( in Transform transform, in Vector3 velocity, in Vector3 linearVelocity, bool fellAsleep, bool wentOutOfBounds )
	{
		prevStepTime = stepTime;
		prevStepTransform = stepTime > 0 ? stepTransform : transform;

		stepTransform = transform;
		stepTime = World.CurrentTime;

		Dirty();

		if ( World is PhysicsWorld3d world3d )
		{
			if ( wentOutOfBounds )
				world3d.OnBodyOutOfBounds?.Invoke( Owner );

			if ( fellAsleep )
				world3d.OnBodyFellAsleep?.Invoke( Owner );
		}
	}

	/// <summary>
	/// When the physics world is run at a fixed timestep, getting the positions of bodies will not be smooth.
	/// You can use this function to get the lerped position between steps, to make things super awesome.
	/// </summary>
	public Transform GetLerpedTransform( double time )
	{
		if ( stepTime == 0 )
			return Transform;

		// lerp gap is too big
		if ( stepTime - prevStepTime > 0.5d )
			return Transform;

		time -= World.CurrentDelta;

		var delta = time.Remap( prevStepTime, stepTime );
		return Transform.Lerp( prevStepTransform, stepTransform, (float)delta, true );
	}

	/// <summary>
	/// Move body to this position in a way that cooperates with the physics system. This is quite
	/// good for things like grabbing and moving objects.
	/// </summary>
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
	public void SmoothMove( in Transform transform, float smoothTime, float timeDelta )
	{
		SmoothMove( transform.Position, smoothTime, timeDelta );
		SmoothRotate( transform.Rotation, smoothTime, timeDelta );
	}

	/// <summary>
	/// Rotate the body to this position in a way that cooperates with the physics system.
	/// </summary>
	public void SmoothRotate( in Rotation rotation, float smoothTime, float timeDelta )
	{
		var angVelocity = AngularVelocity;
		Rotation.SmoothDamp( Rotation, rotation, ref angVelocity, smoothTime, timeDelta );
		AngularVelocity = angVelocity;
	}

	protected void Dirty()
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

	internal virtual void ResetProxy() { }

	/// <summary>
	/// Enable enhanced continuous collision detection (CCD) for this body.
	/// When enabled, the body performs CCD against dynamic bodies
	/// (but not against other bodies with enhanced CCD enabled).
	/// This is useful for fast-moving objects like bullets or rockets
	/// that need reliable collision detection.
	/// </summary>
	public virtual bool EnhancedCcd { set { } }
}
