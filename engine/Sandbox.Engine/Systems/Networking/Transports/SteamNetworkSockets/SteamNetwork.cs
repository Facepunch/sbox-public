using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Sandbox.Network;

internal static partial class SteamNetwork
{
	private static readonly DelegateFunctionPointer freeDataCallback = DelegateFunctionPointer.Get<FreeDataCallback_t>( FreeDataCallback );
	private static readonly DelegateFunctionPointer debugFunction = DelegateFunctionPointer.Get<DebugOutput_t>( DebugOutput );

	public static readonly ConcurrentDictionary<IntPtr, GCHandle> PinnedBuffers = new();

	internal static void Initialize()
	{
		if ( Networking.Debug )
		{
			Glue.Networking.SetDebugFunction( 4, debugFunction );
		}

		Glue.Networking.SetFreeDataCallback( freeDataCallback );
	}

	[UnmanagedFunctionPointer( CallingConvention.StdCall )]
	unsafe delegate void DebugOutput_t( int type, IntPtr msg );

	[UnmanagedFunctionPointer( CallingConvention.StdCall )]
	unsafe delegate void FreeDataCallback_t( IntPtr msg );

	static void DebugOutput( int type, IntPtr msg )
	{
		var str = Interop.GetString( msg );
		Log.Info( $"SteamNetwork: {type}: {str}" );
	}

	static void FreeDataCallback( IntPtr data )
	{
		if ( !PinnedBuffers.TryRemove( data, out var handle ) )
			return;

		handle.Free();
	}
}
