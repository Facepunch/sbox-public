using System.Buffers;
using System.Collections.Concurrent;

namespace Sandbox.Navigation.Generation;

[SkipHotload]
internal class PolyMesh : IDisposable
{
	public Vector3 BMin { get; set; }
	public Vector3 BMax { get; set; }
	public float CellSize { get; set; }
	public float CellHeight { get; set; }
	public int BorderSize { get; set; }
	public float MaxEdgeError { get; set; }

	/// <summary>
	/// The polygon vertices. [Size: 3 * #nverts]
	/// </summary>
	public Span<ushort> Verts => _vertsArray.AsSpan( 0, maxVertCount * 3 );
	private ushort[] _vertsArray;

	/// <summary>
	/// The polygon indices. [Size: 2 * #npolys * nvp]
	/// </summary>
	public Span<ushort> Polys => _polysArray.AsSpan( 0, maxPolyCount * MaxVertsPerPoly * 2 );
	private ushort[] _polysArray;

	/// <summary>
	/// The area id assigned to each polygon. [Size: #npolys]
	/// </summary>
	public Span<int> Areas => _areasArray.AsSpan( 0, maxPolyCount );
	private int[] _areasArray;

	public int VertCount { get; set; }
	public int PolyCount { get; set; }
	public int MaxPolys { get; set; }
	public int MaxVertsPerPoly { get; set; }

	private int maxVertCount;
	private int maxPolyCount;

	private PolyMesh()
	{
	}

	internal void Init( ContourSet cset, int maxVertsPerPoly, int maxTris, int maxVertices )
	{
		BMin = cset.BMin;
		BMax = cset.BMax;
		CellSize = cset.CellSize;
		CellHeight = cset.CellHeight;
		BorderSize = cset.BorderSize;
		MaxEdgeError = cset.MaxError;

		maxVertCount = maxVertices;
		maxPolyCount = maxTris;

		if ( _vertsArray == null || _vertsArray.Length < maxVertices * 3 )
		{
			if ( _vertsArray != null ) ArrayPool<ushort>.Shared.Return( _vertsArray );
			_vertsArray = ArrayPool<ushort>.Shared.Rent( maxVertices * 3 * 2 );
		}
		if ( _polysArray == null || _polysArray.Length < maxTris * maxVertsPerPoly * 2 )
		{
			if ( _polysArray != null ) ArrayPool<ushort>.Shared.Return( _polysArray );
			_polysArray = ArrayPool<ushort>.Shared.Rent( maxTris * maxVertsPerPoly * 2 * 2 );
		}
		if ( _areasArray == null || _areasArray.Length < maxTris )
		{
			if ( _areasArray != null ) ArrayPool<int>.Shared.Return( _areasArray );
			_areasArray = ArrayPool<int>.Shared.Rent( maxTris * 2 );
		}
		VertCount = 0;
		PolyCount = 0;
		MaxVertsPerPoly = maxVertsPerPoly;
		MaxPolys = maxTris;

		// We intentionally do NOT clear/fill the other arrays here here. All used elements
		// are fully written during BuildPolyMesh
		Polys.Fill( Constants.MESH_NULL_IDX );
	}

	private static ConcurrentQueue<PolyMesh> _pool = new();

	public static PolyMesh GetPooled()
	{
		return _pool.TryDequeue( out var hf ) ? hf : new PolyMesh();
	}

	public void Dispose()
	{
		_pool.Enqueue( this );
		// Arrays will get disposed on shutdown, i guess
	}
}
