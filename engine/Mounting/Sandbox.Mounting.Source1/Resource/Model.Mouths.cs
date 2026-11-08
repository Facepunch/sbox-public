using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceModelBuilder
{
	private static TeethVertex[] CreateTeethVertices( ReadOnlySpan<SkinnedVertex> vertices, MdlFile model,
		VertexTransform[] vertexTransforms = null, VertexTransform[] skinTransforms = null )
	{
		var forward = Vector3.Zero;
		var mouthBone = -1;
		if ( model.MouthCount > 0 )
		{
			var mouth = model.GetMouth( 0 );
			forward = GetMouthBindForward( mouth.Forward, model.Bones[mouth.Bone].PoseToBone );
			mouthBone = mouth.Bone;
		}
		if ( skinTransforms is not null && mouthBone >= 0 )
			forward = skinTransforms[mouthBone].Direction( forward );
		var result = GC.AllocateUninitializedArray<TeethVertex>( vertices.Length );
		for ( var index = 0; index < result.Length; index++ )
		{
			var vertex = vertices[index];
			if ( vertexTransforms is not null )
				vertex = TransformVertex( vertex, vertexTransforms[index] );
			result[index] = new TeethVertex( vertex, forward, mouthBone );
		}
		return result;
	}

	private static Vector3 GetMouthBindForward( Vector3 forward, MdlFile.Matrix3x4 m )
	{
		var determinant = m.M11 * (m.M22 * m.M33 - m.M23 * m.M32)
			- m.M12 * (m.M21 * m.M33 - m.M23 * m.M31)
			+ m.M13 * (m.M21 * m.M32 - m.M22 * m.M31);
		if ( !float.IsFinite( determinant ) || MathF.Abs( determinant ) < 1e-12f )
			throw new InvalidDataException( "MDL mouth bone has an invalid bind matrix." );
		// The renderer negates bone-local mouth forward; skinning matrices expect bind space.
		var result = -new Vector3(
			(m.M22 * m.M33 - m.M23 * m.M32) * forward.x + (m.M13 * m.M32 - m.M12 * m.M33) * forward.y + (m.M12 * m.M23 - m.M13 * m.M22) * forward.z,
			(m.M23 * m.M31 - m.M21 * m.M33) * forward.x + (m.M11 * m.M33 - m.M13 * m.M31) * forward.y + (m.M13 * m.M21 - m.M11 * m.M23) * forward.z,
			(m.M21 * m.M32 - m.M22 * m.M31) * forward.x + (m.M12 * m.M31 - m.M11 * m.M32) * forward.y + (m.M11 * m.M22 - m.M12 * m.M21) * forward.z ) / determinant;
		if ( !float.IsFinite( result.LengthSquared ) || result.LengthSquared < 1e-12f )
			throw new InvalidDataException( "MDL mouth has an invalid bind-space forward vector." );
		return result;
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct TeethVertex( SkinnedVertex vertex, Vector3 forward, int mouthBone )
	{
		[VertexLayout.Position] public Vector3 Position = vertex.position;
		[VertexLayout.Normal] public Vector3 Normal = vertex.normal;
		[VertexLayout.Tangent] public Vector4 Tangent = vertex.tangent;
		[VertexLayout.TexCoord] public Vector2 TexCoord = vertex.texcoord;
		[VertexLayout.BlendIndices] public Color32 BlendIndices = vertex.blendIndices;
		[VertexLayout.BlendWeight] public Color32 BlendWeights = vertex.blendWeights;
		// BlendIndices uses Color32/UNORM; keep the independently sampled bone index as a float.
		[VertexLayout.TexCoord( 4 )] public Vector4 MouthForward = new( forward, mouthBone );
	}
}
