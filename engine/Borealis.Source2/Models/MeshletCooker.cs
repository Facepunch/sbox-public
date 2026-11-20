using Borealis.Source2.Resources;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Borealis.Source2.Models;

/// <summary>
/// A range's meshlets as a cooker cuts them (Borealis.Backend's <c>MeshletBuilder</c>): its index values in meshlet order
/// and its 32-byte records, or null to leave the range as it is. <paramref name="position"/> takes a vertex of the buffer
/// (<paramref name="baseVertex"/> plus an index).
/// </summary>
public delegate (uint[] Indices, byte[] Records)? MeshletCut( uint[] indices, Func<int, Vector3> position, int baseVertex );

/// <summary>
/// What the resource compiler writes for a mesh's meshlets (<c>compilemeshlets.cpp</c>), done to a <c>.vmesh_c</c> already
/// compiled: for content whose source isn't at hand (a workshop map), to load it as a recompile would.
/// </summary>
public static class MeshletCooker
{
	/// <summary>
	/// The bytes of <paramref name="vmesh"/> (an aggregate's mesh: every draw a fragment) with each draw's indices in meshlet
	/// order (its index buffers stored decoded) and an <c>MSHL</c> block, as the compiler cooks them. Null when there's
	/// nothing to cook: a skinned or morphed mesh, one already cooked, or no range cut whole.
	/// </summary>
	public static byte[] CookMesh( byte[] vmesh, int maxVertices, int maxTriangles, MeshletCut cut )
	{
		var resource = ResourceFile.Read( vmesh );
		if ( resource.Has( CookedMeshlets.BlockName ) || !resource.Has( "DATA" ) || !resource.Has( "VBIB" ) || resource.Has( "MRPH" ) || resource.HeaderSize != vmesh.Length ) return null;

		var vbib = resource.Block( "VBIB" );
		var mesh = ModelMesh.Read( resource.Block( "DATA" ), vbib, "cook", 0, ulong.MaxValue, 0xFF, false, null );
		return Cut( mesh, maxVertices, maxTriangles, cut ) is var (indexData, block)
			? WithBlocks( vmesh, ("VBIB", Reindexed( vbib, indexData )), (CookedMeshlets.BlockName, block) )
			: null;
	}

	/// <summary>
	/// A mesh already read, cooked as <see cref="CookMesh"/> cooks its file: the same mesh with its index buffers' ranges in
	/// meshlet order and their <see cref="MeshIndexBuffer.Meshlets"/>, or null when there's nothing to cook.
	/// </summary>
	public static ModelMesh Cook( ModelMesh mesh, int maxVertices, int maxTriangles, MeshletCut cut )
	{
		if ( mesh.HasMorphs || Cut( mesh, maxVertices, maxTriangles, cut ) is not var (indexData, block) ) return null;
		var indexBuffers = mesh.IndexBuffers.Select( ( b, i ) => indexData[i] is { } data ? b with { Data = data } : b ).ToArray();
		var vertexBuffers = mesh.VertexBuffers.ToArray();
		var cooked = CookedMeshlets.Read( block, vertexBuffers, indexBuffers );
		for ( int i = 0; i < indexBuffers.Length; i++ )
		{
			if ( cooked?[i] is { } meshlets ) indexBuffers[i] = indexBuffers[i] with { Meshlets = meshlets };
		}

		return mesh.WithBuffers( vertexBuffers, indexBuffers );
	}

	/// <summary>
	/// Each index buffer's data with its draws' ranges in meshlet order (null for one left as it was), and the block; null
	/// when no range is cut.
	/// </summary>
	static (byte[][] IndexData, byte[] Block)? Cut( ModelMesh mesh, int maxVertices, int maxTriangles, MeshletCut cut )
	{
		if ( mesh.IsSkinned ) return null;

		var indexData = new byte[mesh.IndexBuffers.Count][];
		var ranges = new List<(int IndexBuffer, int VertexBuffer, int Start, int Count, int BaseVertex, int First, int Records)>();
		var records = new MemoryStream();
		foreach ( var draw in mesh.DrawCalls )
		{
			if ( draw.Primitive != PrimitiveKind.Triangles || draw.IndexCount < 3 || draw.IndexCount % 3 != 0 || draw.VertexBuffers.Count == 0 ) continue;
			var (vbIndex, vbOffset) = draw.VertexBuffers[0];
			var (ibIndex, ibOffset) = draw.IndexBuffer;
			if ( (uint)vbIndex >= (uint)mesh.VertexBuffers.Count || (uint)ibIndex >= (uint)mesh.IndexBuffers.Count ) continue;
			var vb = mesh.VertexBuffers[vbIndex];
			var ib = mesh.IndexBuffers[ibIndex];
			if ( ib.Generated || vb.Stride == 0 || vb.Find( "BLENDINDICES" ) is not null || vb.Find( "POSITION" ) is not { Format: ColorFormat.R32G32B32_FLOAT } position ) continue;

			var start = ibOffset / ib.Size + draw.StartIndex;
			var baseVertex = vbOffset / vb.Stride + draw.BaseVertex;
			if ( start < 0 || start + draw.IndexCount > ib.Count ) continue;

			// Each range once, and none over another: reordering one would reorder the other's triangles
			var end = start + draw.IndexCount;
			if ( ranges.Exists( r => r.IndexBuffer == ibIndex && r.Start < end && start < r.Start + r.Count ) ) continue;

			var data = indexData[ibIndex] ??= (byte[])ib.Data.Clone();
			var indices = new uint[draw.IndexCount];
			for ( int k = 0; k < indices.Length; k++ ) indices[k] = ib.Size == 2 ? BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( (start + k) * 2 ) ) : BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( (start + k) * 4 ) );
			if ( cut( indices, v => { var p = vb.Read( position, v ); return new Vector3( p.X, p.Y, p.Z ); }, baseVertex ) is not { } made ) continue;
			var (ordered, cutRecords) = made;
			if ( ordered.Length != indices.Length || cutRecords.Length == 0 || cutRecords.Length % CookedMeshlets.RecordBytes != 0 ) continue;

			for ( int k = 0; k < ordered.Length; k++ )
			{
				if ( ib.Size == 2 ) BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( (start + k) * 2 ), (ushort)ordered[k] );
				else BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( (start + k) * 4 ), ordered[k] );
			}

			ranges.Add( (ibIndex, vbIndex, start, draw.IndexCount, baseVertex, (int)(records.Length / CookedMeshlets.RecordBytes), cutRecords.Length / CookedMeshlets.RecordBytes) );
			records.Write( cutRecords );
		}

		if ( ranges.Count == 0 ) return null;

		// The block: header, ranges, records
		var block = new byte[CookedMeshlets.HeaderBytes + ranges.Count * CookedMeshlets.RangeBytes + records.Length];
		var span = block.AsSpan();
		BinaryPrimitives.WriteUInt32LittleEndian( span, CookedMeshlets.Magic );
		BinaryPrimitives.WriteUInt32LittleEndian( span[4..], CookedMeshlets.Version );
		BinaryPrimitives.WriteUInt16LittleEndian( span[8..], (ushort)maxVertices );
		BinaryPrimitives.WriteUInt16LittleEndian( span[10..], (ushort)maxTriangles );
		BinaryPrimitives.WriteUInt32LittleEndian( span[12..], CookedMeshlets.IndicesInOrder );
		BinaryPrimitives.WriteInt32LittleEndian( span[16..], ranges.Count );
		BinaryPrimitives.WriteInt32LittleEndian( span[20..], CookedMeshlets.HeaderBytes );
		BinaryPrimitives.WriteInt32LittleEndian( span[24..], (int)(records.Length / CookedMeshlets.RecordBytes) );
		BinaryPrimitives.WriteInt32LittleEndian( span[28..], CookedMeshlets.HeaderBytes + ranges.Count * CookedMeshlets.RangeBytes );
		for ( int i = 0; i < ranges.Count; i++ )
		{
			var at = span[(CookedMeshlets.HeaderBytes + i * CookedMeshlets.RangeBytes)..];
			var r = ranges[i];
			BinaryPrimitives.WriteInt32LittleEndian( at, r.IndexBuffer );
			BinaryPrimitives.WriteInt32LittleEndian( at[4..], r.VertexBuffer );
			BinaryPrimitives.WriteInt32LittleEndian( at[8..], r.Start );
			BinaryPrimitives.WriteInt32LittleEndian( at[12..], r.Count );
			BinaryPrimitives.WriteInt32LittleEndian( at[16..], r.BaseVertex );
			BinaryPrimitives.WriteInt32LittleEndian( at[20..], r.First );
			BinaryPrimitives.WriteInt32LittleEndian( at[24..], r.Records );
		}

		records.ToArray().CopyTo( span[(CookedMeshlets.HeaderBytes + ranges.Count * CookedMeshlets.RangeBytes)..] );
		return (indexData, block);
	}

	/// <summary>
	/// A buffer block (<c>VBIBBlockHeader_t</c>) whose index buffers with new data point at it, stored decoded after the rest
	/// (what they pointed at is left in place, unread).
	/// </summary>
	static byte[] Reindexed( ReadOnlySpan<byte> vbib, byte[][] indexData )
	{
		var output = new MemoryStream();
		output.Write( vbib );
		var bytes = output.GetBuffer();
		var appended = new List<(int Entry, long At, int Size)>();
		var indexStart = 8 + BinaryPrimitives.ReadInt32LittleEndian( vbib[8..] );
		for ( int i = 0; i < indexData.Length; i++ )
		{
			if ( indexData[i] is not { } data ) continue;
			while ( output.Length % 16 != 0 ) output.WriteByte( 0 );
			appended.Add( (indexStart + i * 24, output.Length, data.Length) );
			output.Write( data );
		}

		var result = output.ToArray();
		foreach ( var (entry, at, size) in appended )
		{
			BinaryPrimitives.WriteInt32LittleEndian( result.AsSpan( entry + 16 ), (int)(at - (entry + 16)) );
			BinaryPrimitives.WriteUInt32LittleEndian( result.AsSpan( entry + 20 ), (uint)size );
		}

		return result;
	}

	/// <summary>
	/// A resource's bytes again with blocks replaced or added (a header, the block table, then every block 16-byte aligned),
	/// the others as they were and in their order.
	/// </summary>
	static byte[] WithBlocks( byte[] data, params (string Name, byte[] Bytes)[] changed )
	{
		var tableAt = 8 + BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 8 ) );
		var count = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 12 ) );
		var blocks = new List<(string Name, byte[] Bytes)>();
		for ( int i = 0; i < count; i++ )
		{
			var entry = tableAt + i * 12;
			var name = System.Text.Encoding.ASCII.GetString( data, entry, 4 );
			var offset = entry + 4 + BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( entry + 4 ) );
			var size = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( entry + 8 ) );
			blocks.Add( (name, data.AsSpan( offset, size ).ToArray()) );
		}

		foreach ( var (name, bytes) in changed )
		{
			var at = blocks.FindIndex( b => b.Name == name );
			if ( at >= 0 ) blocks[at] = (name, bytes);
			else blocks.Add( (name, bytes) );
		}

		var output = new MemoryStream();
		var header = new byte[16 + blocks.Count * 12];
		data.AsSpan( 4, 4 ).CopyTo( header.AsSpan( 4 ) );
		BinaryPrimitives.WriteInt32LittleEndian( header.AsSpan( 8 ), 8 );
		BinaryPrimitives.WriteInt32LittleEndian( header.AsSpan( 12 ), blocks.Count );
		output.Write( header );
		var placed = new long[blocks.Count];
		for ( int i = 0; i < blocks.Count; i++ )
		{
			while ( output.Length % 16 != 0 ) output.WriteByte( 0 );
			placed[i] = output.Length;
			output.Write( blocks[i].Bytes );
		}

		var result = output.ToArray();
		BinaryPrimitives.WriteUInt32LittleEndian( result, (uint)result.Length );
		for ( int i = 0; i < blocks.Count; i++ )
		{
			var entry = 16 + i * 12;
			System.Text.Encoding.ASCII.GetBytes( blocks[i].Name ).CopyTo( result, entry );
			BinaryPrimitives.WriteInt32LittleEndian( result.AsSpan( entry + 4 ), (int)(placed[i] - (entry + 4)) );
			BinaryPrimitives.WriteInt32LittleEndian( result.AsSpan( entry + 8 ), blocks[i].Bytes.Length );
		}

		return result;
	}
}
