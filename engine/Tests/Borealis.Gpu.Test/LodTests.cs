using Borealis.Backend;
using Borealis.Backend.Lods;
using Borealis.Backend.Meshlets;
using Borealis.Source2;
using System.IO;
using V3 = System.Numerics.Vector3;

namespace BorealisGpuTests;

/// <summary>
/// Generated LODs (docs/managed/borealis-gpu-driven.md, "Generated LODs"): what <see cref="LodBuilder"/> makes of a range
/// with meshoptimizer's simplifier.
/// </summary>
[TestClass]
public class LodTests
{
	static void UseEngineLibrary()
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault();
		if ( core is not null ) MeshOptimizer.Directory = EngineFolders.NativeIn( Path.GetDirectoryName( core ) );
		if ( !MeshOptimizer.Available ) Assert.Inconclusive( "meshopt isn't built" );
	}

	/// <summary>A grid of quads, two triangles each, its vertices from <paramref name="first"/>, at heights from <paramref name="height"/>.</summary>
	static (uint[] Indices, V3[] Positions) Grid( int side, uint first, Func<int, int, float> height )
	{
		var positions = new V3[(side + 1) * (side + 1)];
		for ( int y = 0; y <= side; y++ )
			for ( int x = 0; x <= side; x++ ) positions[y * (side + 1) + x] = new V3( x, y, height( x, y ) );

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
	/// Each level has at most the config's share of the last's triangles, reuses the range's vertices, keeps every border
	/// vertex (so a neighbouring range stays joined), and errors only grow down the chain.
	/// </summary>
	[TestMethod]
	public void LevelsShrinkKeepBordersAndGrowInError()
	{
		UseEngineLibrary();
		const uint First = 1000;
		const int BaseVertex = 5;
		const int Side = 40;
		var (indices, positions) = Grid( Side, First, ( x, y ) => MathF.Sin( x * 0.3f ) * MathF.Cos( y * 0.2f ) * 2 );
		V3 Position( int vertex ) => positions[vertex - BaseVertex - (int)First];

		var config = LodConfig.Default;
		var chain = LodBuilder.Build( indices, Position, BaseVertex, config );
		Assert.IsNotNull( chain );
		Assert.IsTrue( chain.Levels.Length is > 0 and <= LodStore.MaxLevels );
		Assert.AreEqual( chain.Levels.Length, chain.Errors.Length );

		var border = new HashSet<uint>();
		for ( int i = 0; i <= Side; i++ )
		{
			border.Add( First + (uint)i );
			border.Add( First + (uint)(Side * (Side + 1) + i) );
			border.Add( First + (uint)(i * (Side + 1)) );
			border.Add( First + (uint)(i * (Side + 1) + Side) );
		}

		var last = indices.Length;
		var error = 0f;
		foreach ( var (level, k) in chain.Levels.Select( ( l, k ) => (l, k) ) )
		{
			Assert.AreEqual( 0, level.Length % 3 );
			Assert.IsTrue( level.Length <= last * config.KeepRatio, $"level {k + 1}: {level.Length} indices after {last}" );
			Assert.IsTrue( level.All( i => i >= First && i < First + positions.Length ), $"level {k + 1} names a vertex outside the range" );
			Assert.IsTrue( border.IsSubsetOf( level ), $"level {k + 1} lost border vertices" );
			Assert.IsTrue( chain.Errors[k] >= error && float.IsFinite( chain.Errors[k] ), $"level {k + 1}'s error {chain.Errors[k]} after {error}" );
			(last, error) = (level.Length, chain.Errors[k]);
		}
	}

	/// <summary>A flat range simplifies without error; one under the config's triangles gets no levels.</summary>
	[TestMethod]
	public void FlatRangesCostNothingAndSmallOnesAreLeft()
	{
		UseEngineLibrary();
		var (indices, positions) = Grid( 20, 0, ( _, _ ) => 0 );
		var chain = LodBuilder.Build( indices, v => positions[v], 0, LodConfig.Default );
		Assert.IsNotNull( chain );
		Assert.IsTrue( chain.Errors.All( e => e < 1e-4f ), string.Join( ", ", chain.Errors ) );

		var (small, smallPositions) = Grid( 5, 0, ( x, y ) => x * y );
		Assert.IsNull( LodBuilder.Build( small, v => smallPositions[v], 0, LodConfig.Default ) );
	}
}
