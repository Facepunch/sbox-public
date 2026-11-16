using Borealis.Backend;
using Borealis.Backend.Meshlets;
using Borealis.Source2;
using System.IO;
using V3 = System.Numerics.Vector3;

namespace BorealisGpuTests;

/// <summary>
/// Meshlets built at load (docs/managed/borealis-gpu-driven.md, "Meshlets"): meshoptimizer's shared library from
/// <c>src/meshopt</c>, and what <see cref="MeshletBuilder"/> makes of a range.
/// </summary>
[TestClass]
public class MeshletTests
{
	static void UseEngineLibrary()
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault();
		if ( core is not null ) MeshOptimizer.Directory = EngineFolders.NativeIn( Path.GetDirectoryName( core ) );
		if ( !MeshOptimizer.Available ) Assert.Inconclusive( "meshopt isn't built" );
	}

	/// <summary>A grid of quads, two triangles each, its vertices from <paramref name="first"/>.</summary>
	static (uint[] Indices, V3[] Positions) Grid( int side, uint first )
	{
		var positions = new V3[(side + 1) * (side + 1)];
		for ( int y = 0; y <= side; y++ )
			for ( int x = 0; x <= side; x++ ) positions[y * (side + 1) + x] = new V3( x, y, (x * 7 + y * 3) % 5 * 0.1f );

		var indices = new List<uint>();
		for ( int y = 0; y < side; y++ )
		{
			for ( int x = 0; x < side; x++ )
			{
				var a = first + (uint)(y * (side + 1) + x);
				var b = a + 1;
				var c = a + (uint)(side + 1);
				var d = c + 1;
				indices.AddRange( [a, b, c, b, d, c] );
			}
		}

		return (indices.ToArray(), positions);
	}

	/// <summary>
	/// Every triangle lands in exactly one meshlet with its corners in their order (its first vertex is what the visibility
	/// ids name), no meshlet is over the config, runs tile the range, and each box holds its triangles' vertices.
	/// </summary>
	[TestMethod]
	public void MeshletsKeepEveryTriangleAndItsFirstVertex()
	{
		UseEngineLibrary();
		const uint First = 1000;
		const int BaseVertex = 5;
		var (indices, positions) = Grid( 40, First );
		V3 Position( int vertex ) => positions[vertex - BaseVertex - (int)First];

		foreach ( var config in new[] { MeshletConfig.Default, new MeshletConfig( 32, 32 ) } )
		{
			var set = MeshletBuilder.Build( indices, Position, BaseVertex, config );
			Assert.IsNotNull( set );
			Assert.AreEqual( indices.Length, set.Indices.Length );

			var expected = new Dictionary<(uint, uint, uint), int>();
			for ( int t = 0; t < indices.Length / 3; t++ ) expected[(indices[t * 3], indices[t * 3 + 1], indices[t * 3 + 2])] = expected.GetValueOrDefault( (indices[t * 3], indices[t * 3 + 1], indices[t * 3 + 2]) ) + 1;
			for ( int t = 0; t < set.Triangles; t++ )
			{
				var key = (set.Indices[t * 3], set.Indices[t * 3 + 1], set.Indices[t * 3 + 2]);
				Assert.IsTrue( expected.TryGetValue( key, out var left ) && left > 0, $"triangle {key} isn't one of the range's, corners in order" );
				expected[key] = left - 1;
			}

			var next = 0u;
			foreach ( var meshlet in set.Meshlets )
			{
				Assert.AreEqual( next, meshlet.FirstTriangle );
				Assert.IsTrue( meshlet.TriangleCount is > 0 && meshlet.TriangleCount <= (uint)config.MaxTriangles );
				var vertices = new HashSet<uint>();
				for ( uint t = meshlet.FirstTriangle; t < meshlet.FirstTriangle + meshlet.TriangleCount; t++ )
				{
					for ( int k = 0; k < 3; k++ )
					{
						var index = set.Indices[t * 3 + k];
						vertices.Add( index );
						var p = Position( BaseVertex + (int)index );
						var d = V3.Abs( p - meshlet.Center ) - meshlet.Extents;
						Assert.IsTrue( d.X <= 1e-4f && d.Y <= 1e-4f && d.Z <= 1e-4f, $"vertex {index} is outside its meshlet's box" );
					}
				}

				Assert.IsTrue( vertices.Count <= config.MaxVertices );
				next += meshlet.TriangleCount;
			}

			Assert.AreEqual( (uint)set.Triangles, next );
		}
	}

	[TestMethod]
	public void ConfigsParse()
	{
		Assert.AreEqual( new MeshletConfig( 32, 32 ), MeshletConfig.Parse( "32x32" ) );
		Assert.AreEqual( MeshletConfig.Default, MeshletConfig.Parse( null ) );
		Assert.AreEqual( MeshletConfig.Default, MeshletConfig.Parse( "64x125" ) );
	}
}
