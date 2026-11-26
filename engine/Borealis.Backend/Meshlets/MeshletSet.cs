using System.Runtime.InteropServices;

namespace Borealis.Backend.Meshlets;

/// <summary>How meshes are cut into meshlets: at most this many vertices and triangles each.</summary>
internal readonly record struct MeshletConfig( int MaxVertices = 64, int MaxTriangles = 124 )
{
	/// <summary>meshoptimizer's defaults, which <c>-meshlets 64x124</c> names.</summary>
	public static MeshletConfig Default => new( 64, 124 );

	/// <summary>
	/// A config from <c>VxT</c> (<c>64x124</c>, <c>32x32</c>); the default for anything else. meshoptimizer takes triangle
	/// counts that are multiples of 4; a meshlet record holds at most 255.
	/// </summary>
	public static MeshletConfig Parse( string text )
	{
		var parts = text?.Split( 'x' );
		return parts is [var v, var t] && int.TryParse( v, out var vertices ) && int.TryParse( t, out var triangles ) && vertices is >= 3 and <= 256 && triangles is >= 4 and <= 252 && triangles % 4 == 0
			? new MeshletConfig( vertices, triangles )
			: Default;
	}

	public override string ToString() => $"{MaxVertices}x{MaxTriangles}";
}

/// <summary>
/// The meshlets of one index range: what Borealis builds at load (<see cref="MeshletBuilder"/>) or the resource compiler
/// cooked into the model (<see cref="FromCooked"/>). <see cref="Indices"/> are the range's own index values in meshlet
/// order, each triangle's corners in their order (its first vertex is what the visibility ids name); each meshlet's
/// triangles are a run of them.
/// </summary>
internal sealed class MeshletSet
{
	/// <summary>Bytes of a <see cref="MeshletRecord"/>.</summary>
	public const int RecordBytes = MeshletRecord.Size;

	public uint[] Indices { get; init; }
	public MeshletRecord[] Meshlets { get; init; }

	/// <summary>Whether the range's indices are already in this order where they're drawn from (cooked): nothing to rewrite.</summary>
	public bool InPlace { get; init; }

	/// <summary>
	/// A cooked range's set: its indices as stored (already in meshlet order) and its records, or null when they aren't
	/// one run covering every triangle once, cut no larger than <paramref name="config"/>.
	/// </summary>
	public static MeshletSet FromCooked( uint[] indices, ReadOnlySpan<byte> records, MeshletConfig config )
	{
		if ( records.Length == 0 || records.Length % RecordBytes != 0 ) return null;
		var meshlets = MemoryMarshal.Cast<byte, MeshletRecord>( records ).ToArray();
		uint next = 0;
		foreach ( ref readonly var meshlet in meshlets.AsSpan() )
		{
			if ( meshlet.FirstTriangle != next || meshlet.TriangleCount == 0 || meshlet.TriangleCount > config.MaxTriangles ) return null;
			next += meshlet.TriangleCount;
		}

		return next * 3 == indices.Length ? new MeshletSet { Indices = indices, Meshlets = meshlets, InPlace = true } : null;
	}

	/// <summary>Triangles in the range.</summary>
	public int Triangles => Indices.Length / 3;

	/// <summary>
	/// Each meshlet's own vertices and triangles, for a mesh shader: its distinct index values in the order its triangles
	/// first use them, and each triangle's corners as positions in that list.
	/// </summary>
	public MeshletLocal Local()
	{
		uint min = uint.MaxValue, max = 0;
		foreach ( var index in Indices )
		{
			min = Math.Min( min, index );
			max = Math.Max( max, index );
		}

		// Per index value, the last meshlet that numbered it (plus one) and its number there; scratch from the shared pools
		var range = Indices.Length == 0 ? 0 : (int)(max - min + 1);
		var seen = System.Buffers.ArrayPool<int>.Shared.Rent( range );
		var number = System.Buffers.ArrayPool<byte>.Shared.Rent( range );
		var vertices = System.Buffers.ArrayPool<uint>.Shared.Rent( Indices.Length );
		try
		{
			seen.AsSpan( 0, range ).Clear();
			var vertexCount = 0;
			var triangles = new uint[Triangles];
			var first = new int[Meshlets.Length];
			var counts = new int[Meshlets.Length];
			for ( int m = 0; m < Meshlets.Length; m++ )
			{
				ref readonly var meshlet = ref Meshlets[m];
				first[m] = vertexCount;
				for ( var t = meshlet.FirstTriangle; t < meshlet.FirstTriangle + meshlet.TriangleCount; t++ )
				{
					uint packed = 0;
					for ( int c = 0; c < 3; c++ )
					{
						var index = Indices[t * 3 + c];
						var at = (int)(index - min);
						if ( seen[at] != m + 1 )
						{
							if ( vertexCount - first[m] > byte.MaxValue ) throw new InvalidOperationException( "A meshlet with more than 256 vertices" );
							seen[at] = m + 1;
							number[at] = (byte)(vertexCount - first[m]);
							vertices[vertexCount++] = index;
						}

						packed |= (uint)number[at] << (c * 8);
					}

					triangles[t] = packed;
				}

				counts[m] = vertexCount - first[m];
			}

			return new MeshletLocal { Vertices = vertices.AsSpan( 0, vertexCount ).ToArray(), Triangles = triangles, FirstVertex = first, VertexCount = counts };
		}
		finally
		{
			System.Buffers.ArrayPool<int>.Shared.Return( seen );
			System.Buffers.ArrayPool<byte>.Shared.Return( number );
			System.Buffers.ArrayPool<uint>.Shared.Return( vertices );
		}
	}

	/// <summary>
	/// The lowest first corner (a triangle's first index, which a visibility id names it by) and how many values from it to
	/// the highest; (0, 0) without triangles.
	/// </summary>
	public (uint Low, uint Span) FirstCorners()
	{
		var triangles = Triangles;
		uint low = uint.MaxValue, high = 0;
		for ( int t = 0; t < triangles; t++ )
		{
			low = Math.Min( low, Indices[t * 3] );
			high = Math.Max( high, Indices[t * 3] );
		}

		return triangles == 0 ? (0, 0) : (low, high - low + 1);
	}

	/// <summary>
	/// The range's triangles by first corner, which a visibility id names a triangle by (its provoking vertex): for each index
	/// value from the lowest first corner to the highest, where its triangles start, then one more for the end; then each
	/// triangle's number in meshlet order, grouped by first corner. <see cref="MeshletCorners.Span"/> values have
	/// <c>Span + 1 + Triangles</c> words.
	/// </summary>
	public MeshletCorners Corners()
	{
		var triangles = Triangles;
		if ( triangles == 0 ) return null;
		var (low, spanValues) = FirstCorners();
		var span = (int)spanValues;
		var words = new uint[span + 1 + triangles];

		// Counts, then where each value's run starts
		for ( int t = 0; t < triangles; t++ ) words[Indices[t * 3] - low]++;
		uint at = 0;
		for ( int v = 0; v <= span; v++ )
		{
			var count = v < span ? words[v] : 0;
			words[v] = at;
			at += count;
		}

		var next = words.AsSpan( 0, span ).ToArray();
		for ( int t = 0; t < triangles; t++ ) words[span + 1 + next[Indices[t * 3] - low]++] = (uint)t;
		return new MeshletCorners { Words = words, Low = low, Span = (uint)span };
	}
}

/// <summary>A range's triangles by first corner (<see cref="MeshletSet.Corners"/>): the lowest first corner, how many values from it, and the words.</summary>
internal sealed class MeshletCorners
{
	public uint[] Words { get; init; }
	public uint Low { get; init; }
	public uint Span { get; init; }
}

/// <summary>
/// The meshlets of a range as a mesh shader reads them (<see cref="MeshletSet.Local"/>): each meshlet's distinct vertices
/// (index values, meshlet after meshlet) and, per triangle of the range in meshlet order, its corners' positions among its
/// meshlet's vertices, a byte each (first corner lowest).
/// </summary>
internal sealed class MeshletLocal
{
	public uint[] Vertices { get; init; }
	public uint[] Triangles { get; init; }
	public int[] FirstVertex { get; init; }
	public int[] VertexCount { get; init; }
}
