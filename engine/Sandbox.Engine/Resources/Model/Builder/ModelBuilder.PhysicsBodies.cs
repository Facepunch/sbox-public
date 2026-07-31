namespace Sandbox;

public sealed partial class ModelBuilder
{
	private readonly List<PhysicsBodyBuilder> _bodies = [];

	/// <summary>
	/// Adds a physics body to the model.
	/// Joints refer to bodies by the order they are added.
	/// </summary>
	/// <param name="mass">The mass in kilograms. A value of 0 calculates mass from the body's shapes and surface density.</param>
	/// <param name="surface">The surface properties applied to the body.</param>
	/// <param name="boneName">
	/// Optional name of the bone this body is attached to.
	/// Leave empty for non-skeletal bodies.
	/// </param>
	/// <returns>A builder for the new body.</returns>
	public PhysicsBodyBuilder AddBody( float mass = default, Surface surface = default, string boneName = default )
	{
		var builder = new PhysicsBodyBuilder
		{
			Mass = mass,
			Surface = surface,
			BoneName = boneName,
			BindPose = Transform.Zero
		};

		_bodies.Add( builder );
		return builder;
	}

	private unsafe CPhysBodyDescArray CreatePhysicsBodies()
	{
		if ( _bodies.Count == 0 )
			return CreateLegacyPhysicsBodies();

		var bodies = CPhysBodyDescArray.Create( _bodies.Count, _joints.Count );
		try
		{
			PopulatePhysicsBodies( bodies );
			return bodies;
		}
		catch
		{
			bodies.DeleteThis();
			throw;
		}
	}

	private unsafe void PopulatePhysicsBodies( CPhysBodyDescArray bodies )
	{
		for ( var index = 0; index < _bodies.Count; ++index )
		{
			var source = _bodies[index];
			var destination = bodies.Get( index );

			foreach ( var box in source.Boxes )
			{
				destination.AddBox( box.Extents, box.Transform );
			}

			foreach ( var hull in source.Hulls )
			{
				var simplify = hull.Simplify ?? new PhysicsBodyBuilder.HullSimplify
				{
					Method = PhysicsBodyBuilder.SimplifyMethod.None
				};

				fixed ( Vector3* pointPointer = hull.Points )
				{
					destination.AddHull(
						(IntPtr)pointPointer,
						hull.Points.Length,
						hull.Transform,
						simplify.AngleTolerance,
						simplify.DistanceTolerance,
						simplify.MaxFaces,
						simplify.MaxEdges,
						simplify.MaxVerts,
						(int)simplify.Method );
				}
			}

			foreach ( var sphere in source.Spheres )
			{
				destination.AddSphere( sphere.Sphere );
			}

			foreach ( var capsule in source.Capsules )
			{
				destination.AddCapsule( capsule.Capsule );
			}

			foreach ( var mesh in source.Meshes )
			{
				fixed ( Vector3* vertexPointer = mesh.Vertices )
				fixed ( uint* indexPointer = mesh.Indices )
				fixed ( byte* materialPointer = mesh.Materials )
				{
					destination.AddMesh(
						(IntPtr)vertexPointer,
						(uint)mesh.Vertices.Length,
						(IntPtr)indexPointer,
						(uint)mesh.Indices.Length,
						(IntPtr)materialPointer );
				}
			}

			destination.m_flMass = source.Mass;
			destination.SetBoneName( source.BoneName );
			destination.SetBindPose( source.BindPose );

			if ( source.Surface is not null )
				destination.SetSurface( new StringToken( source.Surface.NameHash ) );
		}

		foreach ( var surface in _surfaces )
		{
			bodies.AddSurface( surface );
		}

		for ( var index = 0; index < _joints.Count; ++index )
		{
			var source = _joints[index].Desc;
			var destination = bodies.GetJoint( index );
			destination.m_nType = (ushort)source.Type;
			destination.m_nBody1 = (ushort)source.Body1;
			destination.m_nBody2 = (ushort)source.Body2;
			destination.m_nFlags = source.Flags;
			destination.m_bEnableCollision = source.EnableCollision;
			destination.m_bEnableLinearLimit = source.EnableLinearLimit;
			destination.m_bEnableLinearMotor = source.EnableLinearMotor;
			destination.m_vLinearTargetVelocity = source.LinearTargetVelocity;
			destination.m_flMaxForce = source.MaxForce;
			destination.m_bEnableSwingLimit = source.EnableSwingLimit;
			destination.m_bEnableTwistLimit = source.EnableTwistLimit;
			destination.m_bEnableAngularMotor = source.EnableAngularMotor;
			destination.m_vAngularTargetVelocity = source.AngularTargetVelocity;
			destination.m_flMaxTorque = source.MaxTorque;
			destination.m_flLinearFrequency = source.LinearFrequency;
			destination.m_flLinearDampingRatio = source.LinearDamping;
			destination.m_flAngularFrequency = source.AngularFrequency;
			destination.m_flAngularDampingRatio = source.AngularDamping;
			destination.m_flLinearStrength = source.LinearStrength;
			destination.m_flAngularStrength = source.AngularStrength;
			destination.m_Frame1 = source.Frame1;
			destination.m_Frame2 = source.Frame2;
			destination.SetLinearLimitMin( source.LinearLimit.x );
			destination.SetLinearLimitMax( source.LinearLimit.y );
			destination.SetSwingLimitMin( source.SwingLimit.x.DegreeToRadian() );
			destination.SetSwingLimitMax( source.SwingLimit.y.DegreeToRadian() );
			destination.SetTwistLimitMin( source.TwistLimit.x.DegreeToRadian() );
			destination.SetTwistLimitMax( source.TwistLimit.y.DegreeToRadian() );
		}
	}
}
