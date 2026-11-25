using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Borealis.Source2.Models;

/// <summary>
/// The meshlets the resource compiler cooked into a mesh (its <c>MSHL</c> block, <c>compilemeshlets.h</c>): per draw's
/// index range, the 32-byte records Borealis culls by (box centre, first triangle, half extents, triangle count), the
/// range's indices already stored in meshlet order; from version 2, also the range's generated LOD levels, each with its
/// error and its own meshlets (<see cref="CookedLevels"/>). Each index buffer holds the ranges of its own
/// (<see cref="MeshIndexBuffer.Meshlets"/>). See docs/reference/borealis-gpu-driven.md, "Meshlets" and "Generated LODs".
/// </summary>
public sealed class CookedMeshlets
{
	/// <summary>The block's four characters, <c>MSHL</c>.</summary>
	public const string BlockName = "MSHL";

	/// <summary><c>MSHL</c> as the block's first word.</summary>
	public const uint Magic = 0x4C48534D;

	/// <summary>The version written: 2, with levels. Version 1 (meshlets only) still reads.</summary>
	public const uint Version = 2;

	/// <summary>Flag: each range's indices are stored in meshlet order (every block has it).</summary>
	public const uint IndicesInOrder = 1;

	/// <summary>Flag: the block has levels, made with its LOD config.</summary>
	public const uint HasLevels = 2;

	/// <summary>Range flag: its levels were made (perhaps none, when nothing simplifies).</summary>
	public const uint RangeLevels = 1;

	/// <summary>Bytes of a version 1 header and range, of a version 2 header and range, of a level and of a record.</summary>
	public const int HeaderBytes1 = 32, RangeBytes1 = 32, HeaderBytes = 80, RangeBytes = 48, LevelBytes = 24, RecordBytes = 32;

	/// <summary>Most vertices and triangles a meshlet has: the config they were cut with.</summary>
	public int MaxVertices { get; }

	/// <summary>Most triangles a meshlet has.</summary>
	public int MaxTriangles { get; }

	/// <summary>What the levels were made with, or null when the block has none.</summary>
	public CookedLodConfig? Lods { get; }

	readonly byte[] records;
	readonly Dictionary<(int Start, int Count, int BaseVertex), Range> ranges;

	readonly record struct Range( MeshVertexBuffer Vertices, int First, int Count, CookedLevels Levels );

	CookedMeshlets( int maxVertices, int maxTriangles, CookedLodConfig? lods, byte[] records, Dictionary<(int, int, int), Range> ranges )
	{
		MaxVertices = maxVertices;
		MaxTriangles = maxTriangles;
		Lods = lods;
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

	/// <summary>The range's levels, as <see cref="Find"/> names it; null when they weren't made.</summary>
	public CookedLevels FindLevels( int start, int count, MeshVertexBuffer vertices, int baseVertex )
	{
		return ranges.TryGetValue( (start, count, baseVertex), out var range ) && ReferenceEquals( range.Vertices, vertices ) ? range.Levels : null;
	}

	/// <summary>
	/// Each index buffer's cooked ranges from a mesh's block, or null for all when there's no block or one this can't read
	/// (another version, a range outside its buffers): those meshes' meshlets and levels are made at load.
	/// </summary>
	internal static CookedMeshlets[] Read( ReadOnlySpan<byte> block, MeshVertexBuffer[] vertexBuffers, MeshIndexBuffer[] indexBuffers )
	{
		if ( block.Length < HeaderBytes1 ) return null;
		var magic = BinaryPrimitives.ReadUInt32LittleEndian( block );
		var version = BinaryPrimitives.ReadUInt32LittleEndian( block[4..] );
		var flags = BinaryPrimitives.ReadUInt32LittleEndian( block[12..] );
		if ( magic != Magic || version is not (1 or 2) || (flags & IndicesInOrder) == 0 || (version == 2 && block.Length < HeaderBytes) ) return null;

		int maxVertices = BinaryPrimitives.ReadUInt16LittleEndian( block[8..] ), maxTriangles = BinaryPrimitives.ReadUInt16LittleEndian( block[10..] );
		var rangeCount = BinaryPrimitives.ReadUInt32LittleEndian( block[16..] );
		var rangesAt = BinaryPrimitives.ReadUInt32LittleEndian( block[20..] );
		var recordCount = BinaryPrimitives.ReadUInt32LittleEndian( block[24..] );
		var recordsAt = BinaryPrimitives.ReadUInt32LittleEndian( block[28..] );
		var rangeBytes = version == 1 ? RangeBytes1 : RangeBytes;
		if ( rangesAt + (long)rangeCount * rangeBytes > block.Length || recordsAt + (long)recordCount * RecordBytes > block.Length ) return null;

		// Version 2's levels: their records, errors and indices, and the config they were made with
		uint levelCount = 0, levelsAt = 0, levelIndexCount = 0, levelIndicesAt = 0;
		CookedLodConfig? lods = null;
		if ( version == 2 && (flags & HasLevels) != 0 )
		{
			levelCount = BinaryPrimitives.ReadUInt32LittleEndian( block[32..] );
			levelsAt = BinaryPrimitives.ReadUInt32LittleEndian( block[36..] );
			levelIndexCount = BinaryPrimitives.ReadUInt32LittleEndian( block[40..] );
			levelIndicesAt = BinaryPrimitives.ReadUInt32LittleEndian( block[44..] );
			if ( levelsAt + (long)levelCount * LevelBytes > block.Length || levelIndicesAt + (long)levelIndexCount * 4 > block.Length ) return null;
			lods = new CookedLodConfig(
				BinaryPrimitives.ReadInt32LittleEndian( block[48..] ), BinaryPrimitives.ReadSingleLittleEndian( block[52..] ), BinaryPrimitives.ReadSingleLittleEndian( block[56..] ),
				BinaryPrimitives.ReadInt32LittleEndian( block[60..] ), BinaryPrimitives.ReadInt32LittleEndian( block[64..] ), BinaryPrimitives.ReadSingleLittleEndian( block[68..] ),
				BinaryPrimitives.ReadSingleLittleEndian( block[72..] ) );
		}

		var records = block.Slice( (int)recordsAt, (int)recordCount * RecordBytes ).ToArray();
		var levelIndices = MemoryMarshal.Cast<byte, uint>( block.Slice( (int)levelIndicesAt, (int)levelIndexCount * 4 ) );
		var perBuffer = new Dictionary<(int, int, int), Range>[indexBuffers.Length];
		for ( int i = 0; i < rangeCount; i++ )
		{
			var range = block.Slice( (int)rangesAt + i * rangeBytes, rangeBytes );
			var indexBuffer = BinaryPrimitives.ReadInt32LittleEndian( range );
			var vertexBuffer = BinaryPrimitives.ReadInt32LittleEndian( range[4..] );
			var start = BinaryPrimitives.ReadInt32LittleEndian( range[8..] );
			var count = BinaryPrimitives.ReadInt32LittleEndian( range[12..] );
			var baseVertex = BinaryPrimitives.ReadInt32LittleEndian( range[16..] );
			var first = BinaryPrimitives.ReadInt32LittleEndian( range[20..] );
			var meshlets = BinaryPrimitives.ReadInt32LittleEndian( range[24..] );
			if ( (uint)indexBuffer >= (uint)indexBuffers.Length || (uint)vertexBuffer >= (uint)vertexBuffers.Length ) return null;
			if ( start < 0 || count < 3 || (long)start + count > indexBuffers[indexBuffer].Count || first < 0 || meshlets <= 0 || (long)first + meshlets > recordCount ) return null;

			CookedLevels levels = null;
			if ( lods is { } lodConfig && (BinaryPrimitives.ReadUInt32LittleEndian( range[28..] ) & RangeLevels) != 0 )
			{
				var firstLevel = BinaryPrimitives.ReadInt32LittleEndian( range[32..] );
				var levelsInRange = BinaryPrimitives.ReadInt32LittleEndian( range[36..] );
				if ( firstLevel < 0 || levelsInRange < 0 || (long)firstLevel + levelsInRange > levelCount ) return null;
				levels = ReadLevels( block[(int)levelsAt..], firstLevel, levelsInRange, levelIndices, records, maxVertices, maxTriangles, vertexBuffers[vertexBuffer], baseVertex );
				if ( levels is null ) return null;
			}

			(perBuffer[indexBuffer] ??= new())[(start, count, baseVertex)] = new Range( vertexBuffers[vertexBuffer], first, meshlets, levels );
		}

		var result = new CookedMeshlets[indexBuffers.Length];
		for ( int b = 0; b < result.Length; b++ )
		{
			if ( perBuffer[b] is { } ranges ) result[b] = new CookedMeshlets( maxVertices, maxTriangles, lods, records, ranges );
		}

		return result;
	}

	/// <summary>A range's levels: each one's indices (a buffer of its own, with its meshlets when it has records) and error.</summary>
	static CookedLevels ReadLevels( ReadOnlySpan<byte> levels, int first, int count, ReadOnlySpan<uint> indices, byte[] records, int maxVertices, int maxTriangles, MeshVertexBuffer vertices, int baseVertex )
	{
		var buffers = new MeshIndexBuffer[count];
		var errors = new float[count];
		for ( int k = 0; k < count; k++ )
		{
			var level = levels.Slice( (first + k) * LevelBytes, LevelBytes );
			var at = BinaryPrimitives.ReadInt32LittleEndian( level );
			var length = BinaryPrimitives.ReadInt32LittleEndian( level[4..] );
			errors[k] = BinaryPrimitives.ReadSingleLittleEndian( level[8..] );
			var firstRecord = BinaryPrimitives.ReadInt32LittleEndian( level[12..] );
			var recordCount = BinaryPrimitives.ReadInt32LittleEndian( level[16..] );
			if ( at < 0 || length < 3 || length % 3 != 0 || (long)at + length > indices.Length || firstRecord < 0 || recordCount < 0 || (long)firstRecord + recordCount > records.Length / RecordBytes ) return null;

			var data = MemoryMarshal.AsBytes( indices.Slice( at, length ) ).ToArray();
			CookedMeshlets meshlets = null;
			if ( recordCount > 0 )
			{
				var own = new Dictionary<(int, int, int), Range> { [(0, length, baseVertex)] = new Range( vertices, firstRecord, recordCount, null ) };
				meshlets = new CookedMeshlets( maxVertices, maxTriangles, null, records, own );
			}

			buffers[k] = new MeshIndexBuffer( length, 4, data ) { Generated = true, Meshlets = meshlets };
		}

		return new CookedLevels { Buffers = buffers, Errors = errors };
	}
}

/// <summary>
/// The config a mesh's cooked levels were made with (Borealis.Backend's <c>LodConfig</c>): a load takes them only when it
/// would make its own with the same.
/// </summary>
public readonly record struct CookedLodConfig( int MaxLevels, float Ratio, float KeepRatio, int MinTriangles, int MinRangeTriangles, float NormalWeight, float ColorWeight );

/// <summary>
/// A range's cooked LOD levels, finest first: each level's indices (the range's own index values, in meshlet order when it
/// has meshlets) in a buffer of its own, with that level's cooked meshlets over all of it; and each level's error, a
/// fraction of the range's bounds' half diagonal. None when nothing simplified.
/// </summary>
public sealed class CookedLevels
{
	/// <summary>Each level's indices, in a buffer of its own.</summary>
	public IReadOnlyList<MeshIndexBuffer> Buffers { get; init; }

	/// <summary>Each level's error.</summary>
	public IReadOnlyList<float> Errors { get; init; }
}
