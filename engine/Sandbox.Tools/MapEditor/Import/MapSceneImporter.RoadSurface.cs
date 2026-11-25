using System;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	// Hammer samples the highest surface under four offsets, including neighboring tiles at a grid boundary.
	sealed class RoadSurface( SourceNode[] cells, int width, int height )
	{
		readonly Dictionary<int, (Triangle[] Faces, bool HasSlope)> cached = [];

		public bool AllowsRoad( int sx, int sy, Vector2 point )
		{
			Span<int> touching = stackalloc int[4];
			int count = 0;
			bool hasSlope = false;
			for ( int y = Math.Max( 0, (sy - 1) / 8 ); y <= Math.Min( height - 1, sy / 8 ); y++ )
				for ( int x = Math.Max( 0, (sx - 1) / 8 ); x <= Math.Min( width - 1, sx / 8 ); x++ )
				{
					int index = y * width + x;
					if ( cells[index] is null ) continue;
					if ( !cached.TryGetValue( index, out var tile ) ) cached[index] = tile = ReadTile( cells[index] );
					touching[count++] = index;
					hasSlope |= tile.HasSlope;
				}
			if ( !hasSlope ) return true;

			for ( int i = 0; i < 4; i++ )
			{
				var p = point + new Vector2( (i & 2) == 0 ? 10 : -10, (i & 1) == 0 ? 10 : -10 );
				float highest = -16384;
				bool steep = false;
				foreach ( var index in touching[..count] )
					foreach ( var face in cached[index].Faces )
					{
						Vector2 ab = face.B - face.A, ac = face.C - face.A, ap = p - (Vector2)face.A;
						float det = Cross( ab, ac );
						if ( det == 0 ) continue;
						float u = Cross( ap, ac ) / det, v = Cross( ab, ap ) / det;
						if ( u < 0 || v < 0 || u + v > 1 ) continue;
						float z = face.A.z + u * (face.B.z - face.A.z) + v * (face.C.z - face.A.z);
						if ( z <= highest || z > 16384 ) continue;
						highest = z;
						steep = MathF.Abs( face.Normal.z ) < 0.707f;
					}
				if ( steep ) return false;
			}
			return true;
		}

		static (Triangle[], bool) ReadTile( SourceNode root )
		{
			var faces = new List<Triangle>();
			var active = new HashSet<SourceNode>();
			bool hasSlope = false;
			void Visit( SourceNode node, Transform placement )
			{
				if ( !active.Add( node ) ) throw new System.IO.InvalidDataException( $"Recursive Hammer tile at '{node.Name}'." );
				var transform = placement.ToWorld( node.Transform );
				foreach ( var part in node.Meshes )
					foreach ( var handle in part.Mesh.FaceHandles )
					{
						var points = part.Mesh.GetFaceVertexPositions( handle, transform ).ToArray();
						var indices = PolygonTriangulation.TriangulateFace( points );
						for ( int i = 0; i < indices.Length; i += 3 )
						{
							var face = new Triangle( points[indices[i]], points[indices[i + 1]], points[indices[i + 2]] );
							float z = MathF.Abs( face.Normal.z );
							if ( z == 0 ) continue;
							faces.Add( face );
							hasSlope |= z < 0.707f;
						}
					}
				if ( node.Target is { } target ) Visit( target, PlacementDelta( transform, target ) );
				foreach ( var child in node.Children ) Visit( child, placement );
				active.Remove( node );
			}
			Visit( root, Transform.Zero );
			return (faces.ToArray(), hasSlope);
		}
	}
}
