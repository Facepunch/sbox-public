using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Nav.Build;

/// <summary>
/// The eroded open-space field of one tile in the form the mesh keeps it: a tile can be rebuilt from this
/// and its volumes without geometry. Columns run x first, then y, border included. Either the three
/// arrays, or, when the mesh has a codec and it made them smaller, the codec's output in Packed.
/// Never changed once the mesh has it, so jobs read it on any thread.
/// </summary>
internal sealed class CachedField
{
	/// <summary>
	/// Cells per edge, border included.
	/// </summary>
	public int Size;

	/// <summary>
	/// World height of floor 0.
	/// </summary>
	public float OriginZ;

	public int SpanCount;

	/// <summary>
	/// Spans per column. The arrays may be longer than what is in use.
	/// </summary>
	public byte[] Counts = [];

	/// <summary>
	/// Floor of each span in cells above OriginZ.
	/// </summary>
	public ushort[] Floors = [];

	/// <summary>
	/// Per span: four 6-bit indices into the neighbour columns (-x, +y, +x, -y; 63 = none), area in the top byte.
	/// </summary>
	public uint[] Cons = [];

	/// <summary>
	/// What the codec made of the bytes of WriteTo. The arrays are empty then.
	/// </summary>
	public byte[]? Packed;

	const int HeaderBytes = 12;

	/// <summary>
	/// The size of what WriteTo writes.
	/// </summary>
	public int ByteSize => HeaderBytes + Size * Size + SpanCount * 6;

	/// <summary>
	/// What the object holds in memory.
	/// </summary>
	public long Bytes => Packed != null ? 64 + 24 + Packed.Length : 64 + 3 * 24 + (long)Counts.Length + Floors.Length * 2L + Cons.Length * 4L;

	public void WriteTo( Span<byte> bytes )
	{
		BinaryPrimitives.WriteInt32LittleEndian( bytes, Size );
		BinaryPrimitives.WriteSingleLittleEndian( bytes.Slice( 4 ), OriginZ );
		BinaryPrimitives.WriteInt32LittleEndian( bytes.Slice( 8 ), SpanCount );
		int columns = Size * Size;
		Counts.AsSpan( 0, columns ).CopyTo( bytes.Slice( HeaderBytes ) );
		MemoryMarshal.AsBytes( Floors.AsSpan( 0, SpanCount ) ).CopyTo( bytes.Slice( HeaderBytes + columns ) );
		MemoryMarshal.AsBytes( Cons.AsSpan( 0, SpanCount ) ).CopyTo( bytes.Slice( HeaderBytes + columns + SpanCount * 2 ) );
	}

	/// <summary>
	/// What the codec made of the bytes of WriteTo is kept with a sum over those bytes. The stages index by
	/// what a field says without looking again, so nothing a codec hands back is used unless it sums up to
	/// the same: then it is the field that went in.
	/// </summary>
	public ulong Checksum;

	/// <summary>
	/// Takes over what WriteTo wrote, reusing this object's arrays. Only the sizes are looked at.
	/// </summary>
	public bool Parse( ReadOnlySpan<byte> bytes )
	{
		SpanCount = 0;
		if ( bytes.Length < HeaderBytes ) return false;
		int size = BinaryPrimitives.ReadInt32LittleEndian( bytes );
		float originZ = BinaryPrimitives.ReadSingleLittleEndian( bytes.Slice( 4 ) );
		int spanCount = BinaryPrimitives.ReadInt32LittleEndian( bytes.Slice( 8 ) );
		if ( size < 1 || size > 4096 || spanCount < 0 || !float.IsFinite( originZ ) ) return false;
		int columns = size * size;
		if ( bytes.Length != HeaderBytes + columns + (long)spanCount * 6 ) return false;

		if ( Counts.Length < columns ) Counts = new byte[columns];
		if ( Floors.Length < spanCount ) Floors = new ushort[spanCount];
		if ( Cons.Length < spanCount ) Cons = new uint[spanCount];
		bytes.Slice( HeaderBytes, columns ).CopyTo( Counts );
		bytes.Slice( HeaderBytes + columns, spanCount * 2 ).CopyTo( MemoryMarshal.AsBytes( Floors.AsSpan( 0, spanCount ) ) );
		bytes.Slice( HeaderBytes + columns + spanCount * 2, spanCount * 4 ).CopyTo( MemoryMarshal.AsBytes( Cons.AsSpan( 0, spanCount ) ) );

		Size = size;
		OriginZ = originZ;
		SpanCount = spanCount;
		return true;
	}
}
