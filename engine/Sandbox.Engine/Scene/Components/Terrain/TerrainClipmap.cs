using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// Geometry for the terrain clipmap. One small square block mesh is instanced as "meshlets" sitting in
/// nested square LOD rings around the camera; each ring outward uses cells twice as large, and outer rings
/// are hollow where the finer ring already sits. Placing and camera-snapping the meshlets happens in the
/// vertex shader (TerrainClipmap.hlsl); this file is all integer cell math.
/// </summary>
internal static class TerrainClipmap
{
	[StructLayout( LayoutKind.Sequential )]
	public struct PosVertex
	{
		[VertexLayout.Position]
		public Vector3 position; // xy = block-local coord in [0, BlockSize]

		public PosVertex( Vector3 position ) => this.position = position;
	}

	[StructLayout( LayoutKind.Sequential )]
	public struct Meshlet
	{
		public Vector2 BlockOffset;
		public int Level;
		public uint Pad;
	}

	// The one mesh every meshlet instances: a blockSize x blockSize grid of quads over [0, blockSize].
	// density subdivides each cell further.
	public static Mesh BuildBlockMesh( int blockSize, int density, Material material )
	{
		density = Math.Max( 1, density );
		int quads = blockSize * density;
		int side = quads + 1;
		float step = 1.0f / density;

		var vertices = new List<PosVertex>( side * side );
		for ( int y = 0; y <= quads; y++ )
			for ( int x = 0; x <= quads; x++ )
				vertices.Add( new PosVertex( new Vector3( x * step, y * step, 0 ) ) );

		var indices = new List<int>( quads * quads * 6 );
		int Idx( int x, int y ) => y * side + x;

		for ( int y = 0; y < quads; y++ )
		{
			for ( int x = 0; x < quads; x++ )
			{
				int a = Idx( x, y ), b = Idx( x + 1, y ), c = Idx( x + 1, y + 1 ), d = Idx( x, y + 1 );
				indices.Add( a ); indices.Add( b ); indices.Add( c );
				indices.Add( c ); indices.Add( d ); indices.Add( a );
			}
		}

		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer( vertices.Count, vertices );
		mesh.CreateIndexBuffer( indices.Count, indices );
		return mesh;
	}

	public static void GetMorphBand( int extentCells, int blockSize, out float startCells, out float endCells )
	{
		float halfExtent = extentCells / 2.0f;
		endCells = Math.Clamp( halfExtent - 3.0f * blockSize, 1.0f, halfExtent );
		startCells = Math.Clamp( halfExtent - 4.0f * blockSize, 0.0f, endCells - 1.0f );
	}

	// One meshlet per block, for every LOD ring. Each ring is the same blocksPerSide x blocksPerSide grid of
	// blocks centred on the origin; outer rings skip the middle that the finer ring covers.
	public static Meshlet[] BuildLayout( int levels, int extentCells, int blockSize )
	{
		int blocksPerSide = extentCells / blockSize;
		int halfExtent = extentCells / 2;

		// The finer ring covers the central quarter; we keep one block of that overlapping so neighbouring
		// rings share a seam the shader can blend.
		int holeRadius = Math.Max( 0, extentCells / 4 - blockSize );

		var meshlets = new List<Meshlet>( levels * blocksPerSide * blocksPerSide );

		for ( int level = 0; level < levels; level++ )
		{
			for ( int by = 0; by < blocksPerSide; by++ )
			{
				for ( int bx = 0; bx < blocksPerSide; bx++ )
				{
					int cellX = bx * blockSize - halfExtent; // block's lower corner, in cells from the centre
					int cellY = by * blockSize - halfExtent;

					bool coveredByFinerRing =
						cellX >= -holeRadius && cellX + blockSize <= holeRadius &&
						cellY >= -holeRadius && cellY + blockSize <= holeRadius;

					if ( level > 0 && coveredByFinerRing )
						continue;

					meshlets.Add( new Meshlet { BlockOffset = new Vector2( cellX, cellY ), Level = level } );
				}
			}
		}

		return [.. meshlets];
	}
}
