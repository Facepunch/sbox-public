using System.Runtime.InteropServices;

namespace NativeEngine;

/// <summary>
/// Mimmicks the engine internal CreateInterface system, allowing us to 
/// get the interfaces without asking native.
/// </summary>
internal static class CreateInterface
{
	static Dictionary<string, IntPtr> loadedModules = new();

	static IntPtr LoadModule( string dll )
	{
		if ( loadedModules.TryGetValue( dll, out var module ) )
			return module;

		// Callers spell these the Windows way. Map it to whatever this platform calls the
		// library, and fall back to looking beside the engine's other natives - a bare name
		// resolves against the runtime's search path, which isn't where ours live.
		var nativeName = Sandbox.Interop.GetNativeLibraryName( dll );

		if ( !NativeLibrary.TryLoad( nativeName, out module ) &&
			 !NativeLibrary.TryLoad( System.IO.Path.Combine( NetCore.NativeDllPath, nativeName ), out module ) )
			return default;

		loadedModules[dll] = module;
		return module;
	}

	[UnmanagedFunctionPointer( CallingConvention.Cdecl )]
	public delegate IntPtr CreateInterfaceFn( string pName, IntPtr pReturnCode );

	public static IntPtr GetCreateInterface( string dll )
	{
		IntPtr module = LoadModule( dll );
		if ( module == IntPtr.Zero ) return default;

		return NativeLibrary.GetExport( module, "CreateInterface" );
	}

	internal static IntPtr LoadInterface( string dll, string interfacename )
	{
		var createInterface = GetCreateInterface( dll );
		if ( createInterface == IntPtr.Zero )
			return default;

		CreateInterfaceFn fn = Marshal.GetDelegateForFunctionPointer<CreateInterfaceFn>( createInterface );
		return fn( interfacename, default );
	}
}
