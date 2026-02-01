using System.Runtime.InteropServices;

namespace NativeEngine;

/// <summary>
/// Mimmicks the engine internal CreateInterface system, allowing us to 
/// get the interfaces without asking native.
/// </summary>
internal static class CreateInterface
{
	static Dictionary<string, IntPtr> loadedModules = new();

	//this somewhat mimics V_GetFileExtension(DLL_EXT_STRING) back on Source 1
	static string NativizeModuleName( string abstractDLL )
	{
		if ( OperatingSystem.IsWindows() )
		{
			return $"{abstractDLL}.dll";
		}
		else if ( OperatingSystem.IsLinux() )
		{
			return $"lib{abstractDLL}.so";
		}
		else if ( OperatingSystem.IsMacOS() )
		{
			return $"lib{abstractDLL}.dylib";
		}
		else
		{
			throw new Exception( "Cannot nativize the module name." );
		}
		;
	}

	static IntPtr LoadModule( string dll )
	{
		var nativizedDLL = NativizeModuleName( dll );
		if ( loadedModules.TryGetValue( dll, out var module ) )
			return module;

		if ( !NativeLibrary.TryLoad( NetCore.NativeDllPath + nativizedDLL, out module ) )
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
