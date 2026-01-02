using System;
using System.Runtime.InteropServices;
using System.Text;
using Sandbox;
using ValvePak;
using ValveKeyValue;

internal class HL2Model : ResourceLoader<HL2Mount>
{
	private readonly VpkArchive _package;
	private readonly VpkEntry _entry;
	private readonly string _filePath;

	public HL2Model( VpkArchive package, VpkEntry entry )
	{
		_package = package;
		_entry = entry;
	}

	public HL2Model( string filePath )
	{
		_filePath = filePath;
	}

	protected override object Load()
	{
		byte[] mdlData;
		byte[] vvdData = null;
		byte[] vtxData = null;
		byte[] aniData = null;
		byte[] phyData = null;

		if ( _package != null )
		{
			_package.ReadEntry( _entry, out mdlData );
			var basePath = _entry.GetFullPath()[..^4];

			var vvdEntry = _package.FindEntry( basePath + ".vvd" );
			if ( vvdEntry != null )
				_package.ReadEntry( vvdEntry, out vvdData );

			var vtxEntry = _package.FindEntry( basePath + ".dx90.vtx" )
				?? _package.FindEntry( basePath + ".dx80.vtx" )
				?? _package.FindEntry( basePath + ".sw.vtx" );
			if ( vtxEntry != null )
				_package.ReadEntry( vtxEntry, out vtxData );

			var aniEntry = _package.FindEntry( basePath + ".ani" );
			if ( aniEntry != null )
				_package.ReadEntry( aniEntry, out aniData );

			var phyEntry = _package.FindEntry( basePath + ".phy" );
			if ( phyEntry != null )
				_package.ReadEntry( phyEntry, out phyData );
		}
		else
		{
			mdlData = File.ReadAllBytes( _filePath );
			var basePath = _filePath[..^4];

			if ( File.Exists( basePath + ".vvd" ) )
				vvdData = File.ReadAllBytes( basePath + ".vvd" );

			var vtxPath = basePath + ".dx90.vtx";
			if ( !File.Exists( vtxPath ) )
				vtxPath = basePath + ".dx80.vtx";
			if ( !File.Exists( vtxPath ) )
				vtxPath = basePath + ".sw.vtx";
			if ( File.Exists( vtxPath ) )
				vtxData = File.ReadAllBytes( vtxPath );

			if ( File.Exists( basePath + ".ani" ) )
				aniData = File.ReadAllBytes( basePath + ".ani" );

			if ( File.Exists( basePath + ".phy" ) )
				phyData = File.ReadAllBytes( basePath + ".phy" );
		}

		return MdlLoader.Load( mdlData, vvdData, vtxData, aniData, phyData, Path, Host );
	}
}

internal static class MdlLoader
{
	private const int IDST = 0x54534449;
	private const int IDSV = 0x56534449;

	public static Model Load( byte[] mdlData, byte[] vvdData, byte[] vtxData, byte[] aniData, byte[] phyData, string path, HL2Mount mount )
	{
		if ( mdlData == null || mdlData.Length < StudioHeader.Size )
			return Model.Error;

		var mdl = new StudioHeader( mdlData, aniData );
		if ( mdl.Id != IDST || mdl.Version < 44 || mdl.Version > 49 )
			return Model.Error;

		if ( vvdData == null || vtxData == null )
			return Model.Error;

		var vvd = new VvdFileHeader( vvdData );
		if ( vvd.Id != IDSV || vvd.Version != 4 )
			return Model.Error;

		var vtx = new VtxFileHeader( vtxData );
		if ( vtx.Version != 7 )
			return Model.Error;

		if ( mdl.Checksum != vvd.Checksum || mdl.Checksum != vtx.Checksum )
			return Model.Error;

		var vertices = vvd.GetVertices( mdl.RootLod );
		var tangents = vvd.GetTangents( mdl.RootLod );

		return BuildModel( mdl, vtx, vertices, tangents, phyData, path, mount );
	}

	private static Model BuildModel( StudioHeader mdl, VtxFileHeader vtx, StudioVertex[] vertices, Vector4[] tangents, byte[] phyData, string path, HL2Mount mount )
	{
		var builder = Model.Builder.WithName( path );

		var allPositions = new List<Vector3>();
		var meshInfos = new List<(Mesh mesh, string bodyPartName, int modelIndex)>();

		var boneNames = new string[mdl.BoneCount];
		var boneTransforms = new Transform[mdl.BoneCount];
		for ( int i = 0; i < mdl.BoneCount; i++ )
		{
			var bone = mdl.GetBone( i );
			boneNames[i] = bone.Name;

			var localTransform = new Transform( bone.Position, bone.Rotation );
			boneTransforms[i] = bone.Parent >= 0 ? boneTransforms[bone.Parent].ToWorld( localTransform ) : localTransform;

			var parentName = bone.Parent >= 0 ? boneNames[bone.Parent] : null;
			builder.AddBone( bone.Name, boneTransforms[i].Position, boneTransforms[i].Rotation, parentName );
		}

		for ( int bodyPartIdx = 0; bodyPartIdx < mdl.BodyPartCount; bodyPartIdx++ )
		{
			var mdlBodyPart = mdl.GetBodyPart( bodyPartIdx );
			var vtxBodyPart = vtx.GetBodyPart( bodyPartIdx );
			var bodyPartName = mdlBodyPart.Name;

			for ( int modelIdx = 0; modelIdx < mdlBodyPart.ModelCount; modelIdx++ )
			{
				var mdlModel = mdlBodyPart.GetModel( modelIdx );
				var vtxModel = vtxBodyPart.GetModel( modelIdx );

				if ( mdlModel.MeshCount == 0 )
					continue;

				var eyeballsByTexture = new Dictionary<int, List<int>>();
				for ( int eyeIdx = 0; eyeIdx < mdlModel.EyeballCount; eyeIdx++ )
				{
					var eyeball = mdlModel.GetEyeball( eyeIdx );
					if ( !eyeballsByTexture.TryGetValue( eyeball.Texture, out var list ) )
					{
						list = [];
						eyeballsByTexture[eyeball.Texture] = list;
					}
					list.Add( eyeIdx );
				}

				var vtxLod = vtxModel.GetLod( mdl.RootLod );
				int vertexOffset = mdlModel.VertexIndex / 48;

				for ( int meshIdx = 0; meshIdx < mdlModel.MeshCount; meshIdx++ )
				{
					var mdlMesh = mdlModel.GetMesh( meshIdx );
					var vtxMesh = vtxLod.GetMesh( meshIdx );

					var material = LoadMaterial( mdl, mdlMesh.Material, mount );
					if ( material.IsValid() && eyeballsByTexture.TryGetValue( mdlMesh.Material, out var eyeballList ) )
					{
						var eyeball = mdlModel.GetEyeball( eyeballList[0] );
						material = CreateEyeMaterial( material, eyeball );
					}

					var meshVertices = new List<Vertex>();
					var meshIndices = new List<int>();
					var vertexMap = new Dictionary<int, int>();

					for ( int groupIdx = 0; groupIdx < vtxMesh.StripGroupCount; groupIdx++ )
					{
						var stripGroup = vtxMesh.GetStripGroup( groupIdx );

						for ( int stripIdx = 0; stripIdx < stripGroup.StripCount; stripIdx++ )
						{
							var strip = stripGroup.GetStrip( stripIdx );

							if ( (strip.Flags & 0x01) != 0 )
							{
								for ( int i = 0; i < strip.IndexCount; i += 3 )
								{
									AddTriangle( stripGroup, strip, i,
										mdlMesh.VertexOffset, vertexOffset,
										vertices, tangents,
										meshVertices, meshIndices, vertexMap );
								}
							}
							else if ( (strip.Flags & 0x02) != 0 )
							{
								for ( int i = 0; i < strip.IndexCount - 2; i++ )
								{
									if ( i % 2 == 0 )
										AddTriangleStrip( stripGroup, strip, i, 0, 1, 2,
											mdlMesh.VertexOffset, vertexOffset,
											vertices, tangents,
											meshVertices, meshIndices, vertexMap );
									else
										AddTriangleStrip( stripGroup, strip, i, 1, 0, 2,
											mdlMesh.VertexOffset, vertexOffset,
											vertices, tangents,
											meshVertices, meshIndices, vertexMap );
								}
							}
						}
					}

					if ( meshVertices.Count == 0 )
						continue;

					foreach ( var v in meshVertices )
						allPositions.Add( v.Position );

					var mesh = new Mesh( material );
					mesh.CreateVertexBuffer( meshVertices.Count, meshVertices );
					mesh.CreateIndexBuffer( meshIndices.Count, meshIndices );

					meshInfos.Add( (mesh, bodyPartName, modelIdx) );
				}
			}
		}

		var bounds = BBox.FromPoints( allPositions );

		foreach ( var (mesh, bodyPartName, modelIndex) in meshInfos )
		{
			mesh.Bounds = bounds;
			builder.AddMesh( mesh, 0, bodyPartName, modelIndex );
		}

		LoadPhysics( builder, phyData, boneNames, boneTransforms );
		LoadAnimations( mdl, builder, boneNames, mount );

		return builder.Create();
	}

	private static void LoadPhysics( ModelBuilder builder, byte[] phyData, string[] boneNames, Transform[] boneTransforms )
	{
		if ( phyData == null || phyData.Length < 16 )
			return;

		int headerSize = BitConverter.ToInt32( phyData, 0 );
		int solidCount = BitConverter.ToInt32( phyData, 8 );

		if ( headerSize != 16 || solidCount <= 0 || solidCount > 128 )
			return;

		var boneNameToIndex = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		for ( int i = 0; i < boneNames.Length; i++ )
			boneNameToIndex[boneNames[i]] = i;

		var solidHulls = new List<List<List<Vector3>>>();
		int offset = 16;

		for ( int solidIdx = 0; solidIdx < solidCount && offset + 4 <= phyData.Length; solidIdx++ )
		{
			int solidSize = BitConverter.ToInt32( phyData, offset );
			offset += 4;

			if ( solidSize <= 0 || offset + solidSize > phyData.Length )
				break;

			var convexHulls = ParseSolidCollision( phyData, offset, solidSize );
			solidHulls.Add( convexHulls );

			offset += solidSize;
		}

		var (solidInfos, constraints) = ParsePhyKeyValues( phyData, offset );

		var solidToBodyIndex = new Dictionary<int, int>();
		int bodyIndex = 0;

		for ( int i = 0; i < solidHulls.Count; i++ )
		{
			var convexHulls = solidHulls[i];
			var info = i < solidInfos.Count ? solidInfos[i] : new PhySolidInfo { Mass = 1.0f };

			if ( convexHulls == null || convexHulls.Count == 0 )
				continue;

			var validHulls = new List<List<Vector3>>();
			foreach ( var hull in convexHulls )
			{
				if ( hull == null || hull.Count < 4 )
					continue;

				var bounds = new BBox( hull[0], hull[0] );
				foreach ( var v in hull )
					bounds = bounds.AddPoint( v );

				var sz = bounds.Size;
				if ( sz.x < 0.01f && sz.y < 0.01f && sz.z < 0.01f )
					continue;

				validHulls.Add( hull );
			}

			if ( validHulls.Count == 0 )
				continue;

			Surface surface = null;
			if ( !string.IsNullOrEmpty( info.SurfaceProp ) )
				surface = Surface.FindByName( info.SurfaceProp );

			var body = builder.AddBody( info.Mass, surface, info.BoneName );

			foreach ( var hull in validHulls )
			{
				body.AddHull( CollectionsMarshal.AsSpan( hull ), Transform.Zero, new PhysicsBodyBuilder.HullSimplify
				{
					Method = PhysicsBodyBuilder.SimplifyMethod.QEM
				} );
			}

			solidToBodyIndex[i] = bodyIndex;
			bodyIndex++;
		}

		var solidToBoneIndex = new Dictionary<int, int>();
		for ( int i = 0; i < solidInfos.Count; i++ )
		{
			var info = solidInfos[i];
			if ( !string.IsNullOrEmpty( info.BoneName ) && boneNameToIndex.TryGetValue( info.BoneName, out int boneIdx ) )
				solidToBoneIndex[i] = boneIdx;
		}

		foreach ( var constraint in constraints )
		{
			if ( !solidToBodyIndex.TryGetValue( constraint.Parent, out int parentBodyIdx ) )
				continue;
			if ( !solidToBodyIndex.TryGetValue( constraint.Child, out int childBodyIdx ) )
				continue;
			if ( parentBodyIdx == childBodyIdx )
				continue;

			Transform frame1 = Transform.Zero;
			Transform frame2 = Transform.Zero;

			if ( solidToBoneIndex.TryGetValue( constraint.Parent, out int parentBoneIdx ) &&
				 solidToBoneIndex.TryGetValue( constraint.Child, out int childBoneIdx ) )
			{
				var parentBoneWorld = boneTransforms[parentBoneIdx];
				var childBoneWorld = boneTransforms[childBoneIdx];
				var childPosInParent = parentBoneWorld.PointToLocal( childBoneWorld.Position );
				var childRotInParent = parentBoneWorld.RotationToLocal( childBoneWorld.Rotation );

				frame1 = new Transform( childPosInParent, childRotInParent );
				frame2 = Transform.Zero;
			}

			float twistMin = constraint.XMin;
			float twistMax = constraint.XMax;

			float twistRange = Math.Abs( twistMax - twistMin );
			float swingYRange = Math.Abs( constraint.YMax - constraint.YMin );
			float swingZRange = Math.Abs( constraint.ZMax - constraint.ZMin );

			const float DofThreshold = 5f;
			int dofCount = 0;
			int dofMask = 0;
			if ( twistRange > DofThreshold ) { dofCount++; dofMask |= 1; }
			if ( swingYRange > DofThreshold ) { dofCount++; dofMask |= 2; }
			if ( swingZRange > DofThreshold ) { dofCount++; dofMask |= 4; }

			if ( dofCount == 0 )
			{
				builder.AddFixedJoint( parentBodyIdx, childBodyIdx, frame1, frame2 );
			}
			else if ( dofCount == 1 )
			{
				float hingeMin, hingeMax;
				if ( (dofMask & 1) != 0 )
				{
					hingeMin = twistMin;
					hingeMax = twistMax;
				}
				else if ( (dofMask & 2) != 0 )
				{
					hingeMin = constraint.YMin;
					hingeMax = constraint.YMax;
				}
				else
				{
					hingeMin = constraint.ZMin;
					hingeMax = constraint.ZMax;
				}

				builder.AddHingeJoint( parentBodyIdx, childBodyIdx, frame1, frame2 )
					.WithTwistLimit( hingeMin, hingeMax );
			}
			else
			{
				float swingY = Math.Max( Math.Abs( constraint.YMin ), Math.Abs( constraint.YMax ) );
				float swingZ = Math.Max( Math.Abs( constraint.ZMin ), Math.Abs( constraint.ZMax ) );
				float swingLimit = Math.Max( swingY, swingZ );

				builder.AddBallJoint( parentBodyIdx, childBodyIdx, frame1, frame2 )
					.WithSwingLimit( swingLimit )
					.WithTwistLimit( twistMin, twistMax );
			}
		}
	}

	private struct PhySolidInfo
	{
		public int Index;
		public string BoneName;
		public string ParentBoneName;
		public float Mass;
		public string SurfaceProp;
	}

	private struct PhyConstraintInfo
	{
		public int Parent;
		public int Child;
		public float XMin, XMax;
		public float YMin, YMax;
		public float ZMin, ZMax;
		public float XFriction, YFriction, ZFriction;
	}

	private static (List<PhySolidInfo> solids, List<PhyConstraintInfo> constraints) ParsePhyKeyValues( byte[] data, int keyValuesOffset )
	{
		var solids = new List<PhySolidInfo>();
		var constraints = new List<PhyConstraintInfo>();

		if ( keyValuesOffset >= data.Length )
			return (solids, constraints);

		string text = Encoding.ASCII.GetString( data, keyValuesOffset, data.Length - keyValuesOffset );
		var kv = KeyValues.Parse( "phyroot { " + text + " }" );
		if ( kv == null )
			return (solids, constraints);

		foreach ( var child in kv.Children )
		{
			if ( child.Name.Equals( "solid", StringComparison.OrdinalIgnoreCase ) )
			{
				solids.Add( new PhySolidInfo
				{
					Index = child.GetInt( "index" ),
					BoneName = child.GetString( "name" ),
					ParentBoneName = child.GetString( "parent" ),
					Mass = child.GetFloat( "mass", 1.0f ),
					SurfaceProp = child.GetString( "surfaceprop" )
				} );
			}
			else if ( child.Name.Equals( "ragdollconstraint", StringComparison.OrdinalIgnoreCase ) )
			{
				constraints.Add( new PhyConstraintInfo
				{
					Parent = child.GetInt( "parent", -1 ),
					Child = child.GetInt( "child", -1 ),
					XMin = child.GetFloat( "xmin" ),
					XMax = child.GetFloat( "xmax" ),
					YMin = child.GetFloat( "ymin" ),
					YMax = child.GetFloat( "ymax" ),
					ZMin = child.GetFloat( "zmin" ),
					ZMax = child.GetFloat( "zmax" ),
					XFriction = child.GetFloat( "xfriction" ),
					YFriction = child.GetFloat( "yfriction" ),
					ZFriction = child.GetFloat( "zfriction" )
				} );
			}
		}

		return (solids, constraints);
	}

	private const int VPHY_ID = 0x59485056; // 'VPHY'
	private const int IVPS_ID = 0x53505649; // 'IVPS'
	private const int SPVI_ID = 0x49565053; // 'SPVI'

	private static List<List<Vector3>> ParseSolidCollision( byte[] data, int offset, int size )
	{
		if ( size < 8 )
			return null;

		int magic = BitConverter.ToInt32( data, offset );
		if ( magic == VPHY_ID )
		{
			// VPHY format: collideheader_t(8) + compactsurfaceheader_t(20) + IVP_Compact_Surface(48+)
			short modelType = BitConverter.ToInt16( data, offset + 6 );
			if ( modelType != 0 ) // modelType 0 = convex hull
				return null;

			const int CollideHeaderSize = 8;
			const int SurfaceHeaderSize = 20;

			if ( size < CollideHeaderSize + SurfaceHeaderSize + 48 )
				return null;

			int compactSurfaceOffset = offset + CollideHeaderSize + SurfaceHeaderSize;
			return ParseCompactSurface( data, compactSurfaceOffset, size - CollideHeaderSize - SurfaceHeaderSize );
		}
		else
		{
			// Legacy format: raw IVP_Compact_Surface
			if ( size < 48 )
				return null;

			int legacyId = BitConverter.ToInt32( data, offset + 44 );
			return legacyId == 0 || legacyId == IVPS_ID || legacyId == SPVI_ID
				? ParseCompactSurface( data, offset, size )
				: null;
		}
	}

	private static List<List<Vector3>> ParseCompactSurface( byte[] data, int offset, int size )
	{
		// IVP_Compact_Surface: offset_ledgetree_root at byte 32
		const int CompactSurfaceSize = 48;
		if ( size < CompactSurfaceSize )
			return null;

		int ledgetreeOffset = BitConverter.ToInt32( data, offset + 32 );
		if ( ledgetreeOffset <= 0 || ledgetreeOffset >= size )
			return null;

		int nodeOffset = offset + ledgetreeOffset;

		var allLedges = new List<(int offset, int triangleCount)>();
		CollectLedges( data, nodeOffset, allLedges );

		var result = new List<List<Vector3>>();
		foreach ( var (ledgeOffset, _) in allLedges )
		{
			var vertices = ParseCompactLedge( data, ledgeOffset );
			if ( vertices != null && vertices.Count >= 4 )
				result.Add( vertices );
		}

		return result.Count > 0 ? result : null;
	}

	private static void CollectLedges( byte[] data, int nodeOffset, List<(int offset, int triangleCount)> ledges )
	{
		// IVP_Compact_Ledgetree_Node (28 bytes):
		// offset_right_node(4) + offset_compact_ledge(4) + center(12) + radius(4) + box_sizes(3) + free_0(1)
		const int NodeSize = 28;

		var nodeStack = new Stack<int>();
		nodeStack.Push( nodeOffset );

		while ( nodeStack.Count > 0 )
		{
			int currentOffset = nodeStack.Pop();

			if ( currentOffset < 0 || currentOffset + NodeSize > data.Length )
				continue;

			int offsetRightNode = BitConverter.ToInt32( data, currentOffset );
			int offsetCompactLedge = BitConverter.ToInt32( data, currentOffset + 4 );

			if ( offsetCompactLedge != 0 )
			{
				int ledgeOffset = currentOffset + offsetCompactLedge;
				if ( ledgeOffset >= 0 && ledgeOffset + 16 <= data.Length )
				{
					short numTriangles = BitConverter.ToInt16( data, ledgeOffset + 12 );
					if ( numTriangles > 0 )
						ledges.Add( (ledgeOffset, numTriangles) );
				}
			}

			if ( offsetRightNode != 0 )
			{
				// Right child is at offset_right_node from current node
				int rightOffset = currentOffset + offsetRightNode;
				if ( rightOffset >= 0 && rightOffset + NodeSize <= data.Length )
					nodeStack.Push( rightOffset );

				// Left child is immediately after this node
				int leftOffset = currentOffset + NodeSize;
				if ( leftOffset >= 0 && leftOffset + NodeSize <= data.Length )
					nodeStack.Push( leftOffset );
			}
		}
	}

	private static List<Vector3> ParseCompactLedge( byte[] data, int offset )
	{
		// IVP_Compact_Ledge (16 bytes): c_point_offset(4) + client_data(4) + flags:size_div_16(4) + n_triangles(2) + reserved(2)
		if ( offset + 16 > data.Length )
			return null;

		int pointOffset = BitConverter.ToInt32( data, offset );
		short numTriangles = BitConverter.ToInt16( data, offset + 12 );

		if ( numTriangles <= 0 || pointOffset == 0 )
			return null;

		int pointArrayOffset = offset + pointOffset;
		if ( pointArrayOffset < 0 || pointArrayOffset >= data.Length )
			return null;

		int trianglesOffset = offset + 16;

		// IVP_Compact_Triangle (16 bytes): indices(4) + c_three_edges[3](12)
		// IVP_Compact_Edge (4 bytes): start_point_index:16 + opposite_index:15 + is_virtual:1
		const int TriangleSize = 16;

		if ( trianglesOffset + numTriangles * TriangleSize > data.Length )
			return null;

		var vertexSet = new HashSet<int>();
		var vertices = new List<Vector3>();

		const float MetersToInches = 39.3701f;

		for ( int i = 0; i < numTriangles; i++ )
		{
			int triOffset = trianglesOffset + i * TriangleSize;

			for ( int j = 0; j < 3; j++ )
			{
				int edgeOffset = triOffset + 4 + j * 4;
				if ( edgeOffset + 4 > data.Length )
					continue;

				uint edgeData = BitConverter.ToUInt32( data, edgeOffset );
				int pointIndex = (int)(edgeData & 0xFFFF);

				if ( !vertexSet.Add( pointIndex ) )
					continue;

				// IVP_Compact_Poly_Point (16 bytes): x,y,z floats + hesse_val
				int ptOffset = pointArrayOffset + pointIndex * 16;
				if ( ptOffset + 12 > data.Length || ptOffset < 0 )
					continue;

				float ivpX = BitConverter.ToSingle( data, ptOffset );
				float ivpY = BitConverter.ToSingle( data, ptOffset + 4 );
				float ivpZ = BitConverter.ToSingle( data, ptOffset + 8 );

				// IVP to Source: (X, Z, -Y) * MetersToInches
				vertices.Add( new Vector3( ivpX * MetersToInches, ivpZ * MetersToInches, -ivpY * MetersToInches ) );
			}
		}

		return vertices.Count > 0 ? vertices : null;
	}

	private static void LoadAnimations( StudioHeader mdl, ModelBuilder builder, string[] boneNames, HL2Mount mount )
	{
		var mainBoneNameToIndex = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		for ( int i = 0; i < boneNames.Length; i++ )
			mainBoneNameToIndex[boneNames[i]] = i;

		var mainBasePoses = new (Vector3 pos, Rotation rot)[boneNames.Length];
		for ( int i = 0; i < boneNames.Length; i++ )
		{
			var bone = mdl.GetBone( i );
			mainBasePoses[i] = (bone.Position, bone.Rotation);
		}

		LoadModelAnimations( mdl, builder, boneNames.Length, null, mainBasePoses );
		LoadIncludeModelAnimations( mdl, builder, boneNames.Length, mainBoneNameToIndex, mainBasePoses, mount );
	}

	private static void LoadModelAnimations( StudioHeader mdl, ModelBuilder builder, int mainBoneCount, int[] boneMapping, (Vector3 pos, Rotation rot)[] mainBasePoses )
	{
		// Sequences reference animations and contain the actual name/flags. Animations are just raw bone data
		var aniData = mdl.GetAniData();
		bool hasAniFile = !aniData.IsEmpty;
		var mdlData = mdl.GetData();

		for ( int seqIdx = 0; seqIdx < mdl.LocalSeqCount; seqIdx++ )
		{
			var seqDesc = mdl.GetSeqDesc( seqIdx );
			var seqName = seqDesc.Label;

			int animIndex = seqDesc.GetAnimIndex( 0, 0 );
			if ( animIndex < 0 || animIndex >= mdl.LocalAnimCount )
				continue;

			var animDesc = mdl.GetAnimDesc( animIndex );

			if ( animDesc.NumFrames <= 0 )
				continue;

			var anim = builder.AddAnimation( seqName, animDesc.Fps )
				.WithLooping( seqDesc.Looping )
				.WithDelta( seqDesc.Delta );

			LoadAnimationFrames( mdl, animDesc, mdlData, hasAniFile ? aniData : ReadOnlySpan<byte>.Empty, anim, mainBoneCount, boneMapping, mainBasePoses );
		}
	}

	private static void LoadAnimationFrames( StudioHeader mdl, StudioAnimDesc animDesc, ReadOnlySpan<byte> mdlData, ReadOnlySpan<byte> aniData, AnimationBuilder anim, int mainBoneCount, int[] boneMapping, (Vector3 pos, Rotation rot)[] mainBasePoses )
	{
		int numFrames = animDesc.NumFrames;
		bool isDelta = (animDesc.Flags & 0x0004) != 0; // STUDIO_DELTA
		bool hasSections = animDesc.SectionFrames != 0;
		bool hasAniFile = !aniData.IsEmpty;

		int localBoneCount = mdl.BoneCount;
		var basePoses = new (Vector3 pos, Rotation rot, Vector3 euler, Vector3 posScale, Vector3 rotScale)[localBoneCount];
		for ( int i = 0; i < localBoneCount; i++ )
		{
			var bone = mdl.GetBone( i );
			basePoses[i] = (bone.Position, bone.Rotation, bone.EulerRotation, bone.PosScale, bone.RotScale);
		}

		for ( int frame = 0; frame < numFrames; frame++ )
		{
			ReadOnlySpan<byte> animData;
			int animDataOffset;
			int sectionRelativeFrame = frame;

			if ( hasSections )
			{
				var (block, index) = animDesc.GetAnimBlockForFrame( frame );
				sectionRelativeFrame = animDesc.GetSectionRelativeFrame( frame );

				if ( block == 0 )
				{
					animData = mdlData;
					animDataOffset = animDesc.AnimDataOffset;
				}
				else if ( hasAniFile )
				{
					int blockStart = mdl.GetAnimBlockDataStart( block );
					if ( blockStart < 0 )
						continue;
					animData = aniData;
					animDataOffset = blockStart + index;
				}
				else
				{
					continue;
				}
			}
			else
			{
				if ( animDesc.AnimBlock == 0 )
				{
					animData = mdlData;
					animDataOffset = animDesc.AnimDataOffset;
				}
				else if ( hasAniFile )
				{
					int blockStart = mdl.GetAnimBlockDataStart( animDesc.AnimBlock );
					if ( blockStart < 0 )
						continue;
					animData = aniData;
					animDataOffset = blockStart + animDesc.AnimIndex;
				}
				else
				{
					continue;
				}
			}

			if ( animDataOffset < 0 || animDataOffset >= animData.Length - 4 )
				continue;

			var boneAnims = new List<(int localBone, byte flags, int dataOffset)>();
			int offset = animDataOffset;
			while ( offset >= 0 && offset < animData.Length - 4 )
			{
				int bone = animData[offset];
				byte flags = animData[offset + 1];
				short nextOffset = BitConverter.ToInt16( animData.Slice( offset + 2, 2 ) );

				boneAnims.Add( (bone, flags, offset + 4) );

				if ( nextOffset == 0 ) break;
				offset += nextOffset;
			}

			var transforms = new Transform[mainBoneCount];

			for ( int i = 0; i < mainBoneCount; i++ )
			{
				transforms[i] = isDelta ? new Transform( Vector3.Zero, Rotation.Identity ) : new Transform( mainBasePoses[i].pos, mainBasePoses[i].rot );
			}

			foreach ( var (localBoneIndex, flags, dataOffset) in boneAnims )
			{
				int mainBoneIndex;
				if ( boneMapping != null )
				{
					if ( localBoneIndex < 0 || localBoneIndex >= boneMapping.Length )
						continue;
					mainBoneIndex = boneMapping[localBoneIndex];
					if ( mainBoneIndex < 0 )
						continue;
				}
				else
				{
					mainBoneIndex = localBoneIndex;
					if ( mainBoneIndex < 0 || mainBoneIndex >= mainBoneCount )
						continue;
				}

				if ( localBoneIndex < 0 || localBoneIndex >= localBoneCount )
					continue;

				var (basePos, baseRot, baseEuler, posScale, rotScale) = basePoses[localBoneIndex];

				Vector3 pos;
				Rotation rot;

				if ( (flags & StudioAnimFlags.RawRot) != 0 )
				{
					rot = StudioAnimReader.DecodeQuaternion48( animData, dataOffset );
				}
				else if ( (flags & StudioAnimFlags.RawRot2) != 0 )
				{
					rot = StudioAnimReader.DecodeQuaternion64( animData, dataOffset );
				}
				else if ( (flags & StudioAnimFlags.AnimRot) != 0 )
				{
					int rotDataOffset = dataOffset;
					var euler = ExtractCompressedEuler( animData, rotDataOffset, sectionRelativeFrame, rotScale );
					if ( !isDelta )
						euler += baseEuler;
					rot = EulerToQuaternion( euler );
				}
				else
				{
					rot = isDelta ? Rotation.Identity : baseRot;
				}

				int posDataOffset = dataOffset;
				if ( (flags & StudioAnimFlags.RawRot) != 0 )
					posDataOffset += 6;
				else if ( (flags & StudioAnimFlags.RawRot2) != 0 )
					posDataOffset += 8;
				else if ( (flags & StudioAnimFlags.AnimRot) != 0 )
					posDataOffset += 6;

				if ( (flags & StudioAnimFlags.RawPos) != 0 )
				{
					pos = StudioAnimReader.DecodeVector48( animData, posDataOffset );
				}
				else if ( (flags & StudioAnimFlags.AnimPos) != 0 )
				{
					pos = ExtractCompressedPosition( animData, posDataOffset, sectionRelativeFrame, posScale );
					if ( !isDelta )
						pos += basePos;
				}
				else
				{
					pos = isDelta ? Vector3.Zero : basePos;
				}

				transforms[mainBoneIndex] = new Transform( pos, rot );
			}

			anim.AddFrame( transforms );
		}
	}

	private static Vector3 ExtractCompressedEuler( ReadOnlySpan<byte> data, int valuePtr, int frame, Vector3 scale )
	{
		short offX = BitConverter.ToInt16( data.Slice( valuePtr, 2 ) );
		short offY = BitConverter.ToInt16( data.Slice( valuePtr + 2, 2 ) );
		short offZ = BitConverter.ToInt16( data.Slice( valuePtr + 4, 2 ) );

		float x = offX > 0 ? ExtractAnimValue( data, valuePtr + offX, frame ) * scale.x : 0;
		float y = offY > 0 ? ExtractAnimValue( data, valuePtr + offY, frame ) * scale.y : 0;
		float z = offZ > 0 ? ExtractAnimValue( data, valuePtr + offZ, frame ) * scale.z : 0;

		return new Vector3( x, y, z );
	}

	private static Vector3 ExtractCompressedPosition( ReadOnlySpan<byte> data, int valuePtr, int frame, Vector3 scale )
	{
		short offX = BitConverter.ToInt16( data.Slice( valuePtr, 2 ) );
		short offY = BitConverter.ToInt16( data.Slice( valuePtr + 2, 2 ) );
		short offZ = BitConverter.ToInt16( data.Slice( valuePtr + 4, 2 ) );

		float x = offX > 0 ? ExtractAnimValue( data, valuePtr + offX, frame ) * scale.x : 0;
		float y = offY > 0 ? ExtractAnimValue( data, valuePtr + offY, frame ) * scale.y : 0;
		float z = offZ > 0 ? ExtractAnimValue( data, valuePtr + offZ, frame ) * scale.z : 0;

		return new Vector3( x, y, z );
	}

	private static float ExtractAnimValue( ReadOnlySpan<byte> data, int offset, int frame )
	{
		if ( offset < 0 || offset >= data.Length - 2 )
			return 0;

		int k = frame;

		while ( true )
		{
			byte valid = data[offset];
			byte total = data[offset + 1];

			if ( total == 0 )
				return 0;

			if ( k < total )
			{
				if ( k < valid )
				{
					int valueOffset = offset + 2 + k * 2;
					return valueOffset + 2 > data.Length ? 0 : BitConverter.ToInt16( data.Slice( valueOffset, 2 ) );
				}
				else
				{
					int valueOffset = offset + 2 + (valid - 1) * 2;
					return valueOffset + 2 > data.Length ? 0 : BitConverter.ToInt16( data.Slice( valueOffset, 2 ) );
				}
			}

			k -= total;
			offset += 2 + valid * 2;

			if ( offset >= data.Length - 2 )
				return 0;
		}
	}

	private static Rotation EulerToQuaternion( Vector3 euler )
	{
		float sr, cr, sp, cp, sy, cy;

		sr = MathF.Sin( euler.x * 0.5f );
		cr = MathF.Cos( euler.x * 0.5f );
		sp = MathF.Sin( euler.y * 0.5f );
		cp = MathF.Cos( euler.y * 0.5f );
		sy = MathF.Sin( euler.z * 0.5f );
		cy = MathF.Cos( euler.z * 0.5f );

		float srXcp = sr * cp;
		float crXsp = cr * sp;
		float crXcp = cr * cp;
		float srXsp = sr * sp;

		return new Rotation(
			srXcp * cy - crXsp * sy,
			crXsp * cy + srXcp * sy,
			crXcp * sy - srXsp * cy,
			crXcp * cy + srXsp * sy
		);
	}

	private static void LoadIncludeModelAnimations( StudioHeader mainMdl, ModelBuilder builder, int mainBoneCount, Dictionary<string, int> mainBoneNameToIndex, (Vector3 pos, Rotation rot)[] mainBasePoses, HL2Mount mount )
	{
		if ( mount == null )
			return;

		for ( int i = 0; i < mainMdl.IncludeModelCount; i++ )
		{
			string includePath = mainMdl.GetIncludeModelPath( i );
			if ( string.IsNullOrEmpty( includePath ) )
				continue;

			byte[] includeMdlData = mount.ReadFile( includePath );
			if ( includeMdlData == null || includeMdlData.Length < StudioHeader.Size )
				continue;

			string aniPath = includePath.Replace( ".mdl", ".ani" );
			byte[] includeAniData = mount.ReadFile( aniPath );

			var includeMdl = new StudioHeader( includeMdlData, includeAniData );
			if ( includeMdl.Id != IDST || includeMdl.Version < 44 || includeMdl.Version > 49 )
				continue;

			int includeBoneCount = includeMdl.BoneCount;
			int[] boneMapping = new int[includeBoneCount];
			for ( int b = 0; b < includeBoneCount; b++ )
			{
				string boneName = includeMdl.GetBone( b ).Name;
				boneMapping[b] = mainBoneNameToIndex.TryGetValue( boneName, out int mainIndex ) ? mainIndex : -1;
			}

			LoadModelAnimations( includeMdl, builder, mainBoneCount, boneMapping, mainBasePoses );
		}
	}

	private static void AddTriangle(
		VtxStripGroupHeader stripGroup, VtxStripHeader strip, int baseIndex,
		int meshVertexOffset, int modelVertexOffset,
		StudioVertex[] vertices, Vector4[] tangents,
		List<Vertex> outVertices, List<int> outIndices, Dictionary<int, int> vertexMap )
	{
		int idx0 = strip.IndexOffset + baseIndex;
		int idx1 = strip.IndexOffset + baseIndex + 1;
		int idx2 = strip.IndexOffset + baseIndex + 2;

		int vi0 = stripGroup.GetIndex( idx0 );
		int vi1 = stripGroup.GetIndex( idx1 );
		int vi2 = stripGroup.GetIndex( idx2 );

		var v0 = stripGroup.GetVertex( vi0 );
		var v1 = stripGroup.GetVertex( vi1 );
		var v2 = stripGroup.GetVertex( vi2 );

		int global0 = modelVertexOffset + meshVertexOffset + v0.OrigMeshVertId;
		int global1 = modelVertexOffset + meshVertexOffset + v1.OrigMeshVertId;
		int global2 = modelVertexOffset + meshVertexOffset + v2.OrigMeshVertId;

		outIndices.Add( GetOrAddVertex( outVertices, vertexMap, vertices, tangents, global0 ) );
		outIndices.Add( GetOrAddVertex( outVertices, vertexMap, vertices, tangents, global2 ) );
		outIndices.Add( GetOrAddVertex( outVertices, vertexMap, vertices, tangents, global1 ) );
	}

	private static void AddTriangleStrip(
		VtxStripGroupHeader stripGroup, VtxStripHeader strip, int baseIndex, int a, int b, int c,
		int meshVertexOffset, int modelVertexOffset,
		StudioVertex[] vertices, Vector4[] tangents,
		List<Vertex> outVertices, List<int> outIndices, Dictionary<int, int> vertexMap )
	{
		int idx0 = strip.IndexOffset + baseIndex + a;
		int idx1 = strip.IndexOffset + baseIndex + b;
		int idx2 = strip.IndexOffset + baseIndex + c;

		int vi0 = stripGroup.GetIndex( idx0 );
		int vi1 = stripGroup.GetIndex( idx1 );
		int vi2 = stripGroup.GetIndex( idx2 );

		var v0 = stripGroup.GetVertex( vi0 );
		var v1 = stripGroup.GetVertex( vi1 );
		var v2 = stripGroup.GetVertex( vi2 );

		int global0 = modelVertexOffset + meshVertexOffset + v0.OrigMeshVertId;
		int global1 = modelVertexOffset + meshVertexOffset + v1.OrigMeshVertId;
		int global2 = modelVertexOffset + meshVertexOffset + v2.OrigMeshVertId;

		if ( global0 == global1 || global1 == global2 || global0 == global2 )
			return;

		outIndices.Add( GetOrAddVertex( outVertices, vertexMap, vertices, tangents, global0 ) );
		outIndices.Add( GetOrAddVertex( outVertices, vertexMap, vertices, tangents, global2 ) );
		outIndices.Add( GetOrAddVertex( outVertices, vertexMap, vertices, tangents, global1 ) );
	}

	private static int GetOrAddVertex(
		List<Vertex> outVertices, Dictionary<int, int> map,
		StudioVertex[] srcVerts, Vector4[] srcTangents, int globalIndex )
	{
		if ( map.TryGetValue( globalIndex, out int existing ) )
			return existing;

		var v = srcVerts[globalIndex];
		var t = srcTangents[globalIndex];
		var weights = NormalizeWeights( v.BoneWeights[0], v.BoneWeights[1], v.BoneWeights[2] );

		int idx = outVertices.Count;
		outVertices.Add( new Vertex
		{
			Position = v.Position,
			Normal = v.Normal,
			Tangent = new Vector3( t.x, t.y, t.z ),
			TexCoord = v.TexCoord,
			BlendIndices = new Color32( v.BoneIds[0], v.BoneIds[1], v.BoneIds[2], 0 ),
			BlendWeights = weights
		} );

		map[globalIndex] = idx;
		return idx;
	}

	private static Color32 NormalizeWeights( float w0, float w1, float w2 )
	{
		var iw0 = (int)(w0 * 255 + 0.5f);
		var iw1 = (int)(w1 * 255 + 0.5f);
		var iw2 = (int)(w2 * 255 + 0.5f);

		var diff = 255 - (iw0 + iw1 + iw2);
		if ( diff != 0 )
		{
			if ( iw0 >= iw1 && iw0 >= iw2 ) iw0 += diff;
			else if ( iw1 >= iw2 ) iw1 += diff;
			else iw2 += diff;
		}

		return new Color32( (byte)iw0, (byte)iw1, (byte)iw2, 0 );
	}

	private static Material CreateEyeMaterial( Material material, StudioEyeball eyeball )
	{
		var origin = eyeball.Origin;
		var forward = eyeball.Forward.Normal;
		var up = eyeball.Up.Normal;
		var right = Vector3.Cross( forward, up ).Normal;

		float irisRadius = eyeball.Radius * eyeball.IrisScale;
		float scale = 0.5f / irisRadius;

		var irisU = new Vector4( right.x * scale, right.y * scale, right.z * scale, 0.5f - Vector3.Dot( right, origin ) * scale );
		var irisV = new Vector4( up.x * scale, up.y * scale, up.z * scale, 0.5f - Vector3.Dot( up, origin ) * scale );

		material.Set( "g_vIrisU", irisU );
		material.Set( "g_vIrisV", irisV );

		return material;
	}

	private static Material LoadMaterial( StudioHeader mdl, int materialIndex, HL2Mount mount )
	{
		var textureName = mdl.GetTexture( materialIndex ).Name.ToLowerInvariant().Replace( '\\', '/' );
		int cdTextureCount = mdl.CdTextureCount;
		var searchPaths = new string[cdTextureCount];

		for ( int i = 0; i < cdTextureCount; i++ )
		{
			searchPaths[i] = mdl.GetCdTexture( i ).ToLowerInvariant().Replace( '\\', '/' ).TrimEnd( '/' );
		}

		for ( int i = 0; i < searchPaths.Length; i++ )
		{
			var searchPath = searchPaths[i];
			var path = string.IsNullOrEmpty( searchPath ) ? textureName : $"{searchPath}/{textureName}";

			var vmtPath = $"materials/{path}.vmt";
			if ( mount == null || !mount.FileExists( vmtPath ) )
				continue;

			var fullPath = $"mount://hl2/materials/{path}.vmat";
			var material = Material.Load( fullPath );
			if ( material.IsValid() )
				return material;
		}

		return null;
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct Vertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.Tangent] public Vector3 Tangent;
		[VertexLayout.TexCoord] public Vector2 TexCoord;
		[VertexLayout.BlendIndices] public Color32 BlendIndices;
		[VertexLayout.BlendWeight] public Color32 BlendWeights;
	}
}

/// <summary>
/// studiohdr_t - MDL file header (408 bytes)
/// </summary>
internal readonly ref struct StudioHeader( byte[] data, byte[] aniData = null )
{
	public const int Size = 408;
	private readonly ReadOnlySpan<byte> _data = data;
	private readonly ReadOnlySpan<byte> _aniData = aniData ?? ReadOnlySpan<byte>.Empty;

	public ReadOnlySpan<byte> GetData() => _data;
	public ReadOnlySpan<byte> GetAniData() => _aniData;

	public int Id => BitConverter.ToInt32( _data[0..4] );
	public int Version => BitConverter.ToInt32( _data[4..8] );
	public int Checksum => BitConverter.ToInt32( _data[8..12] );

	public Vector3 EyePosition => ReadVector3( 80 );
	public Vector3 IllumPosition => ReadVector3( 92 );
	public Vector3 HullMin => ReadVector3( 104 );
	public Vector3 HullMax => ReadVector3( 116 );
	public Vector3 ViewBBMin => ReadVector3( 128 );
	public Vector3 ViewBBMax => ReadVector3( 140 );
	public int Flags => BitConverter.ToInt32( _data[152..156] );

	public int BoneCount => BitConverter.ToInt32( _data[156..160] );
	public int BoneOffset => BitConverter.ToInt32( _data[160..164] );

	public int BoneControllerCount => BitConverter.ToInt32( _data[164..168] );
	public int BoneControllerOffset => BitConverter.ToInt32( _data[168..172] );

	public int HitboxSetCount => BitConverter.ToInt32( _data[172..176] );
	public int HitboxSetOffset => BitConverter.ToInt32( _data[176..180] );

	public int LocalAnimCount => BitConverter.ToInt32( _data[180..184] );
	public int LocalAnimOffset => BitConverter.ToInt32( _data[184..188] );
	public int LocalSeqCount => BitConverter.ToInt32( _data[188..192] );
	public int LocalSeqOffset => BitConverter.ToInt32( _data[192..196] );

	public int NumAnimBlocks => BitConverter.ToInt32( _data[352..356] );
	public int AnimBlockOffset => BitConverter.ToInt32( _data[356..360] );

	public int TextureCount => BitConverter.ToInt32( _data[204..208] );
	public int TextureOffset => BitConverter.ToInt32( _data[208..212] );
	public int CdTextureCount => BitConverter.ToInt32( _data[212..216] );
	public int CdTextureOffset => BitConverter.ToInt32( _data[216..220] );
	public int SkinRefCount => BitConverter.ToInt32( _data[220..224] );
	public int SkinFamilyCount => BitConverter.ToInt32( _data[224..228] );
	public int SkinOffset => BitConverter.ToInt32( _data[228..232] );
	public int BodyPartCount => BitConverter.ToInt32( _data[232..236] );
	public int BodyPartOffset => BitConverter.ToInt32( _data[236..240] );

	public int IncludeModelCount => BitConverter.ToInt32( _data[336..340] );
	public int IncludeModelOffset => BitConverter.ToInt32( _data[340..344] );

	public byte RootLod => _data[377];

	public StudioBone GetBone( int index )
	{
		int offset = BoneOffset + index * StudioBone.Size;
		return new StudioBone( _data, offset );
	}

	public StudioBodyParts GetBodyPart( int index )
	{
		int offset = BodyPartOffset + index * StudioBodyParts.Size;
		return new StudioBodyParts( _data, offset );
	}

	public StudioTexture GetTexture( int index )
	{
		int offset = TextureOffset + index * StudioTexture.Size;
		return new StudioTexture( _data, offset );
	}

	public string GetCdTexture( int index )
	{
		int ptrOffset = CdTextureOffset + index * 4;
		int stringOffset = BitConverter.ToInt32( _data.Slice( ptrOffset, 4 ) );
		return ReadString( stringOffset );
	}

	public int GetSkinRef( int family, int index )
	{
		if ( family < 0 || family >= SkinFamilyCount || index < 0 || index >= SkinRefCount )
			return index;
		int offset = SkinOffset + (family * SkinRefCount + index) * 2;
		return BitConverter.ToInt16( _data.Slice( offset, 2 ) );
	}

	public StudioAnimDesc GetAnimDesc( int index )
	{
		int offset = LocalAnimOffset + index * StudioAnimDesc.Size;
		return new StudioAnimDesc( _data, offset );
	}

	public StudioSeqDesc GetSeqDesc( int index )
	{
		int offset = LocalSeqOffset + index * StudioSeqDesc.Size;
		return new StudioSeqDesc( _data, offset );
	}

	public string GetIncludeModelPath( int index )
	{
		if ( index < 0 || index >= IncludeModelCount )
			return null;

		int structOffset = IncludeModelOffset + index * 8;
		int nameOffset = structOffset + BitConverter.ToInt32( _data.Slice( structOffset + 4, 4 ) );
		return ReadString( nameOffset );
	}

	public int GetAnimBlockDataStart( int blockIndex )
	{
		if ( blockIndex <= 0 || blockIndex >= NumAnimBlocks || AnimBlockOffset == 0 )
			return -1;

		int blockTableOffset = AnimBlockOffset + blockIndex * 8;
		return BitConverter.ToInt32( _data.Slice( blockTableOffset, 4 ) );
	}

	private Vector3 ReadVector3( int offset ) => new(
		BitConverter.ToSingle( _data.Slice( offset, 4 ) ),
		BitConverter.ToSingle( _data.Slice( offset + 4, 4 ) ),
		BitConverter.ToSingle( _data.Slice( offset + 8, 4 ) )
	);

	private string ReadString( int offset )
	{
		int end = offset;
		while ( end < _data.Length && _data[end] != 0 ) end++;
		return Encoding.ASCII.GetString( _data.Slice( offset, end - offset ) );
	}
}

/// <summary>
/// mstudiobone_t - Bone definition (216 bytes)
/// </summary>
internal readonly ref struct StudioBone
{
	public const int Size = 216;
	private readonly ReadOnlySpan<byte> _data;
	private readonly int _offset;

	public StudioBone( ReadOnlySpan<byte> data, int offset )
	{
		_data = data;
		_offset = offset;
	}

	private int NameOffset => BitConverter.ToInt32( _data.Slice( _offset, 4 ) );
	public int Parent => BitConverter.ToInt32( _data.Slice( _offset + 4, 4 ) );

	public Vector3 Position => new(
		BitConverter.ToSingle( _data.Slice( _offset + 32, 4 ) ),
		BitConverter.ToSingle( _data.Slice( _offset + 36, 4 ) ),
		BitConverter.ToSingle( _data.Slice( _offset + 40, 4 ) )
	);

	public Rotation Rotation => new(
		BitConverter.ToSingle( _data.Slice( _offset + 44, 4 ) ),
		BitConverter.ToSingle( _data.Slice( _offset + 48, 4 ) ),
		BitConverter.ToSingle( _data.Slice( _offset + 52, 4 ) ),
		BitConverter.ToSingle( _data.Slice( _offset + 56, 4 ) )
	);

	public Vector3 EulerRotation => new(
		BitConverter.ToSingle( _data.Slice( _offset + 60, 4 ) ),
		BitConverter.ToSingle( _data.Slice( _offset + 64, 4 ) ),
		BitConverter.ToSingle( _data.Slice( _offset + 68, 4 ) )
	);

	public Vector3 PosScale => new(
		BitConverter.ToSingle( _data.Slice( _offset + 72, 4 ) ),
		BitConverter.ToSingle( _data.Slice( _offset + 76, 4 ) ),
		BitConverter.ToSingle( _data.Slice( _offset + 80, 4 ) )
	);

	public Vector3 RotScale => new(
		BitConverter.ToSingle( _data.Slice( _offset + 84, 4 ) ),
		BitConverter.ToSingle( _data.Slice( _offset + 88, 4 ) ),
		BitConverter.ToSingle( _data.Slice( _offset + 92, 4 ) )
	);

	public int Flags => BitConverter.ToInt32( _data.Slice( _offset + 160, 4 ) );

	public string Name
	{
		get
		{
			int offset = _offset + NameOffset;
			int end = offset;
			while ( end < _data.Length && _data[end] != 0 ) end++;
			return Encoding.ASCII.GetString( _data[offset..end] );
		}
	}
}

/// <summary>
/// mstudiobodyparts_t - Body part group (16 bytes)
/// </summary>
internal readonly ref struct StudioBodyParts
{
	public const int Size = 16;
	private readonly ReadOnlySpan<byte> _data;
	private readonly int _offset;

	public StudioBodyParts( ReadOnlySpan<byte> data, int offset )
	{
		_data = data;
		_offset = offset;
	}

	private int NameOffset => BitConverter.ToInt32( _data.Slice( _offset, 4 ) );
	public int ModelCount => BitConverter.ToInt32( _data.Slice( _offset + 4, 4 ) );
	public int Base => BitConverter.ToInt32( _data.Slice( _offset + 8, 4 ) );
	private int ModelOffset => BitConverter.ToInt32( _data.Slice( _offset + 12, 4 ) );

	public string Name
	{
		get
		{
			int offset = _offset + NameOffset;
			int end = offset;
			while ( end < _data.Length && _data[end] != 0 ) end++;
			return Encoding.ASCII.GetString( _data[offset..end] );
		}
	}

	public StudioModel GetModel( int index )
	{
		int offset = _offset + ModelOffset + index * StudioModel.Size;
		return new StudioModel( _data, offset );
	}
}

/// <summary>
/// mstudiomodel_t - Model within a body part (148 bytes)
/// </summary>
internal readonly ref struct StudioModel
{
	public const int Size = 148;
	private readonly ReadOnlySpan<byte> _data;
	private readonly int _offset;

	public StudioModel( ReadOnlySpan<byte> data, int offset )
	{
		_data = data;
		_offset = offset;
	}

	public string Name
	{
		get
		{
			int end = 0;
			while ( end < 64 && _data[_offset + end] != 0 ) end++;
			return Encoding.ASCII.GetString( _data.Slice( _offset, end ) );
		}
	}

	public int MeshCount => BitConverter.ToInt32( _data.Slice( _offset + 72, 4 ) );
	private int MeshOffset => BitConverter.ToInt32( _data.Slice( _offset + 76, 4 ) );
	public int VertexCount => BitConverter.ToInt32( _data.Slice( _offset + 80, 4 ) );
	public int VertexIndex => BitConverter.ToInt32( _data.Slice( _offset + 84, 4 ) );
	public int EyeballCount => BitConverter.ToInt32( _data.Slice( _offset + 100, 4 ) );
	private int EyeballOffset => BitConverter.ToInt32( _data.Slice( _offset + 104, 4 ) );

	public StudioMesh GetMesh( int index )
	{
		int offset = _offset + MeshOffset + index * StudioMesh.Size;
		return new StudioMesh( _data, offset );
	}

	public StudioEyeball GetEyeball( int index )
	{
		int offset = _offset + EyeballOffset + index * StudioEyeball.Size;
		return new StudioEyeball( _data, offset );
	}
}

/// <summary>
/// mstudiomesh_t - Mesh definition (116 bytes)
/// </summary>
internal readonly ref struct StudioMesh
{
	public const int Size = 116;
	private readonly ReadOnlySpan<byte> _data;
	private readonly int _offset;

	public StudioMesh( ReadOnlySpan<byte> data, int offset )
	{
		_data = data;
		_offset = offset;
	}

	public int Material => BitConverter.ToInt32( _data.Slice( _offset, 4 ) );
	public int VertexCount => BitConverter.ToInt32( _data.Slice( _offset + 8, 4 ) );
	public int VertexOffset => BitConverter.ToInt32( _data.Slice( _offset + 12, 4 ) );
}

/// <summary>
/// mstudioeyeball_t - Eyeball definition (172 bytes)
/// </summary>
internal readonly ref struct StudioEyeball
{
	public const int Size = 172;
	private readonly ReadOnlySpan<byte> _data;
	private readonly int _offset;

	public StudioEyeball( ReadOnlySpan<byte> data, int offset )
	{
		_data = data;
		_offset = offset;
	}

	public int Bone => BitConverter.ToInt32( _data.Slice( _offset + 4, 4 ) );
	public Vector3 Origin => new(
		BitConverter.ToSingle( _data.Slice( _offset + 8, 4 ) ),
		BitConverter.ToSingle( _data.Slice( _offset + 12, 4 ) ),
		BitConverter.ToSingle( _data.Slice( _offset + 16, 4 ) )
	);
	public float ZOffset => BitConverter.ToSingle( _data.Slice( _offset + 20, 4 ) );
	public float Radius => BitConverter.ToSingle( _data.Slice( _offset + 24, 4 ) );
	public Vector3 Up => new(
		BitConverter.ToSingle( _data.Slice( _offset + 28, 4 ) ),
		BitConverter.ToSingle( _data.Slice( _offset + 32, 4 ) ),
		BitConverter.ToSingle( _data.Slice( _offset + 36, 4 ) )
	);
	public Vector3 Forward => new(
		BitConverter.ToSingle( _data.Slice( _offset + 40, 4 ) ),
		BitConverter.ToSingle( _data.Slice( _offset + 44, 4 ) ),
		BitConverter.ToSingle( _data.Slice( _offset + 48, 4 ) )
	);
	public int Texture => BitConverter.ToInt32( _data.Slice( _offset + 52, 4 ) );
	public float IrisScale => BitConverter.ToSingle( _data.Slice( _offset + 60, 4 ) );
}

/// <summary>
/// mstudiotexture_t - Texture/material reference (64 bytes)
/// </summary>
internal readonly ref struct StudioTexture
{
	public const int Size = 64;
	private readonly ReadOnlySpan<byte> _data;
	private readonly int _offset;

	public StudioTexture( ReadOnlySpan<byte> data, int offset )
	{
		_data = data;
		_offset = offset;
	}

	public string Name
	{
		get
		{
			int nameOffset = _offset + BitConverter.ToInt32( _data.Slice( _offset, 4 ) );
			int end = nameOffset;
			while ( end < _data.Length && _data[end] != 0 ) end++;
			return Encoding.ASCII.GetString( _data.Slice( nameOffset, end - nameOffset ) );
		}
	}
}

/// <summary>
/// mstudiovertex_t - Vertex data from VVD file (48 bytes)
/// </summary>
internal readonly struct StudioVertex
{
	public readonly float[] BoneWeights;
	public readonly byte[] BoneIds;
	public readonly byte NumBones;
	public readonly Vector3 Position;
	public readonly Vector3 Normal;
	public readonly Vector2 TexCoord;

	public StudioVertex( ReadOnlySpan<byte> data, int offset )
	{
		BoneWeights = [
			BitConverter.ToSingle( data.Slice( offset, 4 ) ),
			BitConverter.ToSingle( data.Slice( offset + 4, 4 ) ),
			BitConverter.ToSingle( data.Slice( offset + 8, 4 ) )
		];

		BoneIds = [data[offset + 12], data[offset + 13], data[offset + 14]];
		NumBones = data[offset + 15];

		Position = new Vector3(
			BitConverter.ToSingle( data.Slice( offset + 16, 4 ) ),
			BitConverter.ToSingle( data.Slice( offset + 20, 4 ) ),
			BitConverter.ToSingle( data.Slice( offset + 24, 4 ) )
		);

		Normal = new Vector3(
			BitConverter.ToSingle( data.Slice( offset + 28, 4 ) ),
			BitConverter.ToSingle( data.Slice( offset + 32, 4 ) ),
			BitConverter.ToSingle( data.Slice( offset + 36, 4 ) )
		);

		TexCoord = new Vector2(
			BitConverter.ToSingle( data.Slice( offset + 40, 4 ) ),
			BitConverter.ToSingle( data.Slice( offset + 44, 4 ) )
		);
	}
}

/// <summary>
/// vertexFileHeader_t - VVD file header
/// </summary>
internal readonly struct VvdFileHeader( byte[] data )
{
	private readonly byte[] _data = data;

	public int Id => BitConverter.ToInt32( _data, 0 );
	public int Version => BitConverter.ToInt32( _data, 4 );
	public int Checksum => BitConverter.ToInt32( _data, 8 );
	public int LodCount => BitConverter.ToInt32( _data, 12 );
	public int FixupCount => BitConverter.ToInt32( _data, 48 );
	public int FixupTableOffset => BitConverter.ToInt32( _data, 52 );
	public int VertexDataOffset => BitConverter.ToInt32( _data, 56 );
	public int TangentDataOffset => BitConverter.ToInt32( _data, 60 );

	public int GetLodVertexCount( int lod ) => BitConverter.ToInt32( _data, 16 + lod * 4 );

	public StudioVertex[] GetVertices( int rootLod )
	{
		int totalVerts = GetLodVertexCount( rootLod );
		var result = new StudioVertex[totalVerts];

		if ( FixupCount == 0 )
		{
			for ( int i = 0; i < totalVerts; i++ )
				result[i] = new StudioVertex( _data, VertexDataOffset + i * 48 );
		}
		else
		{
			int destIndex = 0;
			for ( int f = 0; f < FixupCount; f++ )
			{
				int fixupOffset = FixupTableOffset + f * 12;
				int fixupLod = BitConverter.ToInt32( _data, fixupOffset );
				int vertex = BitConverter.ToInt32( _data, fixupOffset + 4 );
				int vertexCount = BitConverter.ToInt32( _data, fixupOffset + 8 );

				if ( fixupLod >= rootLod )
				{
					for ( int v = 0; v < vertexCount; v++ )
						result[destIndex++] = new StudioVertex( _data, VertexDataOffset + (vertex + v) * 48 );
				}
			}
		}

		return result;
	}

	public Vector4[] GetTangents( int rootLod )
	{
		int totalVerts = GetLodVertexCount( rootLod );
		var result = new Vector4[totalVerts];

		if ( FixupCount == 0 )
		{
			for ( int i = 0; i < totalVerts; i++ )
			{
				int offset = TangentDataOffset + i * 16;
				result[i] = new Vector4(
					BitConverter.ToSingle( _data, offset ),
					BitConverter.ToSingle( _data, offset + 4 ),
					BitConverter.ToSingle( _data, offset + 8 ),
					BitConverter.ToSingle( _data, offset + 12 )
				);
			}
		}
		else
		{
			int destIndex = 0;
			for ( int f = 0; f < FixupCount; f++ )
			{
				int fixupOffset = FixupTableOffset + f * 12;
				int fixupLod = BitConverter.ToInt32( _data, fixupOffset );
				int vertex = BitConverter.ToInt32( _data, fixupOffset + 4 );
				int vertexCount = BitConverter.ToInt32( _data, fixupOffset + 8 );

				if ( fixupLod >= rootLod )
				{
					for ( int v = 0; v < vertexCount; v++ )
					{
						int offset = TangentDataOffset + (vertex + v) * 16;
						result[destIndex++] = new Vector4(
							BitConverter.ToSingle( _data, offset ),
							BitConverter.ToSingle( _data, offset + 4 ),
							BitConverter.ToSingle( _data, offset + 8 ),
							BitConverter.ToSingle( _data, offset + 12 )
						);
					}
				}
			}
		}

		return result;
	}
}

/// <summary>
/// OptimizedModel::FileHeader_t - VTX file header
/// </summary>
internal readonly struct VtxFileHeader( byte[] data )
{
	private readonly byte[] _data = data;

	public int Version => BitConverter.ToInt32( _data, 0 );
	public int Checksum => BitConverter.ToInt32( _data, 16 );
	public int LodCount => BitConverter.ToInt32( _data, 20 );
	public int BodyPartCount => BitConverter.ToInt32( _data, 28 );
	public int BodyPartOffset => BitConverter.ToInt32( _data, 32 );

	public VtxBodyPartHeader GetBodyPart( int index )
	{
		int offset = BodyPartOffset + index * 8;
		return new VtxBodyPartHeader( _data, offset );
	}
}

/// <summary>
/// OptimizedModel::BodyPartHeader_t - VTX body part (8 bytes)
/// </summary>
internal readonly struct VtxBodyPartHeader( byte[] data, int offset )
{
	private readonly byte[] _data = data;
	private readonly int _offset = offset;

	public int ModelCount => BitConverter.ToInt32( _data, _offset );
	private int ModelOffset => BitConverter.ToInt32( _data, _offset + 4 );

	public VtxModelHeader GetModel( int index )
	{
		int offset = _offset + ModelOffset + index * 8;
		return new VtxModelHeader( _data, offset );
	}
}

/// <summary>
/// OptimizedModel::ModelHeader_t - VTX model (8 bytes)
/// </summary>
internal readonly struct VtxModelHeader( byte[] data, int offset )
{
	private readonly byte[] _data = data;
	private readonly int _offset = offset;

	public int LodCount => BitConverter.ToInt32( _data, _offset );
	private int LodOffset => BitConverter.ToInt32( _data, _offset + 4 );

	public VtxModelLODHeader GetLod( int index )
	{
		int offset = _offset + LodOffset + index * 12;
		return new VtxModelLODHeader( _data, offset );
	}
}

/// <summary>
/// OptimizedModel::ModelLODHeader_t - VTX LOD (12 bytes)
/// </summary>
internal readonly struct VtxModelLODHeader( byte[] data, int offset )
{
	private readonly byte[] _data = data;
	private readonly int _offset = offset;

	public int MeshCount => BitConverter.ToInt32( _data, _offset );
	private int MeshOffset => BitConverter.ToInt32( _data, _offset + 4 );

	public VtxMeshHeader GetMesh( int index )
	{
		int offset = _offset + MeshOffset + index * 9;
		return new VtxMeshHeader( _data, offset );
	}
}

/// <summary>
/// OptimizedModel::MeshHeader_t - VTX mesh (9 bytes)
/// </summary>
internal readonly struct VtxMeshHeader( byte[] data, int offset )
{
	private readonly byte[] _data = data;
	private readonly int _offset = offset;

	public int StripGroupCount => BitConverter.ToInt32( _data, _offset );
	private int StripGroupOffset => BitConverter.ToInt32( _data, _offset + 4 );

	public VtxStripGroupHeader GetStripGroup( int index )
	{
		int offset = _offset + StripGroupOffset + index * 25;
		return new VtxStripGroupHeader( _data, offset );
	}
}

/// <summary>
/// OptimizedModel::StripGroupHeader_t - VTX strip group (25 bytes)
/// </summary>
internal readonly struct VtxStripGroupHeader( byte[] data, int offset )
{
	private readonly byte[] _data = data;
	private readonly int _offset = offset;

	public int VertexCount => BitConverter.ToInt32( _data, _offset );
	private int VertexOffset => BitConverter.ToInt32( _data, _offset + 4 );
	public int IndexCount => BitConverter.ToInt32( _data, _offset + 8 );
	private int IndexOffset => BitConverter.ToInt32( _data, _offset + 12 );
	public int StripCount => BitConverter.ToInt32( _data, _offset + 16 );
	private int StripOffset => BitConverter.ToInt32( _data, _offset + 20 );

	public VtxVertex GetVertex( int index )
	{
		int offset = _offset + VertexOffset + index * 9;
		return new VtxVertex( _data, offset );
	}

	public int GetIndex( int index )
	{
		int offset = _offset + IndexOffset + index * 2;
		return BitConverter.ToUInt16( _data, offset );
	}

	public VtxStripHeader GetStrip( int index )
	{
		int offset = _offset + StripOffset + index * 27;
		return new VtxStripHeader( _data, offset );
	}
}

/// <summary>
/// OptimizedModel::StripHeader_t - VTX strip (27 bytes)
/// Flags: STRIP_IS_TRILIST = 0x01, STRIP_IS_TRISTRIP = 0x02
/// </summary>
internal readonly struct VtxStripHeader( byte[] data, int offset )
{
	private readonly byte[] _data = data;
	private readonly int _offset = offset;

	public int IndexCount => BitConverter.ToInt32( _data, _offset );
	public int IndexOffset => BitConverter.ToInt32( _data, _offset + 4 );
	public int VertexCount => BitConverter.ToInt32( _data, _offset + 8 );
	public int VertexOffset => BitConverter.ToInt32( _data, _offset + 12 );
	public short BoneCount => BitConverter.ToInt16( _data, _offset + 16 );
	public byte Flags => _data[_offset + 18];
}

/// <summary>
/// OptimizedModel::Vertex_t - VTX vertex reference (9 bytes)
/// </summary>
internal readonly struct VtxVertex( byte[] data, int offset )
{
	public readonly int OrigMeshVertId = BitConverter.ToUInt16( data, offset + 4 );
}

/// <summary>
/// mstudioanimdesc_t - Animation description (100 bytes)
/// </summary>
internal readonly ref struct StudioAnimDesc
{
	public const int Size = 100;
	private readonly ReadOnlySpan<byte> _data;
	private readonly int _offset;

	public StudioAnimDesc( ReadOnlySpan<byte> data, int offset )
	{
		_data = data;
		_offset = offset;
	}

	private int NameOffset => BitConverter.ToInt32( _data.Slice( _offset + 4, 4 ) );
	public float Fps => BitConverter.ToSingle( _data.Slice( _offset + 8, 4 ) );
	public int Flags => BitConverter.ToInt32( _data.Slice( _offset + 12, 4 ) );
	public int NumFrames => BitConverter.ToInt32( _data.Slice( _offset + 16, 4 ) );
	public int AnimBlock => BitConverter.ToInt32( _data.Slice( _offset + 52, 4 ) );
	public int AnimIndex => BitConverter.ToInt32( _data.Slice( _offset + 56, 4 ) );
	public int SectionIndex => BitConverter.ToInt32( _data.Slice( _offset + 80, 4 ) );
	public int SectionFrames => BitConverter.ToInt32( _data.Slice( _offset + 84, 4 ) );

	public string Name
	{
		get
		{
			int offset = _offset + NameOffset;
			int end = offset;
			while ( end < _data.Length && _data[end] != 0 ) end++;
			return Encoding.ASCII.GetString( _data[offset..end] );
		}
	}

	public int AnimDataOffset => _offset + AnimIndex;

	public (int block, int index) GetAnimBlockForFrame( int frame )
	{
		if ( SectionFrames == 0 )
		{
			return (AnimBlock, AnimIndex);
		}

		int section = NumFrames > SectionFrames && frame == NumFrames - 1
		? (NumFrames / SectionFrames) + 1
		: frame / SectionFrames;
		int sectionOffset = _offset + SectionIndex + section * 8;
		int sectionBlock = BitConverter.ToInt32( _data.Slice( sectionOffset, 4 ) );
		int sectionAnimIndex = BitConverter.ToInt32( _data.Slice( sectionOffset + 4, 4 ) );

		return (sectionBlock, sectionAnimIndex);
	}

	public int GetSectionRelativeFrame( int frame )
	{
		return SectionFrames == 0
		? frame
		: NumFrames > SectionFrames && frame == NumFrames - 1
		? 0
		: frame % SectionFrames;
	}
}

/// <summary>
/// mstudioseqdesc_t - Sequence description (212 bytes)
/// </summary>
internal readonly ref struct StudioSeqDesc
{
	public const int Size = 212;
	private readonly ReadOnlySpan<byte> _data;
	private readonly int _offset;

	public StudioSeqDesc( ReadOnlySpan<byte> data, int offset )
	{
		_data = data;
		_offset = offset;
	}

	private int LabelOffset => BitConverter.ToInt32( _data.Slice( _offset + 4, 4 ) );
	private int ActivityNameOffset => BitConverter.ToInt32( _data.Slice( _offset + 8, 4 ) );
	public int Flags => BitConverter.ToInt32( _data.Slice( _offset + 12, 4 ) );
	public int Activity => BitConverter.ToInt32( _data.Slice( _offset + 16, 4 ) );
	public int ActivityWeight => BitConverter.ToInt32( _data.Slice( _offset + 20, 4 ) );
	public int NumBlends => BitConverter.ToInt32( _data.Slice( _offset + 56, 4 ) );
	private int AnimIndexOffset => BitConverter.ToInt32( _data.Slice( _offset + 60, 4 ) );
	public int GroupSize0 => BitConverter.ToInt32( _data.Slice( _offset + 68, 4 ) );
	public int GroupSize1 => BitConverter.ToInt32( _data.Slice( _offset + 72, 4 ) );
	public float FadeInTime => BitConverter.ToSingle( _data.Slice( _offset + 104, 4 ) );
	public float FadeOutTime => BitConverter.ToSingle( _data.Slice( _offset + 108, 4 ) );

	public bool Looping => (Flags & 0x0001) != 0;
	public bool Delta => (Flags & 0x0004) != 0;

	public string Label
	{
		get
		{
			int offset = _offset + LabelOffset;
			int end = offset;
			while ( end < _data.Length && _data[end] != 0 ) end++;
			return Encoding.ASCII.GetString( _data[offset..end] );
		}
	}

	public string ActivityName
	{
		get
		{
			int offset = _offset + ActivityNameOffset;
			int end = offset;
			while ( end < _data.Length && _data[end] != 0 ) end++;
			return Encoding.ASCII.GetString( _data[offset..end] );
		}
	}

	public int GetAnimIndex( int x, int y )
	{
		int offset = _offset + AnimIndexOffset + (y * GroupSize0 + x) * 2;
		return BitConverter.ToInt16( _data.Slice( offset, 2 ) );
	}
}

internal static class StudioAnimFlags
{
	public const byte RawPos = 0x01;
	public const byte RawRot = 0x02;
	public const byte AnimPos = 0x04;
	public const byte AnimRot = 0x08;
	public const byte Delta = 0x10;
	public const byte RawRot2 = 0x20;
}

internal ref struct StudioAnimReader
{
	private readonly ReadOnlySpan<byte> _data;
	private int _offset;

	public StudioAnimReader( ReadOnlySpan<byte> data, int offset )
	{
		_data = data;
		_offset = offset;
	}

	public bool ReadNext( out int bone, out byte flags, out Vector3 position, out Rotation rotation )
	{
		bone = _data[_offset];
		flags = _data[_offset + 1];
		short nextOffset = BitConverter.ToInt16( _data.Slice( _offset + 2, 2 ) );

		position = Vector3.Zero;
		rotation = Rotation.Identity;

		int dataOffset = _offset + 4;

		if ( (flags & StudioAnimFlags.RawRot) != 0 )
		{
			rotation = DecodeQuaternion48( _data, dataOffset );
			dataOffset += 6;
		}
		else if ( (flags & StudioAnimFlags.RawRot2) != 0 )
		{
			rotation = DecodeQuaternion64( _data, dataOffset );
			dataOffset += 8;
		}

		if ( (flags & StudioAnimFlags.RawPos) != 0 )
		{
			position = DecodeVector48( _data, dataOffset );
		}

		if ( nextOffset == 0 )
		{
			return false;
		}

		_offset += nextOffset;
		return true;
	}

	public static Rotation DecodeQuaternion48( ReadOnlySpan<byte> data, int offset )
	{
		ushort xRaw = BitConverter.ToUInt16( data.Slice( offset, 2 ) );
		ushort yRaw = BitConverter.ToUInt16( data.Slice( offset + 2, 2 ) );
		ushort zRaw = BitConverter.ToUInt16( data.Slice( offset + 4, 2 ) );

		float x = (xRaw - 32768) * (1.0f / 32768.0f);
		float y = (yRaw - 32768) * (1.0f / 32768.0f);
		float z = ((zRaw & 0x7FFF) - 16384) * (1.0f / 16384.0f);

		bool wNeg = (zRaw & 0x8000) != 0;
		float wSq = 1.0f - x * x - y * y - z * z;
		float w = MathF.Sqrt( MathF.Max( 0, wSq ) );
		if ( wNeg ) w = -w;

		return new Rotation( x, y, z, w );
	}

	public static Rotation DecodeQuaternion64( ReadOnlySpan<byte> data, int offset )
	{
		ulong packed = BitConverter.ToUInt64( data.Slice( offset, 8 ) );

		int xRaw = (int)(packed & 0x1FFFFF);
		int yRaw = (int)((packed >> 21) & 0x1FFFFF);
		int zRaw = (int)((packed >> 42) & 0x1FFFFF);
		bool wNeg = ((packed >> 63) & 1) != 0;

		float x = (xRaw - 1048576) * (1.0f / 1048576.5f);
		float y = (yRaw - 1048576) * (1.0f / 1048576.5f);
		float z = (zRaw - 1048576) * (1.0f / 1048576.5f);

		float wSq = 1.0f - x * x - y * y - z * z;
		float w = MathF.Sqrt( MathF.Max( 0, wSq ) );
		if ( wNeg ) w = -w;

		return new Rotation( x, y, z, w );
	}

	public static Vector3 DecodeVector48( ReadOnlySpan<byte> data, int offset )
	{
		float x = HalfToFloat( BitConverter.ToUInt16( data.Slice( offset, 2 ) ) );
		float y = HalfToFloat( BitConverter.ToUInt16( data.Slice( offset + 2, 2 ) ) );
		float z = HalfToFloat( BitConverter.ToUInt16( data.Slice( offset + 4, 2 ) ) );
		return new Vector3( x, y, z );
	}

	private static float HalfToFloat( ushort half )
	{
		return (float)BitConverter.UInt16BitsToHalf( half );
	}
}

