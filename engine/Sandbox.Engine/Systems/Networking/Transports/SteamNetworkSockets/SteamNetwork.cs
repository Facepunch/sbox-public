using System.Runtime.InteropServices;

namespace Sandbox.Network;

internal static partial class SteamNetwork
{
	private static readonly DelegateFunctionPointer debugFunction = DelegateFunctionPointer.Get<DebugOutput_t>( DebugOutput );

	internal static void Initialize()
	{
		Glue.Networking.SetDebugFunction( Networking.Debug ? 4 : 0, debugFunction );
	}

	[UnmanagedFunctionPointer( CallingConvention.StdCall )]
	unsafe delegate void DebugOutput_t( int type, IntPtr msg );

	static void DebugOutput( int type, IntPtr msg )
	{
		var str = Interop.GetString( msg );
		Log.Info( $"SteamNetwork: {type}: {str}" );
	}

	/// <summary>
	/// This gets called by the SteamAPI, so only really need to call this in unit tests.
	/// </summary>
	internal static void RunCallbacks()
	{
		Glue.Networking.RunCallbacks();
	}
}
