using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Nav.Build;

internal static class Fnv
{
	/// <summary>
	/// A sum over the bytes that tells whether a codec handed back the bytes it was given. Eight bytes a
	/// step in four independent lanes, each kept with the running sum of its sums, which is what makes
	/// the place count: a changed, missing, added or moved run of bytes gives another sum. It is sums and
	/// not a hash, so changes made to cancel pass: add d, take 2d and add d at three words of one lane,
	/// four apart, and both of its sums are what they were. It guards against a faulty codec, not
	/// against intent. Only compared within one process.
	/// </summary>
	public static ulong Checksum( ReadOnlySpan<byte> bytes )
	{
		var words = MemoryMarshal.Cast<byte, ulong>( bytes );
		ulong a0 = 0, a1 = 0, a2 = 0, a3 = 0, b0 = 0, b1 = 0, b2 = 0, b3 = 0;
		int i = 0;
		for ( ; i + 4 <= words.Length; i += 4 )
		{
			a0 += words[i];
			b0 += a0;
			a1 += words[i + 1];
			b1 += a1;
			a2 += words[i + 2];
			b2 += a2;
			a3 += words[i + 3];
			b3 += a3;
		}
		for ( ; i < words.Length; i++ )
		{
			a0 += words[i];
			b0 += a0;
		}

		// The last bytes that do not fill a word, and the length
		ulong hash = Hash( bytes.Slice( words.Length * 8 ) ) ^ (ulong)bytes.Length;
		const ulong prime = 1099511628211ul;
		hash = (hash ^ a0) * prime;
		hash = (hash ^ b0) * prime;
		hash = (hash ^ a1) * prime;
		hash = (hash ^ b1) * prime;
		hash = (hash ^ a2) * prime;
		hash = (hash ^ b2) * prime;
		hash = (hash ^ a3) * prime;
		hash = (hash ^ b3) * prime;
		return hash;
	}

	/// <summary>
	/// FNV-1a over the bytes, eight at a time. The same in every process and on every machine, so it can
	/// be kept in a bake. Never 0.
	/// </summary>
	public static ulong Hash( ReadOnlySpan<byte> bytes )
	{
		ulong hash = 14695981039346656037ul ^ (ulong)bytes.Length;
		int i = 0;
		for ( ; i + 8 <= bytes.Length; i += 8 ) hash = (hash ^ BinaryPrimitives.ReadUInt64LittleEndian( bytes.Slice( i, 8 ) )) * 1099511628211ul;
		for ( ; i < bytes.Length; i++ ) hash = (hash ^ bytes[i]) * 1099511628211ul;
		return hash == 0 ? 1 : hash;
	}
}
