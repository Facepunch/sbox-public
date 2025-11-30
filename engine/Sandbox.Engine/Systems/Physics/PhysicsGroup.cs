using NativeEngine;

namespace Sandbox
{
	/// <summary>
	/// Represents a set of <see cref="PhysicsBody">PhysicsBody</see> objects. Think ragdoll.
	/// </summary>
	[Expose, ActionGraphIgnore]
	public sealed class PhysicsGroup : IHandle
	{
		#region IHandle
		//
		// A pointer to the actual native object
		//
		internal IPhysAggregateInstance native;

		//
		// IHandle implementation
		//
		internal int HandleValue { get; set; }
		void IHandle.HandleInit( IntPtr ptr )
		{
			native = ptr;
		}
		void IHandle.HandleDestroy()
		{
			native = IntPtr.Zero;
		}
		bool IHandle.HandleValid() => !native.IsNull;
		#endregion

		internal PhysicsGroup() { }
		internal PhysicsGroup( HandleCreationData _ ) { }

		/// <summary>
		/// The world in which this group belongs
		/// </summary>
		[ActionGraphInclude]
		public PhysicsWorld World => native.GetWorld();

		/// <summary>
		/// Returns position of the first physics body of this group, or zero vector if it has none.
		/// </summary>
		/// <remarks>
		/// TODO: How useful is this in its current form? Should it be removed, or at least renamed to Position?
		/// </remarks>
		public Vector3 Pos
		{
			get => native.GetOrigin();
		}

		/// <summary>
		/// Returns the center of mass for this group of physics bodies.
		/// </summary>
		[ActionGraphInclude]
		public Vector3 MassCenter
		{
			get => native.GetMassCenter();
		}

		/// <summary>
		/// Adds given amount of velocity (<see cref="PhysicsBody.ApplyForce"/>) to all physics bodies in this group.
		/// </summary>
		/// <param name="vel">How much linear force to add?</param>
		[ActionGraphInclude]
		public void AddVelocity( Vector3 vel )
		{
			native.AddVelocity( vel );
		}

		/// <summary>
		/// Adds given amount of angular velocity to all physics bodies in this group.
		/// </summary>
		/// <param name="vel">How much angular force to add?</param>
		[ActionGraphInclude]
		public void AddAngularVelocity( Vector3 vel )
		{
			native.AddAngularVelocity( vel );
		}

		/// <summary>
		/// Adds given amount of linear impulse (<see cref="PhysicsBody.ApplyImpulse"/>) to all physics bodies in this group.
		/// </summary>
		/// <param name="vel">Velocity to apply.</param>
		/// <param name="withMass">Whether to multiply the velocity by mass of the <see cref="PhysicsBody"/> on a per-body basis.</param>
		[ActionGraphInclude]
		public void ApplyImpulse( Vector3 vel, bool withMass = false )
		{
			for ( int i = 0; i < BodyCount; ++i )
			{
				var body = GetBody( i );
				if ( !body.IsValid() )
					continue;

				if ( withMass )
				{
					body.ApplyImpulse( vel * body.Mass );
				}
				else
				{
					body.ApplyImpulse( vel );
				}
			}
		}


		/// <summary>
		/// Adds given amount of angular linear impulse (<see cref="PhysicsBody.ApplyAngularImpulse"/>) to all physics bodies in this group.
		/// </summary>
		/// <param name="vel">Angular velocity to apply.</param>
		/// <param name="withMass">Whether to multiply the velocity by mass of the <see cref="PhysicsBody"/> on a per-body basis.</param>
		[ActionGraphInclude]
		public void ApplyAngularImpulse( Vector3 vel, bool withMass = false )
		{
			for ( int i = 0; i < BodyCount; ++i )
			{
				var body = GetBody( i );
				if ( !body.IsValid() )
					continue;

				if ( withMass )
				{
					body.ApplyAngularImpulse( vel * body.Mass );
				}
				else
				{
					body.ApplyAngularImpulse( vel );
				}
			}
		}

		/// <summary>
		/// Sets <see cref="PhysicsBody.Velocity"/> on all bodies of this group.
		/// </summary>
		[ActionGraphInclude]
		public Vector3 Velocity
		{
			set => native.SetVelocity( value );
		}

		/// <summary>
		/// Sets <see cref="PhysicsBody.AngularVelocity"/> on all bodies of this group.
		/// </summary>
		[ActionGraphInclude]
		public Vector3 AngularVelocity
		{
			set => native.SetAngularVelocity( value );
		}

		/// <summary>
		/// Physics bodies automatically go to sleep after a certain amount of time of inactivity to save on performance.
		/// You can use this to wake the body up, or prematurely send it to sleep.
		/// </summary>
		[ActionGraphInclude]
		public bool Sleeping
		{
			get => native.IsAsleep();

			set
			{
				if ( value ) native.PutToSleep();
				else native.WakeUp();
			}
		}

		/// <summary>
		/// Calls <see cref="PhysicsBody.RebuildMass()"/> on all bodies of this group.
		/// </summary>
		public void RebuildMass()
		{
			for ( int i = 0; i < BodyCount; ++i )
			{
				var body = GetBody( i );
				if ( !body.IsValid() )
					continue;

				body.RebuildMass();
			}
		}

		/// <summary>
		/// The total mass of all the <b>dynamic</b> <see cref="PhysicsBody">PhysicsBodies</see> in this group.
		/// When setting the total mass, it will be set on each body proportionally to each of their old masses,
		/// i.e. if a body had 25% of previous total mass, it will have 25% of new total mass.
		/// </summary>
		[ActionGraphInclude]
		public float Mass
		{
			get => native.GetTotalMass();
			set => native.SetTotalMass( value );
		}

		/// <summary>
		/// Sets <see cref="PhysicsBody.LinearDamping"/> on all bodies in this group.
		/// </summary>
		[ActionGraphInclude]
		public float LinearDamping
		{
			set => native.SetLinearDamping( value );
		}

		/// <summary>
		/// Sets <see cref="PhysicsBody.AngularDamping"/> on all bodies in this group.
		/// </summary>
		[ActionGraphInclude]
		public float AngularDamping
		{
			set => native.SetAngularDamping( value );
		}

		/// <summary>
		/// Returns all physics bodies that belong to this physics group.
		/// </summary>
		[ActionGraphInclude]
		public IEnumerable<PhysicsBody> Bodies
		{
			get
			{
				var bodyCount = BodyCount;
				for ( int i = 0; i < bodyCount; ++i )
					yield return GetBody( i );
			}
		}

		/// <summary>
		/// Returns amount of physics bodies that belong to this physics group.
		/// </summary>
		[ActionGraphInclude]
		public int BodyCount => native.GetBodyCount();

		/// <summary>
		/// Gets a <see cref="PhysicsBody"/> at given index within this physics group. See <see cref="BodyCount"/>.
		/// </summary>
		/// <param name="groupIndex">Index for the body to look up, in range from 0 to <see cref="BodyCount"/>.</param>
		[ActionGraphInclude, Pure]
		public PhysicsBody GetBody( int groupIndex ) => native.GetBodyHandle( groupIndex ); // Throw on OOB

		/// <summary>
		/// Returns amount of joints in this group.
		/// </summary>
		public int JointCount => native.GetJointCount();

		/// <summary>
		/// Get a joint by index without allocating memory.
		/// </summary>
		public PhysicsJoint GetJoint( int index ) => native.GetJointHandle( index );

		/// <summary>
		/// Returns a <see cref="PhysicsBody"/> by its <see cref="PhysicsBody.GroupName"/> within this group.
		/// </summary>
		/// <param name="groupName">Name of the physics body to look up.</param>
		/// <returns>The physics body, or null if body with given name is not found.</returns>
		[ActionGraphInclude, Pure]
		public PhysicsBody GetBody( string groupName ) => native.FindBodyByName( groupName );

		/// <summary>
		/// Any and all joints that are attached to any body in this group.
		/// WARNING: Creates Garbage. Use JointCount and GetJoint(i) for zero-allocation loops.
		/// </summary>
		[ActionGraphInclude]
		public IEnumerable<PhysicsJoint> Joints
		{
			get
			{
				var jointCount = native.GetJointCount();
				for ( int i = 0; i < jointCount; ++i )
					yield return native.GetJointHandle( i );
			}
		}

		internal void RemoveJoint( PhysicsJoint joint )
		{
			if ( joint.IsValid() )
			{
				native.RemoveJoint( joint );
			}
		}

		/// <summary>
		/// Sets the physical properties of each <see cref="PhysicsShape">PhysicsShape</see> of this group.
		/// </summary>
		[ActionGraphInclude]
		public void SetSurface( string name )
		{
			native.SetSurfaceProperties( name );
		}

		/// <summary>
		/// Delete this group, and all of its bodies
		/// </summary>
		public void Remove()
		{
			var physicsWorld = native.GetWorld();
			if ( physicsWorld is null ) return;

			physicsWorld.native.DestroyAggregateInstance( this );
		}
	}
}
