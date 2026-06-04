
partial struct CPhysBodyDescArray
{
	/// <summary>
	/// Builds a native CPhysBodyDescArray from managed body/joint builders.
	/// Caller must call DeleteThis() when done.
	/// </summary>
	internal static unsafe CPhysBodyDescArray Create( List<PhysicsBodyBuilder> bodies, List<PhysicsJointBuilder> joints = null )
	{
		if ( bodies.Count == 0 )
			return default;

		int jointCount = joints?.Count ?? 0;
		var arr = Create( bodies.Count, jointCount );

		int index = 0;
		foreach ( var body in bodies )
		{
			var desc = arr.Get( index++ );

			foreach ( var hull in body.Hulls )
			{
				var simplify = hull.Simplify ?? new PhysicsBodyBuilder.HullSimplify
				{
					Method = PhysicsBodyBuilder.SimplifyMethod.None
				};

				fixed ( Vector3* pPoints = &hull.Points[0] )
				{
					desc.AddHull(
						(IntPtr)pPoints,
						hull.Points.Length,
						hull.Transform,
						simplify.AngleTolerance,
						simplify.DistanceTolerance,
						simplify.MaxFaces, simplify.MaxEdges, simplify.MaxVerts,
						(int)simplify.Method
					);
				}
			}

			foreach ( var shape in body.Spheres )
			{
				desc.AddSphere( shape.Sphere );
			}

			foreach ( var shape in body.Capsules )
			{
				desc.AddCapsule( shape.Capsule );
			}

			foreach ( var mesh in body.Meshes )
			{
				fixed ( Vector3* pVertices = mesh.Vertices )
				fixed ( uint* pIndices = mesh.Indices )
				fixed ( byte* pMaterials = mesh.Materials )
				{
					desc.AddMesh(
						(IntPtr)pVertices, (uint)mesh.Vertices.Length,
						(IntPtr)pIndices, (uint)mesh.Indices.Length,
						(IntPtr)pMaterials
					);
				}
			}

			desc.m_flMass = body.Mass;
			desc.SetBoneName( body.BoneName );
			desc.SetBindPose( body.BindPose );

			if ( body.Surface is not null )
			{
				desc.SetSurface( new StringToken( body.Surface.NameHash ) );
			}
		}

		if ( joints is not null )
		{
			index = 0;
			foreach ( var joint in joints )
			{
				var dest = arr.GetJoint( index++ );
				var jd = joint.Desc;
				dest.m_nType = (ushort)jd.Type;
				dest.m_nBody1 = (ushort)jd.Body1;
				dest.m_nBody2 = (ushort)jd.Body2;
				dest.m_nFlags = jd.Flags;
				dest.m_bEnableCollision = jd.EnableCollision;
				dest.m_bEnableLinearLimit = jd.EnableLinearLimit;
				dest.m_bEnableLinearMotor = jd.EnableLinearMotor;
				dest.m_vLinearTargetVelocity = jd.LinearTargetVelocity;
				dest.m_flMaxForce = jd.MaxForce;
				dest.m_bEnableSwingLimit = jd.EnableSwingLimit;
				dest.m_bEnableTwistLimit = jd.EnableTwistLimit;
				dest.m_bEnableAngularMotor = jd.EnableAngularMotor;
				dest.m_vAngularTargetVelocity = jd.AngularTargetVelocity;
				dest.m_flMaxTorque = jd.MaxTorque;
				dest.m_flLinearFrequency = jd.LinearFrequency;
				dest.m_flLinearDampingRatio = jd.LinearDamping;
				dest.m_flAngularFrequency = jd.AngularFrequency;
				dest.m_flAngularDampingRatio = jd.AngularDamping;
				dest.m_flLinearStrength = jd.LinearStrength;
				dest.m_flAngularStrength = jd.AngularStrength;
				dest.m_Frame1 = jd.Frame1;
				dest.m_Frame2 = jd.Frame2;
				dest.SetLinearLimitMin( jd.LinearLimit.x );
				dest.SetLinearLimitMax( jd.LinearLimit.y );
				dest.SetSwingLimitMin( jd.SwingLimit.x.DegreeToRadian() );
				dest.SetSwingLimitMax( jd.SwingLimit.y.DegreeToRadian() );
				dest.SetTwistLimitMin( jd.TwistLimit.x.DegreeToRadian() );
				dest.SetTwistLimitMax( jd.TwistLimit.y.DegreeToRadian() );
			}
		}

		return arr;
	}
}
