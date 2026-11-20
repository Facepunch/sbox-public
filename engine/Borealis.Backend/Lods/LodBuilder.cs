using Borealis.Backend.Meshlets;
using V3 = System.Numerics.Vector3;

namespace Borealis.Backend.Lods;

/// <summary>
/// How an index range's coarser levels are made: each level about <see cref="Ratio"/> of the last's triangles, kept only
/// when it has at most <see cref="KeepRatio"/> of them, until <see cref="MaxLevels"/> or <see cref="MinTriangles"/>.
/// </summary>
internal readonly record struct LodConfig( int MaxLevels = 6, float Ratio = 0.5f, float KeepRatio = 0.8f, int MinTriangles = 16, int MinRangeTriangles = 64, float NormalWeight = 0.02f, float ColorWeight = 0.05f )
{
	/// <summary>
	/// Up to 6 levels of half the last's triangles, from ranges of 64 or more; a normal turned all the way round counts as
	/// far as 4% of the range's size (<see cref="NormalWeight"/> of it per unit of normal), and a vertex colour or blend
	/// weight going from 0 to 1 as far as 5% (<see cref="ColorWeight"/>).
	/// </summary>
	public static LodConfig Default => new( 6, 0.5f, 0.8f, 16, 64, 0.02f, 0.05f );

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
/// Simplifies an index range into a chain of coarser levels with meshoptimizer (<c>meshopt_simplifyWithAttributes</c>): the
/// range's borders locked so neighbouring ranges stay joined, each level from the last. Texture coordinates (the lightmap's
/// among them) count in the error as far as the surface they map would slide, so a level keeps baked shadows and texture
/// where they were. A level reuses the range's vertices, so only its indices are new.
/// </summary>
internal static unsafe class LodBuilder
{
	/// <summary>
	/// A range's triangles in one order whatever order they're stored in (sorted by their corners, each triangle's corners
	/// as they are), so its levels don't depend on it: the resource compiler stores aggregate fragments in meshlet order
	/// (<see cref="Borealis.Source2.Models.CookedMeshlets"/>), and the simplifier's result depends on its input's order.
	/// </summary>
	static uint[] Canonical( ReadOnlySpan<uint> indices )
	{
		var triangles = new (uint A, uint B, uint C)[indices.Length / 3];
		for ( int t = 0; t < triangles.Length; t++ ) triangles[t] = (indices[t * 3], indices[t * 3 + 1], indices[t * 3 + 2]);
		Array.Sort( triangles );
		var sorted = new uint[indices.Length];
		for ( int t = 0; t < triangles.Length; t++ ) (sorted[t * 3], sorted[t * 3 + 1], sorted[t * 3 + 2]) = triangles[t];
		return sorted;
	}

	/// <summary>
	/// The levels of <paramref name="indices"/> (index values as stored, vertex <c>baseVertex + index</c> at
	/// <paramref name="position"/>), or null when it's too small, nothing simplifies or the library isn't there.
	/// </summary>
	public static LodChain Build( ReadOnlySpan<uint> indices, Func<int, V3> position, int baseVertex, LodConfig config, Func<int, System.Numerics.Vector2>[] coordinates = null, Func<int, V3> normal = null, Func<int, System.Numerics.Vector4>[] colors = null )
	{
		// meshoptimizer is built without its asserts: what it would assert is checked here
		if ( indices.Length % 3 != 0 ) throw new ArgumentException( "Not whole triangles", nameof( indices ) );
		if ( config.MaxLevels is < 1 or > LodStore.MaxLevels ) throw new ArgumentOutOfRangeException( nameof( config ), config, $"1 to {LodStore.MaxLevels} levels" );
		var count = indices.Length;
		if ( count / 3 < Math.Max( config.MinRangeTriangles, 1 ) || !MeshOptimizer.Available ) return null;
		indices = Canonical( indices );

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

		// Texture coordinates, each weighed by the surface it maps per unit (world area over its area), over meshoptimizer's
		// scale (it works in positions scaled to a unit box), so a coordinate's error is the distance its texture slides
		var sets = coordinates?.Length ?? 0;
		var normals = normal is not null && config.NormalWeight > 0 ? 3 : 0;
		var colorSets = config.ColorWeight > 0 ? colors?.Length ?? 0 : 0;
		var stride = sets * 2 + normals + colorSets * 4;
		var attributes = new float[Math.Max( vertexCount * stride, 1 )];
		var weights = new float[Math.Max( stride, 1 )];
		float extent;
		fixed ( V3* p = positions ) extent = MeshOptimizer.SimplifyScale( (float*)p, (nuint)vertexCount, 12 );
		for ( int c = 0; c < sets; c++ )
		{
			for ( int v = 0; v < vertexCount; v++ )
			{
				var uv = coordinates[c]( baseVertex + (int)min + v );
				attributes[v * stride + c * 2] = float.IsFinite( uv.X ) ? uv.X : 0;
				attributes[v * stride + c * 2 + 1] = float.IsFinite( uv.Y ) ? uv.Y : 0;
			}

			double world = 0, mapped = 0;
			for ( int t = 0; t < count; t += 3 )
			{
				var (a, b, d) = (current[t], current[t + 1], current[t + 2]);
				world += V3.Cross( positions[b] - positions[a], positions[d] - positions[a] ).Length() * 0.5;
				var ua = new System.Numerics.Vector2( attributes[a * stride + c * 2], attributes[a * stride + c * 2 + 1] );
				var ub = new System.Numerics.Vector2( attributes[b * stride + c * 2], attributes[b * stride + c * 2 + 1] ) - ua;
				var ud = new System.Numerics.Vector2( attributes[d * stride + c * 2], attributes[d * stride + c * 2 + 1] ) - ua;
				mapped += Math.Abs( ub.X * ud.Y - ub.Y * ud.X ) * 0.5;
			}

			var weight = mapped > 0 && extent > 0 ? (float)(Math.Sqrt( world / mapped ) / extent) : 0;
			weights[c * 2] = weights[c * 2 + 1] = float.IsFinite( weight ) ? weight : 0;
		}

		// Normals, which shading follows however small the surface: a unit of normal counts as NormalWeight of the range's
		// size (meshoptimizer's scale is its extent, so the weight is that fraction as it is)
		for ( int v = 0; v < vertexCount && normals > 0; v++ )
		{
			var n = normal( baseVertex + (int)min + v );
			attributes[v * stride + sets * 2] = float.IsFinite( n.X ) ? n.X : 0;
			attributes[v * stride + sets * 2 + 1] = float.IsFinite( n.Y ) ? n.Y : 0;
			attributes[v * stride + sets * 2 + 2] = float.IsFinite( n.Z ) ? n.Z : 0;
		}

		for ( int k = 0; k < normals; k++ ) weights[sets * 2 + k] = config.NormalWeight;

		// Vertex colours and blend weights (a blended material's paint), which change what's drawn however small the surface
		for ( int c = 0; c < colorSets; c++ )
		{
			var at = sets * 2 + normals + c * 4;
			for ( int v = 0; v < vertexCount; v++ )
			{
				var color = colors[c]( baseVertex + (int)min + v );
				attributes[v * stride + at] = color.X;
				attributes[v * stride + at + 1] = color.Y;
				attributes[v * stride + at + 2] = color.Z;
				attributes[v * stride + at + 3] = color.W;
			}

			for ( int k = 0; k < 4; k++ ) weights[at + k] = config.ColorWeight;
		}

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
			fixed ( float* a = attributes )
			fixed ( float* w = weights )
			{
				written = stride > 0
					? MeshOptimizer.SimplifyWithAttributes( dst, src, (nuint)current.Length, (float*)p, (nuint)vertexCount, 12, a, (nuint)(stride * 4), w, (nuint)stride, null, target, float.MaxValue, Options, &error )
					: MeshOptimizer.Simplify( dst, src, (nuint)current.Length, (float*)p, (nuint)vertexCount, 12, target, float.MaxValue, Options, &error );
			}

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
