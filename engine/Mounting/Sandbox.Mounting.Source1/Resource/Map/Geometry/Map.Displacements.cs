using System;
using System.Collections.Generic;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	void BuildDisplacement( BspFile bsp, ref BspFace face, ref FaceSurface surface, FaceProjection projection, Vector3 tangent, MeshGroup group )
	{
		ref var disp = ref bsp.DispInfos[face.DispInfo];
		int power = disp.Power;
		int size = (1 << power) + 1;
		Span<Vector3> corners = stackalloc Vector3[4];
		GetDisplacementCorners( bsp, face, corners );
		int vertBase = group.Vertices.Count;
		int vertStart = disp.DispVertStart;
		for ( int y = 0; y < size; y++ )
		{
			float ty = y / (float)(size - 1);
			for ( int x = 0; x < size; x++ )
			{
				float tx = x / (float)(size - 1);
				var basePos = DisplacementBasePosition( corners, tx, ty );
				var uv = projection.TextureUV( basePos );
				int vertIdx = vertStart + x + y * size;
				ref var dv = ref bsp.DispVerts[vertIdx];
				basePos += dv.Vector * dv.Dist;
				float alpha = Math.Clamp( dv.Alpha / 255.0f, 0.0f, 1.0f );
				var lmUV = Vector2.Zero;
				if ( surface.Width > 0 && surface.Height > 0 )
				{
					float lmU = (tx * (surface.Width - 1) + 0.5f + surface.OffsetX) * surface.InverseAtlasSize.x;
					float lmV = (ty * (surface.Height - 1) + 0.5f + surface.OffsetY) * surface.InverseAtlasSize.y;
					lmUV = new Vector2( lmU, lmV );
				}

				group.Vertices.Add( new BlendLightmapVertex( basePos, Vector3.Up, tangent, uv, lmUV, alpha ) );
			}
		}

		for ( int y = 0; y < size; y++ )
		{
			for ( int x = 0; x < size; x++ )
			{
				int idx = vertBase + x + y * size;
				var accumNormal = Vector3.Zero;
				bool hasLeft = x > 0;
				bool hasUp = y < size - 1;
				bool hasRight = x < size - 1;
				bool hasDown = y > 0;
				var center = group.Vertices[idx].Position;
				if ( hasUp && hasRight )
				{
					var pU = group.Vertices[vertBase + x + (y + 1) * size].Position;
					var pR = group.Vertices[vertBase + (x + 1) + y * size].Position;
					var pUR = group.Vertices[vertBase + (x + 1) + (y + 1) * size].Position;
					var n = Vector3.Cross( pR - center, pU - center ).Normal;
					accumNormal += n;
					n = Vector3.Cross( pUR - pR, pU - pR ).Normal;
					accumNormal += n;
				}

				if ( hasLeft && hasUp )
				{
					var pL = group.Vertices[vertBase + (x - 1) + y * size].Position;
					var pU = group.Vertices[vertBase + x + (y + 1) * size].Position;
					var pUL = group.Vertices[vertBase + (x - 1) + (y + 1) * size].Position;
					var n = Vector3.Cross( center - pL, pUL - pL ).Normal;
					accumNormal += n;
					n = Vector3.Cross( pU - center, pUL - center ).Normal;
					accumNormal += n;
				}

				if ( hasLeft && hasDown )
				{
					var pL = group.Vertices[vertBase + (x - 1) + y * size].Position;
					var pD = group.Vertices[vertBase + x + (y - 1) * size].Position;
					var pDL = group.Vertices[vertBase + (x - 1) + (y - 1) * size].Position;
					var n = Vector3.Cross( pD - pDL, pL - pDL ).Normal;
					accumNormal += n;
					n = Vector3.Cross( center - pD, pL - pD ).Normal;
					accumNormal += n;
				}

				if ( hasRight && hasDown )
				{
					var pR = group.Vertices[vertBase + (x + 1) + y * size].Position;
					var pD = group.Vertices[vertBase + x + (y - 1) * size].Position;
					var pDR = group.Vertices[vertBase + (x + 1) + (y - 1) * size].Position;
					var n = Vector3.Cross( pDR - pD, center - pD ).Normal;
					accumNormal += n;
					n = Vector3.Cross( pR - pDR, center - pDR ).Normal;
					accumNormal += n;
				}

				var normal = accumNormal.Normal;
				if ( normal.LengthSquared < 0.001f )
					normal = bsp.Planes[face.PlaneNum].Normal;
				var t = (tangent - normal * Vector3.Dot( normal, tangent )).Normal;
				if ( t.LengthSquared < 0.001f )
					t = tangent;
				var v = group.Vertices[idx];
				v.Normal = normal;
				v.Tangent = new Vector4( t.x, t.y, t.z, -1.0f );
				group.Vertices[idx] = v;
			}
		}

		AddDisplacementIndices( group.Indices, vertBase, size, DisplacementTriangleTags( bsp, disp ) );
	}
}
