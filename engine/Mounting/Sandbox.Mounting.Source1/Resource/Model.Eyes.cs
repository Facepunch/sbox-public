using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceModelBuilder
{
	private static EyeVertex[] CreateEyeVertices( ReadOnlySpan<SkinnedVertex> vertices, MdlFile.StudioEyeball eye,
		MdlFile.Matrix3x4 poseToBone, VertexTransform[] vertexTransforms = null )
	{
		var (u, v) = GetEyeProjection( eye, poseToBone );
		var result = GC.AllocateUninitializedArray<EyeVertex>( vertices.Length );
		for ( var i = 0; i < result.Length; i++ )
		{
			var position = new Vector4( vertices[i].position, 1 );
			var uv = new Vector2( Vector4.Dot( position, u ), Vector4.Dot( position, v ) );
			if ( !float.IsFinite( uv.x ) || !float.IsFinite( uv.y ) )
				throw new InvalidDataException( "Invalid MDL iris coordinates." );
			var vertex = vertices[i];
			if ( vertexTransforms is not null )
				vertex = TransformVertex( vertex, vertexTransforms[i] );
			result[i] = new EyeVertex( vertex, uv );
		}
		return result;
	}

	internal static (Vector4 U, Vector4 V) GetEyeProjection( MdlFile.StudioEyeball eye, MdlFile.Matrix3x4 poseToBone )
	{
		var forward = -eye.Forward.Normal;
		var up = eye.Up.Normal;
		var right = Vector3.Cross( forward, up ).Normal;
		forward = (forward + 2 * eye.ZOffset * right).Normal;
		right = Vector3.Cross( forward, up ).Normal;
		up = Vector3.Cross( right, forward ).Normal;
		if ( !float.IsFinite( right.LengthSquared ) || !float.IsFinite( up.LengthSquared )
			|| right.LengthSquared < 0.5f || up.LengthSquared < 0.5f )
			throw new InvalidDataException( "Invalid MDL eyeball orientation." );

		return (Projection( -right * eye.IrisScale ), Projection( -up * eye.IrisScale ));

		Vector4 Projection( Vector3 axis ) => new(
			axis.x * poseToBone.M11 + axis.y * poseToBone.M21 + axis.z * poseToBone.M31,
			axis.x * poseToBone.M12 + axis.y * poseToBone.M22 + axis.z * poseToBone.M32,
			axis.x * poseToBone.M13 + axis.y * poseToBone.M23 + axis.z * poseToBone.M33,
			0.5f + axis.x * poseToBone.M14 + axis.y * poseToBone.M24 + axis.z * poseToBone.M34 - Vector3.Dot( axis, eye.Origin ) );
	}

	// Only eye meshes need a projected iris UV; other models retain their compact vertex layout.
	[StructLayout( LayoutKind.Sequential )]
	private struct EyeVertex( SkinnedVertex vertex, Vector2 irisUv )
	{
		[VertexLayout.Position] public Vector3 position = vertex.position;
		[VertexLayout.Normal] public Vector3 normal = vertex.normal;
		[VertexLayout.Tangent] public Vector4 tangent = vertex.tangent;
		[VertexLayout.TexCoord( 0 )] public Vector2 texcoord = vertex.texcoord;
		[VertexLayout.BlendIndices] public Color32 blendIndices = vertex.blendIndices;
		[VertexLayout.BlendWeight] public Color32 blendWeights = vertex.blendWeights;
		[VertexLayout.TexCoord( 1 )] public Vector2 irisTexcoord = irisUv;
	}
}
