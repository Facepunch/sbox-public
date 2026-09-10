using System;

namespace SeriousEngine3;

public partial class ModelLoader
{
	const float Scale = 40f;

	static bool AddMechanismCollision( ModelBuilder builder, MetaValue mechanism, List<BoneInfo> bones, Transform[] worldTransforms, bool isSkinned )
	{
		if ( mechanism == null ) return false;

		var templates = mechanism["1"]; // CDynamicContainer<CMechanismTemplate>
		if ( templates.Count == 0 ) return false;

		if ( isSkinned )
		{
			var ragdoll = FindRagdollTemplate( templates, bones );
			if ( ragdoll != null )
			{
				BuildRagdoll( builder, ragdoll, bones, worldTransforms );
				return true;
			}
		}

		var primary = FindNamedTemplate( templates, "Default" ) ?? templates[0];
		return AddSingleBody( builder, primary["3"] );
	}

	static MetaValue FindNamedTemplate( MetaValue templates, string name )
	{
		for ( int i = 0; i < templates.Count; i++ )
			if ( templates[i]["1"].AsIdentName() == name ) return templates[i];
		return null;
	}

	static MetaValue FindRagdollTemplate( MetaValue templates, List<BoneInfo> bones )
	{
		var named = FindNamedTemplate( templates, "Ragdoll" );
		if ( named != null && PartTreeHasBone( named["3"], bones ) ) return named;

		for ( int i = 0; i < templates.Count; i++ )
			if ( PartTreeHasBone( templates[i]["3"], bones ) ) return templates[i];

		return null;
	}

	static bool PartTreeHasBone( MetaValue part, List<BoneInfo> bones )
	{
		var boneName = part["1"].AsIdentName();
		if ( !string.IsNullOrEmpty( boneName ) && bones.Exists( b => b.Name == boneName ) )
			return true;

		var children = part["6"];
		for ( int ci = 0; ci < children.Count; ci++ )
			if ( PartTreeHasBone( children[ci], bones ) ) return true;

		return false;
	}

	static bool AddSingleBody( ModelBuilder builder, MetaValue rootPart )
	{
		PhysicsBodyBuilder body = null;
		AddPartShapes( builder, ref body, rootPart, Rotation.Identity, Vector3.Zero );
		return body != null;
	}

	static void AddPartShapes( ModelBuilder builder, ref PhysicsBodyBuilder body, MetaValue part, Rotation accRot, Vector3 accPos )
	{
		var partTransform = part["2"]; // QuatVect (SE3 space, relative to parent part)
		var partRot = partTransform["1"].AsQuaternion();
		var partPos = partTransform["2"].AsVector3();

		var worldRot = accRot * partRot;
		var worldPos = accRot * partPos + accPos;

		var shapes = part["5"]; // CStaticArray<CMechanismShapeTemplate>
		for ( int si = 0; si < shapes.Count; si++ )
		{
			body ??= builder.AddBody();
			AddShape( body, shapes[si], worldRot, worldPos );
		}

		var children = part["6"]; // CStaticArray<CMechanismPartTemplate>
		for ( int ci = 0; ci < children.Count; ci++ )
			AddPartShapes( builder, ref body, children[ci], worldRot, worldPos );
	}

	static void BuildRagdoll( ModelBuilder builder, MetaValue template, List<BoneInfo> bones, Transform[] worldTransforms )
	{
		var rootPart = template["3"];

		var bodyIndices = new Dictionary<string, int>();
		int nextBodyIdx = 0;

		AddPartBodies( builder, rootPart, bones, worldTransforms, bodyIndices, ref nextBodyIdx );
		AddPartJoints( builder, rootPart, bones, worldTransforms, bodyIndices, parentBoneName: null );
	}

	static void AddPartBodies( ModelBuilder builder, MetaValue part, List<BoneInfo> bones, Transform[] worldTransforms, Dictionary<string, int> bodyIndices, ref int nextBodyIdx )
	{
		var boneName = part["1"].AsIdentName();
		int boneIdx = string.IsNullOrEmpty( boneName ) ? -1 : bones.FindIndex( b => b.Name == boneName );

		if ( boneIdx >= 0 )
		{
			var shapes = part["5"]; // CStaticArray<CMechanismShapeTemplate>
			if ( shapes.Count > 0 )
			{
				var body = builder.AddBody( boneName: boneName );

				for ( int si = 0; si < shapes.Count; si++ )
				{
					AddShape( body, shapes[si], Rotation.Identity, Vector3.Zero );
				}

				bodyIndices[boneName] = nextBodyIdx++;
			}
		}

		// Recurse into children
		var children = part["6"]; // CStaticArray<CMechanismPartTemplate>
		for ( int ci = 0; ci < children.Count; ci++ )
		{
			AddPartBodies( builder, children[ci], bones, worldTransforms, bodyIndices, ref nextBodyIdx );
		}
	}

	static void AddShape( PhysicsBodyBuilder body, MetaValue shapeTemplate, Rotation partRot, Vector3 partPos )
	{
		var hullPtr = shapeTemplate["4"].Deref(); // *CHullTemplate (or CPrimitiveHullTemplate)
		if ( hullPtr == null ) return;

		var hullBase = hullPtr.Base ?? hullPtr;
		var hullTransform = hullBase["1"]; // QuatVect (SE3 space, relative to part)
		var hullPos = hullTransform["2"].AsVector3();
		var hullRot = hullTransform["1"].AsQuaternion();

		var rot = partRot * hullRot;
		var pos = partRot * hullPos + partPos;

		var primitiveDesc = hullPtr.MemberCount > 0 ? hullPtr.Member( 0 ) : null; // CPrimitiveDesc (null for CDummyHullTemplate)
		if ( primitiveDesc == null ) return;

		try
		{
			int primitiveType = primitiveDesc["1"].AsEnum();
			float h1 = primitiveDesc["2"].AsFloat() * 0.5f;
			float h2 = primitiveDesc["3"].AsFloat() * 0.5f;
			float h3 = primitiveDesc["4"].AsFloat() * 0.5f;

			switch ( primitiveType )
			{
				case 0: // Sphere
					body.AddSphere( new Sphere( ToScene( pos ), h1 * Scale ) );
					break;
				case 1: // Box
					var corners = new Vector3[8];
					int idx = 0;
					for ( int x = -1; x <= 1; x += 2 )
						for ( int y = -1; y <= 1; y += 2 )
							for ( int z = -1; z <= 1; z += 2 )
								corners[idx++] = ToScene( pos + rot * new Vector3( x * h1, y * h2, z * h3 ) );
					body.AddHull( corners );
					break;
				case 2: // Capsule (axis along local Y)
					{
						float radius = h1;
						float halfHeight = Math.Max( h2 - radius, 0f );
						var axis = rot * new Vector3( 0, 1, 0 );
						body.AddCapsule( new Capsule( ToScene( pos - axis * halfHeight ), ToScene( pos + axis * halfHeight ), radius * Scale ) );
						break;
					}
				case 3: // Cylinder (round, axis along local Y)
					{
						const int segments = 12;
						var points = new Vector3[segments * 2];
						for ( int i = 0; i < segments; i++ )
						{
							float a = MathF.PI * 2f * i / segments;
							float x = MathF.Cos( a ) * h1;
							float z = MathF.Sin( a ) * h1;
							points[i * 2] = ToScene( pos + rot * new Vector3( x, h2, z ) );
							points[i * 2 + 1] = ToScene( pos + rot * new Vector3( x, -h2, z ) );
						}
						body.AddHull( points );
						break;
					}
			}
		}
		catch ( Exception ex )
		{
			Log.Warning( $"SE3 collision shape failed to build: {ex.Message}" );
		}
	}

	static Vector3 ToScene( Vector3 se3 ) => ConvertPosition( se3 ) * Scale;

	static void AddPartJoints( ModelBuilder builder, MetaValue part, List<BoneInfo> bones, Transform[] worldTransforms, Dictionary<string, int> bodyIndices, string parentBoneName )
	{
		var boneName = part["1"].AsIdentName();

		// Use this part's bone as parent for children, or pass through the parent if this part has no bone
		string effectiveParent = !string.IsNullOrEmpty( boneName ) && bodyIndices.ContainsKey( boneName )
			? boneName
			: parentBoneName;

		// Add joint between this part and its parent
		if ( !string.IsNullOrEmpty( boneName ) && parentBoneName != null &&
			 bodyIndices.TryGetValue( boneName, out var childIdx ) &&
			 bodyIndices.TryGetValue( parentBoneName, out var parentIdx ) )
		{
			int boneIdx = bones.FindIndex( b => b.Name == boneName );
			int parentBoneIdx = bones.FindIndex( b => b.Name == parentBoneName );

			if ( boneIdx >= 0 && parentBoneIdx >= 0 )
			{
				try
				{
					var anchor = worldTransforms[boneIdx].Position;
					var pose1 = worldTransforms[parentBoneIdx];
					var pose2 = worldTransforms[boneIdx];

					var jointTemplate = part["4"].Deref();
					var jointBase = jointTemplate.Base ?? jointTemplate;
					var jointTransform = jointBase["1"];
					var jointRot = ConvertRotation( jointTransform["1"].AsQuaternion() );

					var worldJoint = new Transform( anchor, jointRot );
					var frame1 = pose1.ToLocal( worldJoint );
					var frame2 = pose2.ToLocal( worldJoint );

					AddJoint( builder, jointTemplate, parentIdx, childIdx, frame1, frame2 );
				}
				catch ( Exception ex )
				{
					Log.Warning( $"joint {parentBoneName}->{boneName} failed: {ex.Message}" );
				}
			}
		}

		// Recurse into children
		var children = part["6"];
		for ( int ci = 0; ci < children.Count; ci++ )
		{
			AddPartJoints( builder, children[ci], bones, worldTransforms, bodyIndices, effectiveParent );
		}
	}

	static void AddJoint( ModelBuilder builder, MetaValue jointTemplate, int bodyA, int bodyB, Transform frame1, Transform frame2 )
	{
		var typeName = jointTemplate.Type.Name;

		if ( typeName == "CHingeJointTemplate" )
		{
			float lo = -jointTemplate.Member( 1 ).AsFloat();
			float hi = -jointTemplate.Member( 0 ).AsFloat();
			builder.AddHingeJoint( bodyA, bodyB, frame1, frame2 )
				.WithTwistLimit( lo, hi );
		}
		else if ( typeName == "CConeJointTemplate" )
		{
			float swing1 = jointTemplate.Member( 0 ).AsFloat();
			float swing2 = jointTemplate.Member( 1 ).AsFloat();
			float twistLo = jointTemplate.Member( 2 ).AsFloat();
			float twistHi = jointTemplate.Member( 3 ).AsFloat();

			float swing = Math.Max( swing1, swing2 );
			if ( twistLo > twistHi ) { float tmp = twistLo; twistLo = twistHi; twistHi = tmp; }

			builder.AddBallJoint( bodyA, bodyB, frame1, frame2 )
				.WithSwingLimit( swing )
				.WithTwistLimit( twistLo, twistHi );
		}
		else
		{
			builder.AddFixedJoint( bodyA, bodyB, frame1, frame2 );
		}
	}

	static void AddCollision( ModelBuilder builder, MetaValue collisionMesh, List<Vector3> renderVerts )
	{
		var idxArray = collisionMesh["13"]; // CStaticArray<UWORD> - indices into render mesh verts
		if ( idxArray.Count < 3 ) return;

		var indices = new int[idxArray.Count];
		for ( int i = 0; i < idxArray.Count; i++ )
			indices[i] = idxArray[i].AsUShort();

		// If member 12 has vertices, use those; otherwise use render mesh verts
		var vertArray = collisionMesh["12"];
		Vector3[] vertices;
		if ( vertArray.Count > 0 )
		{
			vertices = new Vector3[vertArray.Count];
			for ( int i = 0; i < vertArray.Count; i++ )
				vertices[i] = ToScene( vertArray[i].AsVector3() );
		}
		else
		{
			vertices = renderVerts.ToArray();
		}

		if ( vertices.Length == 0 || indices.Length < 3 )
			return;

		int maxIndex = 0;
		for ( int i = 0; i < indices.Length; i++ )
			if ( indices[i] > maxIndex ) maxIndex = indices[i];

		if ( maxIndex >= vertices.Length )
			return;

		builder.AddCollisionMesh( vertices, indices );
	}

	static Vector3 ConvertPosition( Vector3 v ) => new( -v.z, -v.x, v.y );
	static Rotation ConvertRotation( Rotation q ) => new( -q.z, -q.x, q.y, q.w );
}
