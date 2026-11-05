using System.Collections.Generic;
using System.Runtime.InteropServices;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	void BuildFace( BspFile bsp, ref BspFace face, FaceProjection projection, Vector3 tangent, MeshGroup group, int vertNormalBase )
	{
		int vertBase = group.Vertices.Count;
		ref var plane = ref bsp.Planes[face.PlaneNum];
		var faceNormal = plane.Normal;
		bool hasVertNormals = bsp.VertNormalIndices.Length > 0;
		for ( int e = 0; e < face.NumEdges; e++ )
		{
			int surfEdge = bsp.SurfEdges[face.FirstEdge + e];
			Vector3 pos = surfEdge >= 0 ? bsp.Vertices[bsp.Edges[surfEdge].V0] : bsp.Vertices[bsp.Edges[-surfEdge].V1];
			var normal = faceNormal;
			if ( hasVertNormals )
				normal = bsp.VertNormals[bsp.VertNormalIndices[vertNormalBase + e]];
			var uv = projection.TextureUV( pos );
			var lmUV = projection.LightmapUV( pos );
			group.Vertices.Add( new BlendLightmapVertex( pos, normal, tangent, uv, lmUV ) );
		}

		for ( int e = 1; e < face.NumEdges - 1; e++ )
		{
			group.Indices.Add( vertBase );
			group.Indices.Add( vertBase + e + 1 );
			group.Indices.Add( vertBase + e );
		}
	}

	readonly struct FaceProjection
	{
		readonly Vector3 textureS, textureT, lightmapS, lightmapT;
		readonly float textureOffsetS, textureOffsetT, lightmapOffsetS, lightmapOffsetT;
		readonly Vector2 mappingSize;
		public FaceProjection( BspTexInfo info, Vector2 mappingSize, FaceSurface surface )
		{
			this.mappingSize = mappingSize;
			textureS = new Vector3( info.TextureVecsS.x, info.TextureVecsS.y, info.TextureVecsS.z );
			textureT = new Vector3( info.TextureVecsT.x, info.TextureVecsT.y, info.TextureVecsT.z );
			textureOffsetS = info.TextureVecsS.w;
			textureOffsetT = info.TextureVecsT.w;
			lightmapS = new Vector3( info.LightmapVecsS.x, info.LightmapVecsS.y, info.LightmapVecsS.z ) * surface.InverseAtlasSize.x;
			lightmapT = new Vector3( info.LightmapVecsT.x, info.LightmapVecsT.y, info.LightmapVecsT.z ) * surface.InverseAtlasSize.y;
			lightmapOffsetS = (info.LightmapVecsS.w - surface.LightmapMinsS + 0.5f + surface.OffsetX) * surface.InverseAtlasSize.x;
			lightmapOffsetT = (info.LightmapVecsT.w - surface.LightmapMinsT + 0.5f + surface.OffsetY) * surface.InverseAtlasSize.y;
		}

		public Vector4 TextureUV( Vector3 pos ) => new( (Vector3.Dot( pos, textureS ) + textureOffsetS) / mappingSize.x, (Vector3.Dot( pos, textureT ) + textureOffsetT) / mappingSize.y, 0, 0 );
		public Vector2 LightmapUV( Vector3 pos ) => new( Vector3.Dot( pos, lightmapS ) + lightmapOffsetS, Vector3.Dot( pos, lightmapT ) + lightmapOffsetT );
	}

	class MeshGroup
	{
		public readonly Material Material;
		public List<BlendLightmapVertex> Vertices = new();
		public List<int> Indices = new();
		public MeshGroup( Material material )
		{
			Material = material;
		}
	}

	[StructLayout( LayoutKind.Sequential )]
	struct BlendLightmapVertex
	{
		[VertexLayout.Position]
		public Vector3 Position;
		[VertexLayout.Color]
		public Color32 Color;
		[VertexLayout.Normal]
		public Vector3 Normal;
		[VertexLayout.TexCoord]
		public Vector4 TexCoord0;
		[VertexLayout.TexCoord]
		public Vector4 TexCoord1;
		[VertexLayout.Tangent]
		public Vector4 Tangent;
		[VertexLayout.TexCoord( 4 )]
		public Vector4 BlendValues;
		public BlendLightmapVertex( in Vector3 position, in Vector3 normal, in Vector3 tangent, in Vector4 texCoord0, in Vector2 lightmapUV, float blend = 0 ) : this()
		{
			Position = position;
			Normal = normal;
			Tangent = new Vector4( tangent.x, tangent.y, tangent.z, -1.0f );
			TexCoord0 = texCoord0;
			TexCoord1 = new Vector4( lightmapUV.x, lightmapUV.y, 0, 0 );
			Color = Color32.White;
			BlendValues = new Vector4( blend, 0, 0, 0 );
		}
	}
}
