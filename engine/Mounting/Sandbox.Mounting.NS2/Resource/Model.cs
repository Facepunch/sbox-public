using Sandbox;
using System;
using System.Text;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

class ModelLoader( string fullPath ) : ResourceLoader<GameMount>
{
	private string FullPath { get; } = fullPath;
	private static readonly Material DefaultMaterial = Material.Load( "materials/dev/primary_white.vmat" );

	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	struct Vertex
	{
		public Vector3 Position;
		public Vector3 Normal;
		public Vector3 Tangent;
		public Vector3 Bitangent;
		public float TexU;
		public float TexV;
		public uint Packed;
		public float Weight0;
		public int Bone0;
		public float Weight1;
		public int Bone1;
		public float Weight2;
		public int Bone2;
		public float Weight3;
		public int Bone3;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct SubmeshHeader
	{
		public int MatId;
		public int StartTri;
		public int TriCount;
		public int PaletteSize;
	}

	struct Bone
	{
		public string Name;
		public Transform Transform;
		public int Parent;
	}

	struct Body
	{
		public string Name;
		public int BoneIndex;
		public Transform Transform;
		public Vector3[] Points;
		public int[] Indices;
	}

	struct Joint
	{
		public string Name;
		public int Body1;
		public int Body2;
		public Transform Frame1;
		public Transform Frame2;
		public float SwingMin;
		public float SwingMax;
		public float TwistMin;
		public float TwistMax;
	}

	struct Attachment
	{
		public string Name;
		public int Parent;
		public Transform Transform;
	}

	enum DataBlockTypes
	{
		Vertices = 1,
		Indices = 2,
		Submeshes = 3,
		Materials = 4,
		Bodies = 5,
		Bones = 6,
		AnimationClips = 7,
		Unknown8 = 8,
		Animations = 9,
		PoseParameters = 10,
		Unknown11 = 11,
		Unknown12 = 12,
		Attachments = 13,
		Joints = 14,
		Unknown15 = 15,
		CollisionReps = 16,
		Unknown17 = 17,
		Unknown18 = 18,
		ExternalModel = 19,
	}

	private readonly record struct Submesh( int MatId, int StartTri, int TriCount, int[] BonePalette );

	Body[] Bodies;
	Joint[] Joints;
	Attachment[] Attachments;

	private static unsafe SkinnedVertex[] ReadVertices( BinaryReader br, int count )
	{
		var size = sizeof( Vertex ) * count;
		var bytes = br.ReadBytes( size );
		var src = MemoryMarshal.Cast<byte, Vertex>( bytes );
		var dst = new SkinnedVertex[count];

		for ( int i = 0; i < count; i++ )
		{
			var v = src[i];
			dst[i] = new SkinnedVertex(
				ConvertPosition( v.Position ),
				ConvertDirection( v.Normal ),
				ConvertDirection( v.Tangent ),
				new Vector2( v.TexU, v.TexV ),
				new Color32( (byte)v.Bone0, (byte)v.Bone1, (byte)v.Bone2, (byte)v.Bone3 ),
				NormalizeWeights( v.Weight0, v.Weight1, v.Weight2, v.Weight3 )
			);
		}

		return dst;
	}

	private static unsafe int[] ReadIndices( BinaryReader br, int count )
	{
		var indices = new int[count];
		fixed ( int* dst = indices ) br.Read( new Span<byte>( dst, count * sizeof( int ) ) );
		return indices;
	}

	private static unsafe Submesh[] ReadSubmeshes( BinaryReader br, int count )
	{
		var submeshes = new Submesh[count];
		for ( int i = 0; i < count; i++ )
		{
			SubmeshHeader header;
			br.Read( new Span<byte>( &header, sizeof( SubmeshHeader ) ) );
			var palette = header.PaletteSize > 0 ? new int[header.PaletteSize] : [];
			if ( header.PaletteSize > 0 ) br.Read( MemoryMarshal.AsBytes( palette.AsSpan() ) );
			submeshes[i] = new Submesh( header.MatId, header.StartTri, header.TriCount, palette );
		}
		return submeshes;
	}

	private static string[] ReadMaterials( BinaryReader br, int count )
	{
		var names = new string[count];
		for ( int i = 0; i < count; i++ ) names[i] = Encoding.UTF8.GetString( br.ReadBytes( br.ReadInt32() ) );
		return names;
	}

	private static Bone[] ReadBones( BinaryReader br, int count )
	{
		var bones = new Bone[count];
		for ( int i = 0; i < count; i++ )
		{
			var name = Encoding.UTF8.GetString( br.ReadBytes( br.ReadInt32() ) );
			var parent = br.ReadInt32();
			var pos = new Vector3( br.ReadSingle(), br.ReadSingle(), br.ReadSingle() );
			var rot = new Rotation( br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle() );
			var scale = new Vector3( br.ReadSingle(), br.ReadSingle(), br.ReadSingle() );
			var unknown0 = br.ReadSingle();
			var unknown1 = br.ReadSingle();
			var unknown2 = br.ReadSingle();
			var unknown3 = br.ReadSingle();
			var unknown4 = br.ReadSingle();

			pos = ConvertPosition( pos );
			rot = ConvertRotation( rot );

			scale *= unknown4;

			var transform = new Transform( pos, rot, scale );
			if ( parent >= 0 ) transform = bones[parent].Transform.ToWorld( transform );

			bones[i] = new Bone { Name = name, Parent = parent, Transform = transform };
		}
		return bones;
	}

	static Transform ReadTransform( BinaryReader br )
	{
		var m1 = ConvertDirection( new Vector3( br.ReadSingle(), br.ReadSingle(), br.ReadSingle() ) );
		var m2 = ConvertDirection( new Vector3( br.ReadSingle(), br.ReadSingle(), br.ReadSingle() ) );
		var m0 = ConvertDirection( new Vector3( br.ReadSingle(), br.ReadSingle(), br.ReadSingle() ) );
		var pos = ConvertPosition( new Vector3( br.ReadSingle(), br.ReadSingle(), br.ReadSingle() ) );

		var mat = new Matrix(
			m0.x, m0.y, m0.z, 0,
			m1.x, m1.y, m1.z, 0,
			m2.x, m2.y, m2.z, 0,
			pos.x, pos.y, pos.z, 1
		);

		Matrix4x4.Decompose( mat, out var scale, out var rot, out var pos2 );
		return new Transform { Position = pos2, Rotation = rot, Scale = scale };
	}

	private void ReadBodies( BinaryReader br, int count )
	{
		Bodies = new Body[count];

		for ( int i = 0; i < count; i++ )
		{
			var name = Encoding.UTF8.GetString( br.ReadBytes( br.ReadInt32() ) );
			var boneIndex = br.ReadInt32();
			var transform = ReadTransform( br );
			var unknown = br.ReadSingle();

			var pointCount = br.ReadInt32();
			var points = new Vector3[pointCount];
			br.Read( MemoryMarshal.AsBytes( points.AsSpan() ) );

			for ( int v = 0; v < points.Length; v++ ) points[v] = ConvertPosition( points[v] );

			var triCount = br.ReadInt32();
			var collisionIndices = new int[triCount * 3];
			br.Read( MemoryMarshal.AsBytes( collisionIndices.AsSpan() ) );

			Bodies[i] = new Body
			{
				Name = name,
				BoneIndex = boneIndex,
				Transform = transform,
				Points = points,
				Indices = collisionIndices,
			};
		}
	}

	private void ReadJoints( BinaryReader br, int count )
	{
		Joints = new Joint[count];

		for ( int i = 0; i < count; i++ )
		{
			var name = Encoding.UTF8.GetString( br.ReadBytes( br.ReadInt32() ) );
			var body1 = br.ReadInt32();
			var frame1 = ReadTransform( br );
			var body2 = br.ReadInt32();
			var frame2 = ReadTransform( br );

			var p0 = br.ReadSingle();
			var swingMin = br.ReadSingle();
			var twistMin = br.ReadSingle();
			var p3 = br.ReadSingle();
			var swingMax = br.ReadSingle();
			var twistMax = br.ReadSingle();
			var p6 = br.ReadSingle();

			Joints[i] = new Joint
			{
				Name = name,
				Body1 = body1,
				Body2 = body2,
				Frame1 = frame1,
				Frame2 = frame2,
				SwingMin = swingMin,
				SwingMax = swingMax,
				TwistMin = twistMin,
				TwistMax = twistMax,
			};
		}
	}

	private void ReadAttachments( BinaryReader br, int count )
	{
		Attachments = new Attachment[count];

		for ( int i = 0; i < count; i++ )
		{
			var name = Encoding.UTF8.GetString( br.ReadBytes( br.ReadInt32() ) );
			var parent = br.ReadInt32();
			var transform = ReadTransform( br );

			Attachments[i] = new Attachment
			{
				Name = name,
				Parent = parent,
				Transform = transform
			};
		}
	}

	struct CollisionRep
	{
		public int BodyCount;
		public int Unknown0;
		public int Unknown1;
		public int Unknown2;
		public int Unknown3;
		public int Unknown4;
	}

	CollisionRep[] CollisionReps;

	private void ReadCollisionReps( BinaryReader br, int count )
	{
		CollisionReps = new CollisionRep[count];

		for ( int i = 0; i < count; i++ )
		{
			var rep = new CollisionRep
			{
				BodyCount = br.ReadInt32(),
				Unknown0 = br.ReadInt32(),
				Unknown1 = br.ReadInt32(),
				Unknown2 = br.ReadInt32(),
				Unknown3 = br.ReadInt32(),
				Unknown4 = br.ReadInt32(),
			};

			CollisionReps[i] = rep;
		}

		var nameCount = br.ReadInt32();
		for ( int i = 0; i < nameCount; i++ )
		{
			var name = Encoding.UTF8.GetString( br.ReadBytes( br.ReadInt32() ) );
			var unknown = br.ReadInt32();
		}
	}

	protected override object Load()
	{
		var bytes = File.ReadAllBytes( FullPath );
		using var ms = new MemoryStream( bytes );
		using var br = new BinaryReader( ms );
		if ( Encoding.ASCII.GetString( br.ReadBytes( 3 ) ) != "MDL" || br.ReadByte() != 7 ) throw new InvalidDataException();

		var builder = Model.Builder.WithName( Path );
		var vertices = Array.Empty<SkinnedVertex>();
		var indices = Array.Empty<int>();
		var bones = Array.Empty<Bone>();
		var materialNames = Array.Empty<string>();
		var submeshes = Array.Empty<Submesh>();

		while ( ms.Position < bytes.Length )
		{
			var blockType = (DataBlockTypes)br.ReadInt32();
			var length = br.ReadInt32();
			var nextPos = length + ms.Position;
			var count = br.ReadInt32();

			switch ( blockType )
			{
				case DataBlockTypes.Vertices: vertices = ReadVertices( br, count ); break;
				case DataBlockTypes.Indices: indices = ReadIndices( br, count ); break;
				case DataBlockTypes.Submeshes: submeshes = ReadSubmeshes( br, count ); break;
				case DataBlockTypes.Bones: bones = ReadBones( br, count ); break;
				case DataBlockTypes.Materials: materialNames = ReadMaterials( br, count ); break;
				case DataBlockTypes.Bodies: ReadBodies( br, count ); break;
				case DataBlockTypes.Joints: ReadJoints( br, count ); break;
				case DataBlockTypes.Attachments: ReadAttachments( br, count ); break;
				case DataBlockTypes.CollisionReps: ReadCollisionReps( br, count ); break;
				default: break;
			}

			ms.Seek( nextPos, SeekOrigin.Begin );
		}

		if ( Attachments is not null )
		{
			foreach ( var attachment in Attachments )
			{
				var parentName = attachment.Parent >= 0 && attachment.Parent < bones.Length ? bones[attachment.Parent].Name : null;
				builder.AddAttachment( attachment.Name, attachment.Transform.Position, attachment.Transform.Rotation, parentName );
			}
		}

		for ( int i = 0; i < bones.Length; i++ )
		{
			var bone = bones[i];
			var parentName = bone.Parent >= 0 && bone.Parent < bones.Length ? bones[bone.Parent].Name : null;
			builder.AddBone( bone.Name, bone.Transform.Position, bone.Transform.Rotation, parentName );
		}

		var marker = new byte[vertices.Length];
		var remap = new int[vertices.Length];

		foreach ( var (MatId, StartTri, TriCount, BonePalette) in submeshes )
		{
			Array.Clear( marker, 0, marker.Length );

			var submeshIndices = indices.AsSpan( StartTri * 3, TriCount * 3 );
			var uniqueIndices = new int[submeshIndices.Length];
			var uniqueCount = 0;
			for ( int i = 0; i < submeshIndices.Length; i++ )
			{
				var idx = submeshIndices[i];
				if ( marker[idx] == 0 )
				{
					marker[idx] = 1;
					uniqueIndices[uniqueCount++] = idx;
				}
			}

			for ( int i = 0; i < uniqueCount; i++ ) remap[uniqueIndices[i]] = i;

			var bounds = new BBox { Mins = float.MaxValue, Maxs = float.MinValue };
			var subVertices = new SkinnedVertex[uniqueCount];
			for ( int i = 0; i < uniqueCount; i++ )
			{
				var v = vertices[uniqueIndices[i]];
				v.blendIndices = new Color32(
					(byte)(v.blendIndices.r < BonePalette.Length ? BonePalette[v.blendIndices.r] : 255),
					(byte)(v.blendIndices.g < BonePalette.Length ? BonePalette[v.blendIndices.g] : 255),
					(byte)(v.blendIndices.b < BonePalette.Length ? BonePalette[v.blendIndices.b] : 255),
					(byte)(v.blendIndices.a < BonePalette.Length ? BonePalette[v.blendIndices.a] : 255)
				);
				subVertices[i] = v;
				bounds = bounds.AddPoint( v.position );
			}

			var remappedIndices = new int[submeshIndices.Length];
			for ( int i = 0; i < submeshIndices.Length; i++ ) remappedIndices[i] = remap[submeshIndices[i]];

			var materialName = materialNames[MatId];
			var material = string.IsNullOrWhiteSpace( materialName ) ? DefaultMaterial : Material.Load( $"mount://ns2/ns2/{materialName}.vmat" );
			var mesh = new Mesh( material );
			mesh.Bounds = bounds;
			mesh.CreateVertexBuffer( subVertices.Length, subVertices );
			mesh.CreateIndexBuffer( remappedIndices.Length, remappedIndices );
			builder.AddMesh( mesh );
		}

		var bodyMap = new Dictionary<int, int>();

		if ( CollisionReps is not null && CollisionReps.Length > 0 )
		{
			var rep = CollisionReps[0];
			var builders = new Dictionary<int, (PhysicsBodyBuilder, int)>();

			var allVertices = new List<Vector3>();
			var allIndices = new List<int>();

			int builderIndex = 0;

			for ( int i = 0; i < rep.BodyCount; i++ )
			{
				var bodySrc = Bodies[i];
				var boneIndex = bodySrc.BoneIndex;
				var points = bodySrc.Points.ToArray();
				var transform = bodySrc.Transform;

				if ( Joints is null || Joints.Length == 0 )
				{
					if ( boneIndex >= 0 )
						transform = bones[boneIndex].Transform.ToWorld( transform );

					boneIndex = -1;
				}

				if ( !builders.TryGetValue( boneIndex, out var body ) )
				{
					body = new( builder.AddBody(), builderIndex++ );
					builders[boneIndex] = body;
				}

				for ( int v = 0; v < points.Length; v++ )
					points[v] = transform.PointToWorld( points[v] );

				bodyMap[i] = body.Item2;

				body.Item1.BoneName = bones.Length == 0 ? null : boneIndex >= 0 ? bones[boneIndex].Name : bones[0].Name;
				body.Item1.AddHull( points, Transform.Zero, new PhysicsBodyBuilder.HullSimplify
				{
					AngleTolerance = 0.1f,
					DistanceTolerance = 0.1f,
					Method = PhysicsBodyBuilder.SimplifyMethod.QEM
				} );

				int baseIndex = allVertices.Count;
				for ( int v = 0; v < points.Length; v++ )
					allVertices.Add( transform.PointToWorld( bodySrc.Points[v] ) );

				for ( int idx = 0; idx < bodySrc.Indices.Length; idx++ )
					allIndices.Add( baseIndex + bodySrc.Indices[idx] );
			}

			builder.AddTraceMesh( allVertices, allIndices );
		}

		if ( Joints is not null )
		{
			foreach ( var joint in Joints )
			{
				if ( !bodyMap.TryGetValue( joint.Body1, out var body1 ) ) continue;
				if ( !bodyMap.TryGetValue( joint.Body2, out var body2 ) ) continue;
				if ( body1 == body2 ) continue;

				var frame1 = Bodies[joint.Body1].Transform.ToWorld( joint.Frame1 );
				var frame2 = Bodies[joint.Body2].Transform.ToWorld( joint.Frame2 );

				var swingMin = joint.SwingMin;
				var swingMax = joint.SwingMax;

				var twistMin = joint.TwistMin;
				var twistMax = joint.TwistMax;

				float swing = MathF.Max( MathF.Abs( swingMin ), MathF.Abs( swingMax ) ).RadianToDegree();
				float twistLow = -MathF.Max( twistMin, twistMax ).RadianToDegree();
				float twistHigh = -MathF.Min( twistMin, twistMax ).RadianToDegree();

				builder.AddBallJoint( body1, body2, frame1, frame2 )
					.WithSwingLimit( swing )
					.WithTwistLimit( twistLow, twistHigh );
			}
		}

		return builder.Create();
	}

	private static Color32 NormalizeWeights( float w0, float w1, float w2, float w3 )
	{
		var iw0 = (int)(w0 * 255 + 0.5f);
		var iw1 = (int)(w1 * 255 + 0.5f);
		var iw2 = (int)(w2 * 255 + 0.5f);
		var iw3 = (int)(w3 * 255 + 0.5f);

		var diff = 255 - (iw0 + iw1 + iw2 + iw3);
		if ( diff != 0 )
		{
			var max = Math.Max( Math.Max( iw0, iw1 ), Math.Max( iw2, iw3 ) );
			if ( iw0 == max ) iw0 += diff;
			else if ( iw1 == max ) iw1 += diff;
			else if ( iw2 == max ) iw2 += diff;
			else iw3 += diff;
		}

		return new Color32( (byte)iw0, (byte)iw1, (byte)iw2, (byte)iw3 );
	}

	private static float Scale => 40.0f;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static Vector3 ConvertPosition( Vector3 v ) => new Vector3( v.z, v.x, v.y ) * Scale;
	private static Vector3 ConvertScale( Vector3 v ) => new( v.z, v.x, v.y );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static Vector3 ConvertDirection( Vector3 v ) => new( v.z, v.x, v.y );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static Rotation ConvertRotation( Rotation r ) => new( r.z, r.x, r.y, r.w );

	[StructLayout( LayoutKind.Sequential )]
	private struct SkinnedVertex( Vector3 position, Vector3 normal, Vector3 tangent, Vector2 texcoord, Color32 blendIndices, Color32 blendWeights )
	{
		[VertexLayout.Position] public Vector3 position = position;
		[VertexLayout.Normal] public Vector3 normal = normal;
		[VertexLayout.Tangent] public Vector3 tangent = tangent;
		[VertexLayout.TexCoord] public Vector2 texcoord = texcoord;
		[VertexLayout.BlendIndices] public Color32 blendIndices = blendIndices;
		[VertexLayout.BlendWeight] public Color32 blendWeights = blendWeights;

		public static readonly VertexAttribute[] Layout =
		[
			new( VertexAttributeType.Position, VertexAttributeFormat.Float32, 3 ),
			new( VertexAttributeType.Normal, VertexAttributeFormat.Float32, 3 ),
			new( VertexAttributeType.Tangent, VertexAttributeFormat.Float32, 3 ),
			new( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2 ),
			new( VertexAttributeType.BlendIndices, VertexAttributeFormat.UInt8, 4 ),
			new( VertexAttributeType.BlendWeights, VertexAttributeFormat.UInt8, 4 )
		];
	}
}
