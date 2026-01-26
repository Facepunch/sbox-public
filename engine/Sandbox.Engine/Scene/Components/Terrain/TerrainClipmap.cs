using System.Buffers;
using System.Runtime.InteropServices;

namespace Sandbox;

internal static class TerrainClipmap
{
	[StructLayout( LayoutKind.Sequential )]
	public struct PosAndLodVertex
	{
		public PosAndLodVertex( Vector3 position )
		{
			this.position = position;
		}

		[VertexLayout.Position]
		public Vector3 position;
	}

	public static Mesh GenerateMesh( int LodLevels, int LodExtentTexels, Material material )
	{
		var vertices = new List<PosAndLodVertex>( 32 );
		var indices = new List<int>();

		// Loop through each LOD level
		for ( int level = 0; level < LodLevels; level++ )
		{
			int step = 1 << level;
			int prevStep = Math.Max( 0, 1 << (level - 1) );

			int g = LodExtentTexels / 2;

			int pad = 1;
			int radius = step * (g + pad);
			int innerRadius = (g * prevStep) - prevStep; // Overlap by one step

			for ( int y = -radius; y < radius; y += step )
			{
				for ( int x = -radius; x < radius; x += step )
				{
					if ( Math.Max( Math.Abs( x + prevStep ), Math.Abs( y + prevStep ) ) < innerRadius )
						continue;

					vertices.Add( new PosAndLodVertex( new Vector3( x, y, level ) ) );
					vertices.Add( new PosAndLodVertex( new Vector3( x + step, y, level ) ) );
					vertices.Add( new PosAndLodVertex( new Vector3( x + step, y + step, level ) ) );
					vertices.Add( new PosAndLodVertex( new Vector3( x, y + step, level ) ) );

					indices.Add( vertices.Count - 4 );
					indices.Add( vertices.Count - 3 );
					indices.Add( vertices.Count - 2 );
					indices.Add( vertices.Count - 2 );
					indices.Add( vertices.Count - 1 );
					indices.Add( vertices.Count - 4 );
				}
			}
		}

		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer( vertices.Count, vertices );
		mesh.CreateIndexBuffer( indices.Count, indices );
		return mesh;
	}

	/// <summary>
	/// Diamond-square implementation trying to reduce duplicate vertices.
	/// </summary>
	public static Mesh GenerateMesh_DiamondSquare( int LodLevels, int LodExtentTexels, Material material, int subdivisionFactor = 1, int subdivisionLodCount = 3 )
	{
		var total = LodLevels * 36 * (LodExtentTexels / 2 + 1) * (LodExtentTexels / 2 + 1) * subdivisionFactor * subdivisionFactor;

		var vertexMap = new Dictionary<(float x, float y, int lod), int>( total );
		var vertices = new List<PosAndLodVertex>( total );
		var indices = new List<int>( total * 3 );

		int GetOrAddVertex( float x, float y, int level )
		{
			var key = (x, y, level);
			if ( !vertexMap.TryGetValue( key, out int index ) )
			{
				index = vertices.Count;
				vertices.Add( new PosAndLodVertex( new Vector3( x, y, level ) ) );
				vertexMap[key] = index;
			}
			return index;
		}

		// Loop through each LOD level
		for ( int level = 0; level < LodLevels; level++ )
		{
			int lodBaseStep = 1 << level;

			// We only subdivise LOD levels athat are < than subDivisionLodCount
			int currentSubdivision = level < subdivisionLodCount ? subdivisionFactor : 1;
			float step = (float)lodBaseStep / currentSubdivision;

			int g = LodExtentTexels / 2;
			int pad = 1;

			int radius = lodBaseStep * (g + pad);
			int prevLodBaseStep = level > 0 ? (1 << (level - 1)) : 0;
			int innerRadius = (prevLodBaseStep * g) - prevLodBaseStep; // Overlap by one step

			for ( float y = -radius; y < radius; y += step )
			{
				for ( float x = -radius; x < radius; x += step )
				{
					if ( Math.Max( Math.Abs( x ), Math.Abs( y ) ) < innerRadius )
						continue;

					//   A-----B-----C
					//   | \   |   / |
					//   |   \ | /   |
					//   D-----E-----F
					//   |   / | \   |
					//   | /   |   \ |
					//   G-----H-----I

					float halfStep = step * 0.5f;
					int idxA = GetOrAddVertex( x, y, level );
					int idxB = GetOrAddVertex( x + halfStep, y, level );
					int idxC = GetOrAddVertex( x + step, y, level );
					int idxD = GetOrAddVertex( x, y + halfStep, level );
					int idxE = GetOrAddVertex( x + halfStep, y + halfStep, level );
					int idxF = GetOrAddVertex( x + step, y + halfStep, level );
					int idxG = GetOrAddVertex( x, y + step, level );
					int idxH = GetOrAddVertex( x + halfStep, y + step, level );
					int idxI = GetOrAddVertex( x + step, y + step, level );

					// Stitch the border into the next level
					if ( x == -radius )
					{
						// E G A
						indices.Add( idxE );
						indices.Add( idxG );
						indices.Add( idxA );
					}
					else
					{
						// E D A
						indices.Add( idxE );
						indices.Add( idxD );
						indices.Add( idxA );
						// E G D
						indices.Add( idxE );
						indices.Add( idxG );
						indices.Add( idxD );
					}

					if ( y == radius - step )
					{
						// E I G
						indices.Add( idxE );
						indices.Add( idxI );
						indices.Add( idxG );
					}
					else
					{
						// E H G
						indices.Add( idxE );
						indices.Add( idxH );
						indices.Add( idxG );
						// E I H
						indices.Add( idxE );
						indices.Add( idxI );
						indices.Add( idxH );
					}

					if ( x == radius - step )
					{
						// E C I
						indices.Add( idxE );
						indices.Add( idxC );
						indices.Add( idxI );
					}
					else
					{
						// E F I
						indices.Add( idxE );
						indices.Add( idxF );
						indices.Add( idxI );
						// E C F
						indices.Add( idxE );
						indices.Add( idxC );
						indices.Add( idxF );
					}

					if ( y == -radius )
					{
						// E A C
						indices.Add( idxE );
						indices.Add( idxA );
						indices.Add( idxC );
					}
					else
					{
						// E B C
						indices.Add( idxE );
						indices.Add( idxB );
						indices.Add( idxC );
						// E A B
						indices.Add( idxE );
						indices.Add( idxA );
						indices.Add( idxB );
					}
				}
			}
		}

		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer( vertices.Count, vertices );
		mesh.CreateIndexBuffer( indices.Count, indices );

		return mesh;
	}
}
