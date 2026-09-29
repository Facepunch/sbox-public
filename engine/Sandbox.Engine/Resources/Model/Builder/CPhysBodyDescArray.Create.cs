using Sandbox;

/// <summary>
/// Builds the native body description array that both models and standalone physics
/// resources are compiled from.
/// </summary>
internal unsafe readonly partial struct CPhysBodyDescArray
{
	/// <summary>
	/// Populate a native body array from managed builders. The caller owns the result
	/// and must call <see cref="DeleteThis"/> on it.
	/// </summary>
	internal static CPhysBodyDescArray Create( List<PhysicsBodyBuilder> bodies, List<PhysicsJointBuilder> joints = null, List<int> surfaces = null )
	{
		if ( bodies is null || bodies.Count == 0 )
			return default;

		var result = Create( bodies.Count, joints?.Count ?? 0 );

		try
		{
			Populate( result, bodies, joints, surfaces );
			return result;
		}
		catch
		{
			result.DeleteThis();
			throw;
		}
	}

	private static void Populate( CPhysBodyDescArray bodies, List<PhysicsBodyBuilder> sources, List<PhysicsJointBuilder> joints, List<int> surfaces )
	{
		for ( var index = 0; index < sources.Count; ++index )
		{
			var source = sources[index];
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

		if ( surfaces is not null )
		{
			foreach ( var surface in surfaces )
			{
				bodies.AddSurface( surface );
			}
		}

		if ( joints is null )
			return;

		for ( var index = 0; index < joints.Count; ++index )
		{
			var source = joints[index].Desc;
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
