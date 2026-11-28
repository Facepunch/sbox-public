using System;
using System.Runtime.InteropServices;

namespace SteamNative;

internal partial struct Buffer
{
	/// <summary>
	/// Views the native values until the buffer is disposed.
	/// </summary>
	internal unsafe ReadOnlySpan<T> AsSpan<T>() where T : unmanaged
	{
		if ( IsNull ) return [];

		var data = GetData( out var size );
		return MemoryMarshal.Cast<byte, T>( new ReadOnlySpan<byte>( data, size ) );
	}

	internal byte[] ToArray() => AsSpan<byte>().ToArray();
}
