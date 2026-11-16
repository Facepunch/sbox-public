using Borealis.Backend.Meshlets;
using V3 = System.Numerics.Vector3;

namespace Borealis.Backend.Lods;

/// <summary>
/// How an index range's coarser levels are made: each level about <see cref="Ratio"/> of the last's triangles, kept only
/// when it has at most <see cref="KeepRatio"/> of them, until <see cref="MaxLevels"/> or <see cref="MinTriangles"/>.
/// </summary>
internal readonly record struct LodConfig( int MaxLevels = 6, float Ratio = 0.5f, float KeepRatio = 0.8f, int MinTriangles = 16, int MinRangeTriangles = 64 )
{
	/// <summary>Up to 6 levels of half the last's triangles, from ranges of 64 or more.</summary>
	public static LodConfig Default => new( 6, 0.5f, 0.8f, 16, 64 );

	public override string ToString() => FormattableString.Invariant( $"{MaxLevels} levels at {Ratio}, kept under {KeepRatio}, from {MinRangeTriangles} triangles" );
}

/// <summary>
/// A range's coarser levels: each one's indices (the range's own index values, the same vertices) and its error, the most
/// any point of it can be from the range's surface, as a fraction of the range's bounds' half diagonal.
/// </summary>
internal sealed class LodChain
{
	public uint[][] Levels { get; init; }
	public float[] Errors { get; init; }
}

/// <summary>
/// Simplifies an index range into a chain of coarser levels with meshoptimizer (<c>meshopt_simplify</c>): positions only,
/// the range's borders locked so neighbouring ranges stay joined, each level from the last. A level reuses the range's
/// vertices, so only its indices are new.
/// </summary>
internal static unsafe class LodBuilder
{
	/// <summary>
	/// The levels of <paramref name="indices"/> (index values as stored, vertex <c>baseVertex + index</c> at
	/// <paramref name="position"/>), or null when it's too small, nothing simplifies or the library isn't there.
	/// </summary>
	public static LodChain Build( ReadOnlySpan<uint> indices, Func<int, V3> position, int baseVertex, LodConfig config )
	{
		// meshoptimizer is built without its asserts: what it would assert is checked here
		if ( indices.Length % 3 != 0 ) throw new ArgumentException( "Not whole triangles", nameof( indices ) );
		if ( config.MaxLevels is < 1 or > LodStore.MaxLevels ) throw new ArgumentOutOfRangeException( nameof( config ), config, $"1 to {LodStore.MaxLevels} levels" );
		var count = indices.Length;
		if ( count / 3 < Math.Max( config.MinRangeTriangles, 1 ) || !MeshOptimizer.Available ) return null;

		uint min = uint.MaxValue, max = 0;
		for ( int k = 0; k < count; k++ )
		{
			min = Math.Min( min, indices[k] );
			max = Math.Max( max, indices[k] );
		}

		var vertexCount = (int)(max - min + 1);
		var positions = new V3[vertexCount];
		for ( int v = 0; v < vertexCount; v++ ) positions[v] = position( baseVertex + (int)min + v );

		// The bounds of the vertices it uses: errors are kept as a fraction of their half diagonal, which the culls scale by
		// a fragment's world bounds
		var lo = new V3( float.MaxValue );
		var hi = new V3( float.MinValue );
		var current = new uint[count];
		for ( int k = 0; k < count; k++ )
		{
			current[k] = indices[k] - min;
			var p = positions[current[k]];
			lo = V3.Min( lo, p );
			hi = V3.Max( hi, p );
		}

		var radius = ((hi - lo) * 0.5f).Length();
		if ( !(radius > 0) || !float.IsFinite( radius ) ) return null;

		var levels = new List<uint[]>();
		var errors = new List<float>();
		var destination = new uint[count];
		float total = 0;
		const MeshOptimizer.SimplifyOptions Options = MeshOptimizer.SimplifyOptions.LockBorder | MeshOptimizer.SimplifyOptions.ErrorAbsolute;
		while ( levels.Count < config.MaxLevels && current.Length / 3 > config.MinTriangles )
		{
			var target = (nuint)Math.Min( Math.Max( (int)(current.Length / 3 * config.Ratio), config.MinTriangles ) * 3, current.Length );
			float error = 0;
			nuint written;
			fixed ( uint* dst = destination )
			fixed ( uint* src = current )
			fixed ( V3* p = positions )
				written = MeshOptimizer.Simplify( dst, src, (nuint)current.Length, (float*)p, (nuint)vertexCount, 12, target, float.MaxValue, Options, &error );

			// Not worth a level, or nothing left to draw
			if ( written < 3 || written > (nuint)(current.Length * config.KeepRatio) || !float.IsFinite( error ) ) break;

			// Errors add up along the chain: each level's is at most the sum of the steps to it
			total += error;
			current = destination.AsSpan( 0, (int)written ).ToArray();
			var level = new uint[current.Length];
			for ( int k = 0; k < level.Length; k++ ) level[k] = current[k] + min;
			levels.Add( level );
			errors.Add( total / radius );
		}

		return levels.Count == 0 ? null : new LodChain { Levels = levels.ToArray(), Errors = errors.ToArray() };
	}
}
