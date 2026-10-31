using System;
using System.IO;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

internal partial class BspFile
{
	internal readonly Dictionary<int, LightingDisplacement> decalDisplacements = [];
	internal bool TraceDecal( Vector3 start, Vector3 delta, out Vector3 position, out Vector3 normal, bool skybox = false )
	{
		position = default;
		normal = default;
		if ( Models.Length == 0 )
			return false;
		var closest = 1f;
		var found = false;
		var hitNormal = Vector3.Zero;
		var model = Models[0];
		for ( var index = model.FirstFace; index < model.FirstFace + model.FaceCount; index++ )
		{
			var face = Faces[index];
			if ( (skybox ? !IsActiveSkyboxFace( index ) : IsSkyboxFace( index )) || face.TexInfo < 0 || face.NumEdges < 3 || (TexInfos[face.TexInfo].Flags & (0x2000 | 0x80 | 6)) != 0 )
				continue;
			var faceNormal = Planes[face.PlaneNum].Normal;
			if ( Vector3.Dot( faceNormal, delta ) >= 0 )
				continue;
			if ( face.DispInfo >= 0 )
			{
				if ( !decalDisplacements.TryGetValue( index, out var geometry ) )
				{
					var disp = DispInfos[face.DispInfo];
					var size = (1 << disp.Power) + 1;
					var corners = new Vector3[4];
					GetDisplacementCorners( this, face, corners );
					var vertices = new Vector3[size * size];
					for ( var y = 0; y < size; y++ )
						for ( var x = 0; x < size; x++ )
						{
							var offset = DispVerts[disp.DispVertStart + y * size + x];
							vertices[y * size + x] = DisplacementBasePosition( corners, x / (float)(size - 1), y / (float)(size - 1) ) + offset.Vector * offset.Dist;
						}

					var indices = new List<int>();
					AddDisplacementIndices( indices, 0, size, DisplacementTriangleTags( this, disp ) );
					var bounds = new BBox( vertices[0], vertices[0] );
					foreach ( var vertex in vertices )
						bounds = bounds.AddPoint( vertex );
					geometry = new( bounds, vertices, [], indices.ToArray(), index );
					decalDisplacements.Add( index, geometry );
				}

				if ( !Intersects( geometry.Bounds, start, delta, closest ) )
					continue;
				for ( var triangle = 0; triangle < geometry.Indices.Length; triangle += 3 )
					Test( geometry.Positions[geometry.Indices[triangle]], geometry.Positions[geometry.Indices[triangle + 1]], geometry.Positions[geometry.Indices[triangle + 2]], faceNormal );
			}
			else
			{
				var first = Vertex( 0 );
				for ( var edge = 1; edge + 1 < face.NumEdges; edge++ )
					Test( first, Vertex( edge ), Vertex( edge + 1 ), faceNormal );
			}

			Vector3 Vertex( int edge )
			{
				var surfEdge = SurfEdges[face.FirstEdge + edge];
				return Vertices[surfEdge >= 0 ? Edges[surfEdge].V0 : Edges[-surfEdge].V1];
			}
		}

		if ( !found )
			return false;
		position = start + delta * closest;
		normal = hitNormal;
		return true;
		void Test( Vector3 a, Vector3 b, Vector3 c, Vector3 faceNormal )
		{
			if ( !Triangle( start, delta, a, b, c, out var fraction, out _ ) || fraction >= closest )
				return;
			closest = fraction;
			hitNormal = Vector3.Cross( b - a, c - a ).Normal;
			if ( Vector3.Dot( hitNormal, faceNormal ) < 0 )
				hitNormal = -hitNormal;
			found = true;
		}
	}
}
