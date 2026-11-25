using System.Buffers.Binary;
using System.Numerics;

namespace Sandbox;

public static partial class SandboxSystemExtensions
{
	/// <summary>
	/// Returns a stable eight-character lowercase base36 hash suitable for filenames.
	/// Different GUIDs can produce the same hash.
	/// </summary>
	public static string ToShortHash( this Guid guid )
	{
		Span<byte> bytes = stackalloc byte[16];
		guid.TryWriteBytes( bytes, bigEndian: true, out _ );

		var high = BinaryPrimitives.ReadUInt64BigEndian( bytes );
		var low = BinaryPrimitives.ReadUInt64BigEndian( bytes[8..] );
		var folded = BitOperations.RotateLeft( high, 23 ) ^ low;

		return string.Create( 8, folded, static ( chars, value ) =>
		{
			for ( var i = chars.Length - 1; i >= 0; i-- )
			{
				chars[i] = CharList[(int)(value % 36)];
				value /= 36;
			}
		} );
	}
}
