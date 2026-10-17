using System.Net;
using System.Runtime.InteropServices;

namespace Steamworks;

internal static partial class Utility
{
	static internal T ToType<T>( this IntPtr ptr )
	{
		if ( ptr == IntPtr.Zero )
			return default;

		// (Steam's struct layout on this platform, not necessarily ours: NativeLayout)
		return (T)NativeLayout.Read( ptr, typeof( T ) );
	}

	static internal object ToType( this IntPtr ptr, System.Type t )
	{
		if ( ptr == IntPtr.Zero )
			return default;

		return NativeLayout.Read( ptr, t );
	}

	static internal uint Swap( uint x )
	{
		return ((x & 0x000000ff) << 24) +
			   ((x & 0x0000ff00) << 8) +
			   ((x & 0x00ff0000) >> 8) +
			   ((x & 0xff000000) >> 24);
	}

	static internal IPAddress Int32ToIp( uint ipAddress )
	{
		return new IPAddress( Swap( ipAddress ) );
	}
}
