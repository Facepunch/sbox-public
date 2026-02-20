using System;
using System.Runtime.InteropServices;

class ModelLoader( string fullPath ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		var ms = new MemoryStream( File.ReadAllBytes( fullPath ) );
		var mdlFile = GoldSrc.Mdl.File.FromStream( ms );
		var textureMdl = mdlFile;
		ms.Dispose();

		if ( mdlFile.Header.NumTextures == 0 )
		{
			var texturePath = System.IO.Path.ChangeExtension( fullPath, null ) + "t.mdl";
			if ( File.Exists( texturePath ) )
			{
				textureMdl = GoldSrc.Mdl.File.FromStream( new MemoryStream( File.ReadAllBytes( texturePath ) ) );
			}
		}

		var seqGroups = new List<BinaryReader>
		{
			new( new MemoryStream( File.ReadAllBytes( fullPath ) ) )
		};

		if ( mdlFile.Header.NumSeqGroups > 1 )
		{
			for ( int i = 1; i < mdlFile.Header.NumSeqGroups; i++ )
			{
				var seqPath = System.IO.Path.ChangeExtension( fullPath, null ) + $"{i:D2}.mdl";
				if ( File.Exists( seqPath ) )
				{
					seqGroups.Add( new BinaryReader( new MemoryStream( File.ReadAllBytes( seqPath ) ) ) );
				}
			}
		}

		mdlFile.LoadAnimations( seqGroups );

		foreach ( var seq in seqGroups )
		{
			seq.Dispose();
		}

		var builder = Model.Builder.WithName( Path );

		var mdlBones = mdlFile.Bones.ToArray();
		var skeletonTransforms = new List<(Transform tx, int parent)>();
		skeletonTransforms.AddRange( mdlFile.Bones
			.Select( x => (new Transform( new Vector3( x.Value[0], x.Value[1], x.Value[2] ),
			AngleQuaternion( new Vector3( x.Value[3], x.Value[4], x.Value[5] ) ) ), x.Parent) ) );

		var boneTransforms = new Transform[mdlBones.Length];
		for ( var i = 0; i < boneTransforms.Length; ++i )
		{
			var (tx, parent) = skeletonTransforms[i];
			boneTransforms[i] = parent >= 0 ? boneTransforms[parent].ToWorld( tx ) : tx;
		}

		var bones = new List<ModelBuilder.Bone>();
		var boneIndex = 0;
		foreach ( var bone in mdlBones )
		{
			var transform = boneTransforms[boneIndex];
			var name = bone.Name;
			if ( string.IsNullOrWhiteSpace( name ) ) name = $"unnamed bone {boneIndex}";
			bones.Add( new ModelBuilder.Bone( name, bone.Parent == -1 ? null : bones[bone.Parent].Name, transform.Position, transform.Rotation ) );
			boneIndex++;
		}

		var collisionVertices = new List<Vector3>();
		var collisionIndices = new List<int>();
		var materialCache = new Dictionary<int, Material>();
		var skinRefs = new List<int>();

		for ( var bodyPartIndex = 0; bodyPartIndex < mdlFile.NumBodyParts; ++bodyPartIndex )
		{
			var bodyPart = mdlFile.GetBodyPart( bodyPartIndex );

			for ( var modelIndex = 0; modelIndex < bodyPart.NumModels; ++modelIndex )
			{
				var bodyPartModel = mdlFile.GetModel( bodyPartIndex, modelIndex );
				var vertices = new Vector3[bodyPartModel.NumVerts];
				var normals = new Vector3[bodyPartModel.NumNorms];
				var boneVertices = new byte[bodyPartModel.NumVerts];

				mdlFile.GetVertices( bodyPartIndex, modelIndex, vertices );
				mdlFile.GetNormals( bodyPartIndex, modelIndex, normals );
				mdlFile.GetBoneVertices( bodyPartIndex, modelIndex, boneVertices );

				for ( int i = 0; i < vertices.Length; ++i )
				{
					var boneVertex = boneVertices[i];
					var position = boneTransforms[boneVertex].PointToWorld( vertices[i] );
					collisionVertices.Add( position );
				}

				if ( bodyPartModel.NumMesh == 0 )
				{
					builder.AddMesh( null, bodyPart.Name, modelIndex );
					continue;
				}

				for ( var meshIndex = 0; meshIndex < bodyPartModel.NumMesh; ++meshIndex )
				{
					var modelMesh = mdlFile.GetMesh( bodyPartIndex, modelIndex, meshIndex );
					var textureIndex = textureMdl.GetTextureIndex( modelMesh.SkinRef, 0 );
					var texture = textureMdl.GetTexture( textureIndex );
					textureMdl.GetTextureData( textureIndex, out var textureName, out var textureWidth, out var textureHeight, out var textureFlags );

					var triVerts = new GoldSrc.Mdl.TriVert[modelMesh.NumTris * 3];
					mdlFile.GetTriVerts( bodyPartIndex, modelIndex, meshIndex, triVerts );

					var s = 1.0f / textureWidth;
					var t = 1.0f / textureHeight;

					var uniqueVertices = new List<SkinnedVertex>( triVerts.Length );
					var vertexMap = new Dictionary<GoldSrc.Mdl.TriVert, int>();
					var indices = new int[triVerts.Length];
					var indexCount = 0;

					foreach ( var triVert in triVerts )
					{
						if ( !vertexMap.TryGetValue( triVert, out int index ) )
						{
							index = uniqueVertices.Count;
							var position = vertices[triVert.VertIndex];
							var normal = normals[triVert.NormIndex];

							var boneVertex = boneVertices[triVert.VertIndex];
							position = boneTransforms[boneVertex].PointToWorld( position );
							normal = boneTransforms[boneVertex].NormalToWorld( normal );

							var blendIndices = new Color32( boneVertex, 255, 255, 255 );
							var blendWeights = new Color32( 255, 0, 0, 0 );

							uniqueVertices.Add( new SkinnedVertex( position, normal, new Vector2( triVert.S * s, triVert.T * t ), blendIndices, blendWeights ) );
							vertexMap[triVert] = index;
						}

						indices[indexCount++] = index;

						collisionIndices.Add( triVert.VertIndex );
					}

					if ( !materialCache.TryGetValue( textureIndex, out var material ) )
					{
						material = Material.Create( $"{Path}/{textureName}.vmat", "goldsrc", false );
						material?.Set( "Color", texture );
						material?.SetFeature( "F_CHROME", textureFlags.HasFlag( GoldSrc.Mdl.LightingFlags.Chrome ) ? 1 : 0 );

						materialCache[textureIndex] = material;
						skinRefs.Add( modelMesh.SkinRef );
					}

					var mesh = new Mesh( bodyPartModel.Name, material );
					mesh.CreateVertexBuffer( uniqueVertices.Count, uniqueVertices );
					mesh.CreateIndexBuffer( indices.Length, indices );
					mesh.Bounds = BBox.FromPoints( uniqueVertices.Select( x => x.Position ) );
					builder.AddMesh( mesh, bodyPart.Name, modelIndex );
				}
			}
		}

		if ( bones.Count > 0 )
		{
			builder.AddBones( [.. bones] );
		}

		int sequenceIndex = 0;
		foreach ( var sequence in mdlFile.Sequences )
		{
			var transforms = mdlFile.SequenceTransforms[sequenceIndex].Transforms.AsSpan();
			int boneCount = bones.Count;
			var frameCount = sequence.NumFrames;
			var animation = builder.AddAnimation( sequence.Label, sequence.Fps );

			for ( var frameIndex = 0; frameIndex < frameCount; ++frameIndex )
			{
				animation.AddFrame( transforms.Slice( frameIndex * boneCount, boneCount ) );
			}

			sequenceIndex++;
		}

		var numSkinFamilies = textureMdl.Header.NumSkinFamilies;
		if ( numSkinFamilies > 1 )
		{
			for ( int skinFamily = 0; skinFamily < textureMdl.Header.NumSkinFamilies; skinFamily++ )
			{
				var group = builder.AddMaterialGroup( $"Skin {skinFamily}" );

				foreach ( var skinRef in skinRefs )
				{
					int textureIndex = textureMdl.GetTextureIndex( skinRef, skinFamily );
					if ( !materialCache.TryGetValue( textureIndex, out var material ) )
					{
						var texture = textureMdl.GetTexture( textureIndex );
						var textureName = textureMdl.GetTextureName( textureIndex );
						material = Material.Create( $"{Path}/{textureName}.vmat", "goldsrc", false );
						material?.Set( "Color", texture );
						materialCache[textureIndex] = material;
					}

					group.AddMaterial( material );
				}
			}
		}

		foreach ( var hb in mdlFile.HitBoxes )
		{
			int b = hb.Bone;
			var localCenter = (hb.Min + hb.Max) * 0.5f;
			var halfExtents = (hb.Max - hb.Min) * 0.5f;
			var worldCenter = boneTransforms[b].PointToWorld( localCenter );
			var worldRot = boneTransforms[b].Rotation;
			builder.AddCollisionBox( halfExtents, worldCenter, worldRot );
		}

		builder.AddTraceMesh( collisionVertices, collisionIndices );

		return builder.Create();
	}

	private static Rotation AngleQuaternion( Vector3 angles )
	{
		var (sy, cy) = MathF.SinCos( angles.z * 0.5f );
		var (sp, cp) = MathF.SinCos( angles.y * 0.5f );
		var (sr, cr) = MathF.SinCos( angles.x * 0.5f );

		return new Rotation
		{
			x = (sr * cp * cy) - (cr * sp * sy),
			y = (cr * sp * cy) + (sr * cp * sy),
			z = (cr * cp * sy) - (sr * sp * cy),
			w = (cr * cp * cy) + (sr * sp * sy)
		};
	}
}

[StructLayout( LayoutKind.Sequential )]
public struct SkinnedVertex( Vector3 position, Vector3 normal, Vector2 texcoord, Color32 blendIndices, Color32 blendWeights )
{
	[VertexLayout.Position]
	public Vector3 Position = position;

	[VertexLayout.Normal]
	public Vector3 Normal = normal;

	[VertexLayout.TexCoord]
	public Vector2 Texcoord = texcoord;

	[VertexLayout.BlendIndices]
	public Color32 BlendIndices = blendIndices;

	[VertexLayout.BlendWeight]
	public Color32 BlendWeights = blendWeights;
}
