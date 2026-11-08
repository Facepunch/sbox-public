using System;
using System.Runtime.InteropServices;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceModelBuilder
{
	internal Model CreateStaticPropModel( (MdlFile Mdl, VtxFile Vtx, PhyFile Physics) source,
		string contentRoot, int index, uint flags, bool hdr, bool diskLighting, Transform transform,
		Vector3? lightingOrigin, Func<Vector3, PropLightingState> fallback, SourceMaterialOverrides shadows, Transform[] referencePose,
		Dictionary<Material, Material> vertexLitMaterials )
	{
		if ( source.Vtx is null ) return null;
		var lighting = new StaticPropLighting( $"source1/{Host.Ident}/{contentRoot}prop_{index}.vmdl", contentRoot, source.Vtx, vertexLitMaterials )
		{
			BakedShadowBindings = shadows
		};
		var vertexLit = source.Mdl.IsStaticProp && !new MaterialSlots( this, source.Mdl, source.Vtx, lighting ).UsesBumpMapping();
		if ( vertexLit && diskLighting && (flags & 0x40) == 0 )
			lighting.ReadVertices( Host, hdr ? $"sp_hdr_{index}.vhv" : $"sp_{index}.vhv", source.Mdl.Checksum );
		if ( vertexLit && diskLighting && (flags & 0x100) == 0 )
		{
			if ( !hdr || !lighting.ReadTexels( Host, $"texelslighting_{index}_hdr.ppl", source.Mdl.Checksum ) )
				lighting.ReadTexels( Host, $"texelslighting_{index}.ppl", source.Mdl.Checksum );
		}
		if ( !lighting.HasLighting )
		{
			var point = source.Mdl.IlluminationPosition;
			if ( source.Mdl.IlluminationAttachment > 0 )
			{
				var attachment = source.Mdl.Attachments.ElementAt( source.Mdl.IlluminationAttachment - 1 );
				if ( (uint)attachment.LocalBone >= source.Mdl.Bones.Length )
					throw new System.IO.InvalidDataException( "MDL illumination attachment references an invalid bone." );
				point = GetAttachmentTransform( attachment.Local ).PointToWorld( point );
				point = GetAttachmentTransform( source.Mdl.Bones[attachment.LocalBone].PoseToBone ).PointToLocal( point );
			}
			lighting.SetFallback( fallback( lightingOrigin ?? transform.PointToWorld( point ) ),
				transform, source.Mdl.ConstantDirectionalLightDot, vertexLit );
		}
		return BuildModel( source.Mdl, source.Vtx, source.Physics, lighting.Name, referencePose, lighting );
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct StaticPropVertex( SkinnedVertex vertex, Vector3 lighting )
	{
		[VertexLayout.Position]
		public Vector3 Position = vertex.position;
		[VertexLayout.Normal]
		public Vector3 Normal = vertex.normal;
		[VertexLayout.Tangent]
		public Vector4 Tangent = vertex.tangent;
		[VertexLayout.TexCoord]
		public Vector2 TexCoord = vertex.texcoord;
		[VertexLayout.BlendIndices]
		public Color32 BlendIndices = vertex.blendIndices;
		[VertexLayout.BlendWeight]
		public Color32 BlendWeights = vertex.blendWeights;
		[VertexLayout.Color]
		public Color Color = Color.White;
		[VertexLayout.TexCoord( 5 )]
		public Vector3 Lighting = lighting;
	}
}
