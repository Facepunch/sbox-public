using System.Numerics;

namespace Nav.Lib.Tests;

/// <summary>
/// For tests that make tiles by hand: polygons as Poly values, the way a tile holds them, instead of the
/// flat arrays the builder's last stage leaves.
/// </summary>
static class TileAssemblerExtensions
{
	public static Tile? Create( this TileAssembler assembler, int x, int y, Vector3[] verts, Poly[] polys, in NavSettings settings, out string? error )
	{
		var polyVerts = new ushort[polys.Length * Poly.MaxVerts];
		var neighbours = new ushort[polys.Length * Poly.MaxVerts];
		var areas = new byte[polys.Length];
		Array.Fill( polyVerts, Poly.Wall );
		Array.Fill( neighbours, Poly.Wall );
		for ( int p = 0; p < polys.Length; p++ )
		{
			for ( int j = 0; j < polys[p].VertCount; j++ )
			{
				polyVerts[p * Poly.MaxVerts + j] = polys[p].Verts[j];
				neighbours[p * Poly.MaxVerts + j] = polys[p].Neis[j];
			}
			areas[p] = polys[p].Area;
		}
		return assembler.Create( x, y, verts, polyVerts, neighbours, areas, settings, out error );
	}
}
