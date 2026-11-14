using System.Buffers.Binary;
using System.IO;

namespace Borealis.Source2.Compression;

/// <summary>
/// Decoders for meshoptimizer's vertex and index buffer codecs (<c>meshopt_decodeVertexBuffer</c>,
/// <c>meshopt_decodeIndexBuffer</c>, <c>src/thirdparty/meshoptimizer/src/vertexcodec.cpp</c> and <c>indexcodec.cpp</c>),
/// which compiled models store their vertex and index buffers in. Vertex format versions 0 and 1 (headers
/// <c>0xA0</c>, <c>0xA1</c>) and index format versions 0 and 1 (<c>0xE0</c>, <c>0xE1</c>), the scalar paths.
/// </summary>
public static class Meshopt
{
	const int VertexBlockSizeBytes = 8192;
	const int VertexBlockMaxSize = 256;
	const int ByteGroupSize = 16;
	const int ByteGroupDecodeLimit = 24;
	const int TailMinSizeV0 = 32;
	const int TailMinSizeV1 = 24;

	static readonly int[] BitsV0 = [0, 2, 4, 8];
	static readonly int[] BitsV1 = [0, 1, 2, 4, 8];

	/// <summary>
	/// Decode <paramref name="count"/> vertices of <paramref name="stride"/> bytes (a multiple of 4, at most 256).
	/// </summary>
	public static byte[] DecodeVertexBuffer( ReadOnlySpan<byte> source, int count, int stride )
	{
		if ( stride <= 0 || stride > 256 || stride % 4 != 0 ) throw new InvalidDataException( $"Vertex size {stride} can't be meshopt encoded" );
		if ( source.Length < 1 ) throw new InvalidDataException( "Empty meshopt vertex buffer" );

		var header = source[0];
		if ( (header & 0xF0) != 0xA0 ) throw new InvalidDataException( $"Not a meshopt vertex buffer (header {header:X2})" );
		var version = header & 0x0F;
		if ( version > 1 ) throw new InvalidDataException( $"Meshopt vertex codec version {version} isn't supported" );

		var tailSize = stride + (version == 0 ? 0 : stride / 4);
		var tailPadded = Math.Max( tailSize, version == 0 ? TailMinSizeV0 : TailMinSizeV1 );
		if ( source.Length - 1 < tailPadded ) throw new InvalidDataException( "Meshopt vertex buffer is too short" );

		var tail = source[^tailSize..];
		Span<byte> lastVertex = stackalloc byte[256];
		tail[..stride].CopyTo( lastVertex );
		var channels = version == 0 ? default : tail.Slice( stride, stride / 4 );

		var output = new byte[count * stride];
		var blockSize = Math.Min( (VertexBlockSizeBytes / stride) & ~(ByteGroupSize - 1), VertexBlockMaxSize );
		var data = 1;
		var end = source.Length;
		var buffer = new byte[VertexBlockMaxSize * 4];
		var transposed = new byte[VertexBlockSizeBytes];

		for ( var offset = 0; offset < count; offset += blockSize )
		{
			var vertices = Math.Min( blockSize, count - offset );
			data = DecodeVertexBlock( source, data, end, output.AsSpan( offset * stride, vertices * stride ), vertices, stride, lastVertex, channels, version, buffer, transposed );
		}

		if ( end - data != tailPadded ) throw new InvalidDataException( "Meshopt vertex buffer has data left over" );
		return output;
	}

	static int DecodeVertexBlock( ReadOnlySpan<byte> source, int data, int end, Span<byte> output, int count, int stride, Span<byte> lastVertex, ReadOnlySpan<byte> channels, int version, byte[] buffer, byte[] transposed )
	{
		var aligned = (count + ByteGroupSize - 1) & ~(ByteGroupSize - 1);
		var controlSize = version == 0 ? 0 : stride / 4;
		if ( end - data < controlSize ) throw new InvalidDataException( "Meshopt vertex block is truncated" );
		var control = data;
		data += controlSize;

		for ( int k = 0; k < stride; k += 4 )
		{
			var controlByte = version == 0 ? 0 : source[control + k / 4];
			for ( int j = 0; j < 4; j++ )
			{
				var mode = (controlByte >> (j * 2)) & 3;
				var target = buffer.AsSpan( j * count, count );
				if ( mode == 3 )
				{
					// Literal bytes
					if ( end - data < count ) throw new InvalidDataException( "Meshopt vertex block is truncated" );
					source.Slice( data, count ).CopyTo( target );
					data += count;
				}
				else if ( mode == 2 )
				{
					target.Clear();
				}
				else
				{
					data = DecodeBytes( source, data, end, buffer.AsSpan( j * count ), aligned, version == 0 ? BitsV0 : BitsV1.AsSpan( mode ) );
				}
			}

			var channel = version == 0 ? 0 : channels[k / 4];
			switch ( channel & 3 )
			{
				case 0:
					DecodeDeltas( buffer, transposed.AsSpan( k ), count, stride, lastVertex[k..], 1, xor: false, 0 );
					break;
				case 1:
					DecodeDeltas( buffer, transposed.AsSpan( k ), count, stride, lastVertex[k..], 2, xor: false, 0 );
					break;
				case 2:
					DecodeDeltas( buffer, transposed.AsSpan( k ), count, stride, lastVertex[k..], 4, xor: true, (32 - (channel >> 4)) & 31 );
					break;
				default:
					throw new InvalidDataException( $"Meshopt vertex channel {channel} isn't valid" );
			}
		}

		transposed.AsSpan( 0, count * stride ).CopyTo( output );
		transposed.AsSpan( stride * (count - 1), stride ).CopyTo( lastVertex );
		return data;
	}

	/// <summary>
	/// Byte groups of 16, each 0, 1, 2, 4 or 8 bits per byte (by a 2-bit header per group), with values at the largest
	/// code escaping to a whole byte that follows the group.
	/// </summary>
	static int DecodeBytes( ReadOnlySpan<byte> source, int data, int end, Span<byte> output, int size, ReadOnlySpan<int> bits )
	{
		var headerSize = (size / ByteGroupSize + 3) / 4;
		if ( end - data < headerSize ) throw new InvalidDataException( "Meshopt byte groups are truncated" );
		var header = data;
		data += headerSize;

		for ( int i = 0; i < size; i += ByteGroupSize )
		{
			if ( end - data < ByteGroupDecodeLimit ) throw new InvalidDataException( "Meshopt byte groups are truncated" );
			var group = i / ByteGroupSize;
			var bitsIndex = (source[header + group / 4] >> ((group % 4) * 2)) & 3;
			data = DecodeBytesGroup( source, data, output.Slice( i, ByteGroupSize ), bits[bitsIndex] );
		}

		return data;
	}

	static int DecodeBytesGroup( ReadOnlySpan<byte> source, int data, Span<byte> output, int bits )
	{
		switch ( bits )
		{
			case 0:
				output.Clear();
				return data;
			case 8:
				source.Slice( data, ByteGroupSize ).CopyTo( output );
				return data + ByteGroupSize;
		}

		// Packed values: high bits first within each byte; 1-bit groups store each byte's bits reversed
		var packedBytes = ByteGroupSize * bits / 8;
		var escape = data + packedBytes;
		var max = (1 << bits) - 1;
		var written = 0;
		for ( int b = 0; b < packedBytes; b++ )
		{
			int value = source[data + b];
			if ( bits == 1 ) value = ReverseBits( (byte)value );
			for ( int shift = 8 - bits; shift >= 0; shift -= bits )
			{
				var code = (value >> shift) & max;
				output[written++] = code == max ? source[escape++] : (byte)code;
			}
		}

		return escape;
	}

	static byte ReverseBits( byte value ) => (byte)((((ulong)value * 0x80200802ul) & 0x0884422110ul) * 0x0101010101ul >> 32);

	/// <summary>
	/// Undo the deltas of one 4-byte column: as bytes, shorts (zigzag deltas) or a 32-bit xor of a rotated value.
	/// </summary>
	static void DecodeDeltas( byte[] buffer, Span<byte> output, int count, int stride, ReadOnlySpan<byte> lastVertex, int size, bool xor, int rotate )
	{
		var source = 0;
		for ( int k = 0; k < 4; k += size )
		{
			uint previous = 0;
			for ( int j = 0; j < size; j++ ) previous |= (uint)lastVertex[k + j] << (8 * j);

			var vertex = k;
			for ( int i = 0; i < count; i++ )
			{
				uint value = 0;
				for ( int j = 0; j < size; j++ ) value |= (uint)buffer[source + i + count * j] << (8 * j);

				if ( xor )
				{
					value = ((value << rotate) | (value >> ((32 - rotate) & 31))) ^ previous;
				}
				else
				{
					var mask = size == 4 ? uint.MaxValue : (1u << (size * 8)) - 1;
					value = ((0 - (value & 1)) ^ (value >> 1)) + previous;
					value &= mask;
				}

				for ( int j = 0; j < size; j++ ) output[vertex + j] = (byte)(value >> (j * 8));
				previous = value;
				vertex += stride;
			}

			source += count * size;
		}
	}

	/// <summary>
	/// Decode a triangle list of <paramref name="count"/> indices, each <paramref name="indexSize"/> bytes (2 or 4).
	/// </summary>
	public static byte[] DecodeIndexBuffer( ReadOnlySpan<byte> source, int count, int indexSize )
	{
		if ( count % 3 != 0 ) throw new InvalidDataException( $"Meshopt index buffers hold triangles; {count} indices isn't" );
		if ( indexSize is not (2 or 4) ) throw new InvalidDataException( $"Index size {indexSize} can't be meshopt encoded" );
		if ( source.Length < 1 + count / 3 + 16 ) throw new InvalidDataException( "Meshopt index buffer is too short" );
		if ( (source[0] & 0xF0) != 0xE0 ) throw new InvalidDataException( $"Not a meshopt index buffer (header {source[0]:X2})" );
		var version = source[0] & 0x0F;
		if ( version > 1 ) throw new InvalidDataException( $"Meshopt index codec version {version} isn't supported" );

		Span<uint> edgeA = stackalloc uint[16];
		Span<uint> edgeB = stackalloc uint[16];
		Span<uint> vertexFifo = stackalloc uint[16];
		edgeA.Fill( uint.MaxValue );
		edgeB.Fill( uint.MaxValue );
		vertexFifo.Fill( uint.MaxValue );
		int edgeOffset = 0, vertexOffset = 0;
		uint next = 0, last = 0;
		var fecMax = version >= 1 ? 13 : 15;

		var output = new byte[count * indexSize];
		var written = 0;
		var code = 1;
		var codeEnd = code + count / 3;
		var data = codeEnd;
		var safeEnd = source.Length - 16;
		var codeAux = source[safeEnd..];

		void PushVertex( Span<uint> fifo, uint v, ref int offset, int advance = 1 )
		{
			fifo[offset] = v;
			offset = (offset + advance) & 15;
		}

		void PushEdge( Span<uint> a, Span<uint> b, uint ea, uint eb, ref int offset )
		{
			a[offset] = ea;
			b[offset] = eb;
			offset = (offset + 1) & 15;
		}

		void Write( Span<byte> into, ref int at, uint a, uint b, uint c )
		{
			if ( indexSize == 2 )
			{
				BinaryPrimitives.WriteUInt16LittleEndian( into[at..], (ushort)a );
				BinaryPrimitives.WriteUInt16LittleEndian( into[(at + 2)..], (ushort)b );
				BinaryPrimitives.WriteUInt16LittleEndian( into[(at + 4)..], (ushort)c );
				at += 6;
			}
			else
			{
				BinaryPrimitives.WriteUInt32LittleEndian( into[at..], a );
				BinaryPrimitives.WriteUInt32LittleEndian( into[(at + 4)..], b );
				BinaryPrimitives.WriteUInt32LittleEndian( into[(at + 8)..], c );
				at += 12;
			}
		}

		while ( code < codeEnd )
		{
			var codeTri = source[code++];
			if ( codeTri < 0xF0 )
			{
				// An edge from the edge fifo, and a vertex that's new, from the vertex fifo or free
				var fe = codeTri >> 4;
				var slot = (edgeOffset - 1 - fe) & 15;
				var a = edgeA[slot];
				var b = edgeB[slot];
				uint c;

				var fec = codeTri & 15;
				if ( fec < fecMax )
				{
					var cf = vertexFifo[(vertexOffset - 1 - fec) & 15];
					c = fec == 0 ? next : cf;
					var fec0 = fec == 0 ? 1 : 0;
					next += (uint)fec0;
					PushVertex( vertexFifo, c, ref vertexOffset, fec0 );
				}
				else
				{
					if ( data > safeEnd ) throw new InvalidDataException( "Meshopt index buffer is truncated" );
					last = c = fec != 15 ? last + (uint)(fec * 2 - 27) : DecodeIndex( source, ref data, last );
					PushVertex( vertexFifo, c, ref vertexOffset );
				}

				PushEdge( edgeA, edgeB, c, b, ref edgeOffset );
				PushEdge( edgeA, edgeB, a, c, ref edgeOffset );
				Write( output, ref written, a, b, c );
			}
			else if ( codeTri < 0xFE )
			{
				// Three vertices, each new or from the vertex fifo, by the code aux table
				var aux = codeAux[codeTri & 15];
				var feb = aux >> 4;
				var fec = aux & 15;

				var a = next++;
				var bf = vertexFifo[(vertexOffset - feb) & 15];
				var b = feb == 0 ? next : bf;
				var feb0 = feb == 0 ? 1 : 0;
				next += (uint)feb0;

				var cf = vertexFifo[(vertexOffset - fec) & 15];
				var c = fec == 0 ? next : cf;
				var fec0 = fec == 0 ? 1 : 0;
				next += (uint)fec0;

				Write( output, ref written, a, b, c );
				PushVertex( vertexFifo, a, ref vertexOffset );
				PushVertex( vertexFifo, b, ref vertexOffset, feb0 );
				PushVertex( vertexFifo, c, ref vertexOffset, fec0 );
				PushEdge( edgeA, edgeB, b, a, ref edgeOffset );
				PushEdge( edgeA, edgeB, c, b, ref edgeOffset );
				PushEdge( edgeA, edgeB, a, c, ref edgeOffset );
			}
			else
			{
				// The same with a full code aux byte, and free indices
				if ( data > safeEnd ) throw new InvalidDataException( "Meshopt index buffer is truncated" );
				var aux = source[data++];
				var fea = codeTri == 0xFE ? 0 : 15;
				var feb = aux >> 4;
				var fec = aux & 15;
				if ( aux == 0 ) next = 0;

				var a = fea == 0 ? next++ : 0;
				var b = feb == 0 ? next++ : vertexFifo[(vertexOffset - feb) & 15];
				var c = fec == 0 ? next++ : vertexFifo[(vertexOffset - fec) & 15];

				if ( fea == 15 ) last = a = DecodeIndex( source, ref data, last );
				if ( feb == 15 ) last = b = DecodeIndex( source, ref data, last );
				if ( fec == 15 ) last = c = DecodeIndex( source, ref data, last );

				Write( output, ref written, a, b, c );
				PushVertex( vertexFifo, a, ref vertexOffset );
				PushVertex( vertexFifo, b, ref vertexOffset, feb == 0 || feb == 15 ? 1 : 0 );
				PushVertex( vertexFifo, c, ref vertexOffset, fec == 0 || fec == 15 ? 1 : 0 );
				PushEdge( edgeA, edgeB, b, a, ref edgeOffset );
				PushEdge( edgeA, edgeB, c, b, ref edgeOffset );
				PushEdge( edgeA, edgeB, a, c, ref edgeOffset );
			}
		}

		if ( data != safeEnd ) throw new InvalidDataException( "Meshopt index buffer has data left over" );
		return output;
	}

	/// <summary>A zigzag delta from <paramref name="last"/>, as up to five 7-bit groups.</summary>
	static uint DecodeIndex( ReadOnlySpan<byte> source, ref int data, uint last )
	{
		uint value = source[data++];
		if ( value >= 128 )
		{
			value &= 127;
			var shift = 7;
			for ( int i = 0; i < 4; i++ )
			{
				var group = source[data++];
				value |= (uint)(group & 127) << shift;
				shift += 7;
				if ( group < 128 ) break;
			}
		}

		var delta = (value >> 1) ^ (uint)-(int)(value & 1);
		return last + delta;
	}
}
