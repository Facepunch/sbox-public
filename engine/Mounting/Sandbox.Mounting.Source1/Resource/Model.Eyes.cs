using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceModelBuilder
{
	private static EyeVertex[] CreateEyeVertices( ReadOnlySpan<SkinnedVertex> vertices, MdlFile.StudioEyeball eye,
		MdlFile.Matrix3x4 poseToBone, bool sourceLighting, VertexTransform[] vertexTransforms = null )
	{
		// Bake stored-forward neutral gaze, not the runtime target-dependent zoffset adjustment.
		var forward = eye.Forward.Normal;
		var up = eye.Up.Normal;
		var right = Vector3.Cross( forward, up ).Normal;
		up = Vector3.Cross( right, forward ).Normal;
		if ( !float.IsFinite( right.LengthSquared ) || !float.IsFinite( up.LengthSquared )
			|| right.LengthSquared < 0.5f || up.LengthSquared < 0.5f )
			throw new System.IO.InvalidDataException( "Invalid MDL eyeball orientation." );

		var u = Projection( -right * eye.IrisScale );
		var v = Projection( -up * eye.IrisScale );
		var result = GC.AllocateUninitializedArray<EyeVertex>( vertices.Length );
		for ( var i = 0; i < result.Length; i++ )
		{
			var position = new Vector4( vertices[i].position, 1 );
			var uv = new Vector2( Vector4.Dot( position, u ), Vector4.Dot( position, v ) );
			if ( !float.IsFinite( uv.x ) || !float.IsFinite( uv.y ) )
				throw new System.IO.InvalidDataException( "Invalid MDL iris coordinates." );
			var vertex = vertices[i];
			if ( sourceLighting )
			{
				var local = TransformToBone( vertex.position, poseToBone ) - eye.Origin;
				var normal = local - 0.5f * Vector3.Dot( local, eye.Up ) * eye.Up;
				normal = new Vector3(
					poseToBone.M11 * normal.x + poseToBone.M21 * normal.y + poseToBone.M31 * normal.z,
					poseToBone.M12 * normal.x + poseToBone.M22 * normal.y + poseToBone.M32 * normal.z,
					poseToBone.M13 * normal.x + poseToBone.M23 * normal.y + poseToBone.M33 * normal.z );
				if ( !float.IsFinite( normal.LengthSquared ) || normal.LengthSquared < 1e-12f )
					throw new InvalidDataException( "Invalid MDL eye lighting normal." );
				vertex.normal = normal.Normal;
			}
			if ( vertexTransforms is not null )
				vertex = TransformVertex( vertex, vertexTransforms[i] );
			result[i] = new EyeVertex( vertex, uv );
		}
		return result;

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
