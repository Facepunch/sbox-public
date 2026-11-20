using System.Buffers.Binary;

namespace Borealis.Source2.Models;

/// <summary>
/// The meshlets the resource compiler cooked into a mesh (its <c>MSHL</c> block, <c>compilemeshlets.h</c>): per draw's
/// index range, the 32-byte records Borealis culls by (box centre, first triangle, half extents, triangle count), the
/// range's indices already stored in meshlet order. Each index buffer holds the ranges of its own
/// (<see cref="MeshIndexBuffer.Meshlets"/>). See docs/reference/borealis-gpu-driven.md, "Meshlets".
/// </summary>
public sealed class CookedMeshlets
{
	/// <summary>The block's four characters, <c>MSHL</c>.</summary>
	public const string BlockName = "MSHL";

	/// <summary><c>MSHL</c> as the block's first word.</summary>
	public const uint Magic = 0x4C48534D;

	/// <summary>The version this reads.</summary>
	public const uint Version = 1;

	/// <summary>Flag: each range's indices are stored in meshlet order (every version 1 block has it).</summary>
	public const uint IndicesInOrder = 1;

	/// <summary>Bytes of the header, of a range and of a record.</summary>
	public const int HeaderBytes = 32, RangeBytes = 32, RecordBytes = 32;

	/// <summary>Most vertices and triangles a meshlet has: the config they were cut with.</summary>
	public int MaxVertices { get; }

	/// <summary>Most triangles a meshlet has.</summary>
	public int MaxTriangles { get; }

	readonly byte[] records;
	readonly Dictionary<(int Start, int Count, int BaseVertex), (MeshVertexBuffer Vertices, int First, int Count)> ranges;

	CookedMeshlets( int maxVertices, int maxTriangles, byte[] records, Dictionary<(int, int, int), (MeshVertexBuffer, int, int)> ranges )
	{
		MaxVertices = maxVertices;
		MaxTriangles = maxTriangles;
		this.records = records;
		this.ranges = ranges;
	}

	/// <summary>Ranges it has.</summary>
	public int Count => ranges.Count;

	/// <summary>
	/// The records of the range of <paramref name="count"/> indices from <paramref name="start"/> (elements of this buffer),
	/// drawn from <paramref name="vertices"/> at <paramref name="baseVertex"/>; empty when it wasn't cooked.
	/// </summary>
	public ReadOnlyMemory<byte> Find( int start, int count, MeshVertexBuffer vertices, int baseVertex )
	{
		return ranges.TryGetValue( (start, count, baseVertex), out var range ) && ReferenceEquals( range.Vertices, vertices )
			? records.AsMemory( range.First * RecordBytes, range.Count * RecordBytes )
			: default;
	}

	/// <summary>
	/// Each index buffer's cooked ranges from a mesh's block, or null for all when there's no block or one this can't read
	/// (another version, a range outside its buffers): those meshes' meshlets are built at load.
	/// </summary>
	internal static CookedMeshlets[] Read( ReadOnlySpan<byte> block, MeshVertexBuffer[] vertexBuffers, MeshIndexBuffer[] indexBuffers )
	{
		if ( block.Length < HeaderBytes ) return null;
		var magic = BinaryPrimitives.ReadUInt32LittleEndian( block );
		var version = BinaryPrimitives.ReadUInt32LittleEndian( block[4..] );
		var flags = BinaryPrimitives.ReadUInt32LittleEndian( block[12..] );
		if ( magic != Magic || version != Version || (flags & IndicesInOrder) == 0 ) return null;

		int maxVertices = BinaryPrimitives.ReadUInt16LittleEndian( block[8..] ), maxTriangles = BinaryPrimitives.ReadUInt16LittleEndian( block[10..] );
		var rangeCount = BinaryPrimitives.ReadUInt32LittleEndian( block[16..] );
		var rangesAt = BinaryPrimitives.ReadUInt32LittleEndian( block[20..] );
		var recordCount = BinaryPrimitives.ReadUInt32LittleEndian( block[24..] );
		var recordsAt = BinaryPrimitives.ReadUInt32LittleEndian( block[28..] );
		if ( rangesAt + (long)rangeCount * RangeBytes > block.Length || recordsAt + (long)recordCount * RecordBytes > block.Length ) return null;

		var records = block.Slice( (int)recordsAt, (int)recordCount * RecordBytes ).ToArray();
		var perBuffer = new Dictionary<(int, int, int), (MeshVertexBuffer, int, int)>[indexBuffers.Length];
		for ( int i = 0; i < rangeCount; i++ )
		{
			var range = block.Slice( (int)rangesAt + i * RangeBytes, RangeBytes );
			var indexBuffer = BinaryPrimitives.ReadInt32LittleEndian( range );
			var vertexBuffer = BinaryPrimitives.ReadInt32LittleEndian( range[4..] );
			var start = BinaryPrimitives.ReadInt32LittleEndian( range[8..] );
			var count = BinaryPrimitives.ReadInt32LittleEndian( range[12..] );
			var baseVertex = BinaryPrimitives.ReadInt32LittleEndian( range[16..] );
			var first = BinaryPrimitives.ReadInt32LittleEndian( range[20..] );
			var meshlets = BinaryPrimitives.ReadInt32LittleEndian( range[24..] );
			if ( (uint)indexBuffer >= (uint)indexBuffers.Length || (uint)vertexBuffer >= (uint)vertexBuffers.Length ) return null;
			if ( start < 0 || count < 3 || (long)start + count > indexBuffers[indexBuffer].Count || first < 0 || meshlets <= 0 || (long)first + meshlets > recordCount ) return null;
			(perBuffer[indexBuffer] ??= new())[(start, count, baseVertex)] = (vertexBuffers[vertexBuffer], first, meshlets);
		}

		var result = new CookedMeshlets[indexBuffers.Length];
		for ( int b = 0; b < result.Length; b++ )
		{
			if ( perBuffer[b] is { } ranges ) result[b] = new CookedMeshlets( maxVertices, maxTriangles, records, ranges );
		}

		return result;
	}
}
