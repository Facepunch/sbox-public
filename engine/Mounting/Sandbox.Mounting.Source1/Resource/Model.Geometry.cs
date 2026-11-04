using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceModelBuilder
{
	private BBox?[] AddGeometry( ModelBuilder builder, MdlFile mdl, VtxFile vtx, MaterialSlots materials,
		BBox viewBounds, string[] bodyGroupNames, string[] flexNames, VertexTransform[] skinTransforms, StaticPropLighting lighting = null )
	{
		var boneBounds = new BBox?[mdl.BoneCount];
		if ( vtx == null )
			return boneBounds;
		for ( var lod = 0; lod < vtx.RenderLodCount; lod++ )
		{
			builder.WithLodDistance( lod, vtx.GetLodSwitchPoint( lod ) );
			AddLodGeometry( builder, mdl, vtx, materials, viewBounds, bodyGroupNames, flexNames, lod, boneBounds, skinTransforms, lighting );
		}
		return boneBounds;
	}

	private void AddLodGeometry( ModelBuilder builder, MdlFile mdl, VtxFile vtx, MaterialSlots materials,
		BBox viewBounds, string[] bodyGroupNames, string[] flexNames, int lod, BBox?[] boneBounds, VertexTransform[] skinTransforms,
		StaticPropLighting lighting )
	{
		for ( int bodyPartIndex = 0; bodyPartIndex < mdl.BodyPartCount; bodyPartIndex++ )
		{
			var bodyPart = mdl.GetBodyPart( bodyPartIndex );
			for ( int modelIndex = 0; modelIndex < bodyPart.NumModels; modelIndex++ )
			{
				var model = mdl.GetModel( bodyPartIndex, modelIndex );
				for ( int meshIndex = 0; meshIndex < model.NumMeshes; meshIndex++ )
				{
					var mesh = mdl.GetMesh( bodyPartIndex, modelIndex, meshIndex );
					if ( mesh.NumVertices == 0 )
						continue;
					var indexCount = vtx.GetIndexCount( bodyPartIndex, modelIndex, lod, meshIndex );

					if ( indexCount < 3 )
						continue;

					var vertices = vtx.GetVertices( bodyPartIndex, modelIndex, lod, meshIndex );
					var indices = GC.AllocateUninitializedArray<int>( indexCount );
					vtx.GetIndices( bodyPartIndex, modelIndex, lod, meshIndex, indices );
					var meshTangents = vtx.GetTangents( bodyPartIndex, modelIndex, lod, meshIndex );

					var vertexCount = 0;
					var originalToOutput = GC.AllocateUninitializedArray<int>( Math.Min( mesh.NumVertices, ushort.MaxValue + 1 ) );
					Array.Fill( originalToOutput, -1 );

					for ( int i = 0; i < indices.Length; i++ )
					{
						var vertexIndex = indices[i];
						var mapped = originalToOutput[vertexIndex];
						if ( mapped == -1 )
						{
							mapped = vertexCount++;
							originalToOutput[vertexIndex] = mapped;
						}
						indices[i] = mapped;
					}

					var uniqueVerts = GC.AllocateUninitializedArray<SkinnedVertex>( vertexCount );
					var vertexTransforms = skinTransforms is null ? null : new VertexTransform[vertexCount];
					var sourceVertexCount = Math.Min( vertices.Length, originalToOutput.Length );
					for ( var vertexIndex = 0; vertexIndex < sourceVertexCount; vertexIndex++ )
					{
						var mapped = originalToOutput[vertexIndex];
						if ( mapped < 0 )
							continue;
						var src = vertices[vertexIndex];

						var boneWeights = src.BoneWeights;
						var pos = new Vector3( src.Position.x, src.Position.y, src.Position.z );
						if ( !IsFinite( pos ) )
							throw new InvalidDataException( "Model vertex contains a non-finite position." );
						var norm = new Vector3( src.Normal.x, src.Normal.y, src.Normal.z );
						var tangent = !meshTangents.IsEmpty ? meshTangents[vertexIndex] : default;

						GetBlendWeights( boneWeights, mdl.Bones.Length, out var blendIndices, out var blendWeights );
						if ( vertexTransforms is not null )
							vertexTransforms[mapped] = GetVertexTransform( boneWeights, skinTransforms );

						uniqueVerts[mapped] = new SkinnedVertex( pos, norm, tangent, new Vector2( src.TexCoordX, src.TexCoordY ), blendIndices, blendWeights );
					}

					for ( int i = 0; i < indices.Length; i += 3 )
						(indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);

					var lightingKey = (bodyPartIndex, modelIndex, lod, meshIndex);
					var material = materials.GetForMesh( mesh.Material, lod, lightingKey );
					var renderMesh = new Mesh( material );
					var morphBounds = mesh.NumFlexes > 0
						? AddMeshMorphs( mdl, mesh, originalToOutput, flexNames, renderMesh, uniqueVerts.Length, vertexTransforms )
						: null;
					AddBoneBounds( mdl, uniqueVerts, morphBounds, boneBounds );
					if ( lod == 0 )
					{
						var tracePositions = GC.AllocateUninitializedArray<Vector3>( uniqueVerts.Length );
						for ( var i = 0; i < tracePositions.Length; i++ )
							tracePositions[i] = vertexTransforms is not null
								? vertexTransforms[i].Point( uniqueVerts[i].position ) : uniqueVerts[i].position;
						builder.AddTraceMesh( tracePositions, indices );
					}
					if ( materials.UsesShader( mesh.Material, lod, "shaders/source1/teeth.shader" ) )
					{
						if ( mesh.MaterialType == 1 )
							throw new NotSupportedException( $"{FullPath}: Teeth requires an MDL mouth mesh, not an eye mesh." );
						var teethVertices = CreateTeethVertices( uniqueVerts, mdl, vertexTransforms, skinTransforms );
						renderMesh.CreateVertexBuffer<TeethVertex>( teethVertices.Length, teethVertices );
					}
					else if ( mesh.MaterialType == 1 )
					{
						var eye = mdl.GetEyeball( bodyPartIndex, modelIndex, mesh.MaterialParam );
						var eyeVertices = CreateEyeVertices( uniqueVerts, eye, mdl.Bones[eye.Bone].PoseToBone, vertexTransforms );
						renderMesh.CreateVertexBuffer<EyeVertex>( eyeVertices.Length, eyeVertices );
					}
					else
					{
						if ( vertexTransforms is not null )
							for ( var i = 0; i < uniqueVerts.Length; i++ )
								uniqueVerts[i] = TransformVertex( uniqueVerts[i], vertexTransforms[i] );
						if ( lighting is not null )
						{
							var litVertices = new StaticPropVertex[uniqueVerts.Length];
							for ( var original = 0; original < originalToOutput.Length; original++ )
							{
								var mapped = originalToOutput[original];
								if ( mapped < 0 ) continue;
								var vertex = uniqueVerts[mapped];
								litVertices[mapped] = new StaticPropVertex( vertex,
									lighting.GetColor( lightingKey, original, vertex.position, vertex.normal ) );
							}
							renderMesh.CreateVertexBuffer<StaticPropVertex>( litVertices.Length, litVertices );
						}
						else
							renderMesh.CreateVertexBuffer<SkinnedVertex>( uniqueVerts.Length, uniqueVerts );
					}
					renderMesh.CreateIndexBuffer( indices.Length, indices );
					renderMesh.Bounds = viewBounds;

					builder.AddMesh( renderMesh, lod, bodyGroupNames[bodyPartIndex], modelIndex );
				}
			}
		}
	}

	private readonly record struct VertexTransform( Vector3 X, Vector3 Y, Vector3 Z, Vector3 Position )
	{
		public Vector3 Direction( Vector3 value ) => X * value.x + Y * value.y + Z * value.z;
		public Vector3 Point( Vector3 value ) => Direction( value ) + Position;
	}

	private static VertexTransform GetVertexTransform( StudioBoneWeight weights, VertexTransform[] transforms )
	{
		var x = Vector3.Zero;
		var y = Vector3.Zero;
		var z = Vector3.Zero;
		var position = Vector3.Zero;
		for ( var i = 0; i < Math.Max( 1, (int)weights.NumBones ); i++ )
		{
			var bone = weights.NumBones == 0 ? 0 : i switch { 0 => weights.Bone0, 1 => weights.Bone1, _ => weights.Bone2 };
			var weight = weights.NumBones <= 1 ? 1 : i switch { 0 => weights.Weight0, 1 => weights.Weight1, _ => weights.Weight2 };
			var transform = transforms[bone];
			x += transform.X * weight;
			y += transform.Y * weight;
			z += transform.Z * weight;
			position += transform.Position * weight;
		}
		return new VertexTransform( x, y, z, position );
	}

	private static SkinnedVertex TransformVertex( SkinnedVertex vertex, VertexTransform transform )
	{
		vertex.position = transform.Point( vertex.position );
		vertex.normal = transform.Direction( vertex.normal );
		vertex.tangent = new Vector4( transform.Direction( new Vector3( vertex.tangent.x, vertex.tangent.y, vertex.tangent.z ) ), vertex.tangent.w );
		return vertex;
	}

	private static void GetBlendWeights( StudioBoneWeight weights, int boneCount, out Color32 indices, out Color32 blend )
	{
		if ( weights.NumBones > 3
			|| (weights.NumBones > 0 && weights.Bone0 >= boneCount)
			|| (weights.NumBones > 1 && weights.Bone1 >= boneCount)
			|| (weights.NumBones > 2 && weights.Bone2 >= boneCount) )
			throw new InvalidDataException( "VVD vertex references an invalid bone." );

		if ( weights.NumBones <= 1 )
		{
			if ( weights.NumBones == 1 && (!float.IsFinite( weights.Weight0 ) || weights.Weight0 <= 0) )
				throw new InvalidDataException( "VVD vertex has invalid bone weights." );
			indices = new Color32( weights.NumBones == 1 ? weights.Bone0 : (byte)0, 0, 0, 0 );
			blend = new Color32( 255, 0, 0, 0 );
			return;
		}

		double a = weights.Weight0;
		double b = weights.Weight1;
		double c = weights.NumBones > 2 ? weights.Weight2 : 0;
		var total = a + b + c;
		if ( a < 0 || b < 0 || c < 0 || !double.IsFinite( total ) || total <= 0 )
			throw new InvalidDataException( "VVD vertex has invalid bone weights." );

		var scaled0 = a / total * 255;
		var scaled1 = b / total * 255;
		var scaled2 = c / total * 255;
		var w0 = (int)scaled0;
		var w1 = (int)scaled1;
		var w2 = (int)scaled2;
		var remainder = 255 - w0 - w1 - w2;
		var fraction0 = scaled0 - w0;
		var fraction1 = scaled1 - w1;
		var fraction2 = scaled2 - w2;
		while ( remainder-- > 0 )
		{
			if ( fraction0 >= fraction1 && fraction0 >= fraction2 )
			{
				w0++;
				fraction0 = -1;
			}
			else if ( fraction1 >= fraction2 )
			{
				w1++;
				fraction1 = -1;
			}
			else
			{
				w2++;
				fraction2 = -1;
			}
		}

		indices = new Color32( weights.Bone0, weights.Bone1, weights.NumBones > 2 ? weights.Bone2 : (byte)0, 0 );
		blend = new Color32( (byte)w0, (byte)w1, (byte)w2, 0 );
	}

	[StructLayout( LayoutKind.Sequential )]
	public struct SkinnedVertex( Vector3 position, Vector3 normal, Vector4 tangent, Vector2 texcoord, Color32 blendIndices, Color32 blendWeights )
	{
		[VertexLayout.Position]
		public Vector3 position = position;

		[VertexLayout.Normal]
		public Vector3 normal = normal;

		[VertexLayout.Tangent]
		public Vector4 tangent = tangent;

		[VertexLayout.TexCoord]
		public Vector2 texcoord = texcoord;

		[VertexLayout.BlendIndices]
		public Color32 blendIndices = blendIndices;

		[VertexLayout.BlendWeight]
		public Color32 blendWeights = blendWeights;

		public static readonly VertexAttribute[] Layout =
		[
			new ( VertexAttributeType.Position, VertexAttributeFormat.Float32, 3 ),
			new ( VertexAttributeType.Normal, VertexAttributeFormat.Float32, 3 ),
			new ( VertexAttributeType.Tangent, VertexAttributeFormat.Float32, 4 ),
			new ( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2 ),
			new ( VertexAttributeType.BlendIndices, VertexAttributeFormat.UInt8, 4 ),
			new ( VertexAttributeType.BlendWeights, VertexAttributeFormat.UInt8, 4 )
		];
	}
}
