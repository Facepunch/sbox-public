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
/// The meshlets of one index range: what Borealis builds at load (<see cref="MeshletBuilder"/>) and a resource compiler
/// could cook into a model or map instead, so the renderer takes either. <see cref="Indices"/> are the range's own index
/// values in meshlet order, each triangle's corners in their order (its first vertex is what the visibility ids name);
/// each meshlet's triangles are a run of them.
/// </summary>
internal sealed class MeshletSet
{
	/// <summary>Bytes of a <see cref="MeshletRecord"/>.</summary>
	public const int RecordBytes = MeshletRecord.Size;

	public uint[] Indices { get; init; }
	public MeshletRecord[] Meshlets { get; init; }

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

		// Per index value, the last meshlet that numbered it (plus one) and its number there
		var seen = new int[Indices.Length == 0 ? 0 : max - min + 1];
		var number = new byte[seen.Length];
		var vertices = new List<uint>( Indices.Length / 2 );
		var triangles = new uint[Triangles];
		var first = new int[Meshlets.Length];
		var counts = new int[Meshlets.Length];
		for ( int m = 0; m < Meshlets.Length; m++ )
		{
			ref readonly var meshlet = ref Meshlets[m];
			first[m] = vertices.Count;
			for ( var t = meshlet.FirstTriangle; t < meshlet.FirstTriangle + meshlet.TriangleCount; t++ )
			{
				uint packed = 0;
				for ( int c = 0; c < 3; c++ )
				{
					var index = Indices[t * 3 + c];
					var at = (int)(index - min);
					if ( seen[at] != m + 1 )
					{
						if ( vertices.Count - first[m] > byte.MaxValue ) throw new InvalidOperationException( "A meshlet with more than 256 vertices" );
						seen[at] = m + 1;
						number[at] = (byte)(vertices.Count - first[m]);
						vertices.Add( index );
					}

					packed |= (uint)number[at] << (c * 8);
				}

				triangles[t] = packed;
			}

			counts[m] = vertices.Count - first[m];
		}

		return new MeshletLocal { Vertices = [.. vertices], Triangles = triangles, FirstVertex = first, VertexCount = counts };
	}
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
