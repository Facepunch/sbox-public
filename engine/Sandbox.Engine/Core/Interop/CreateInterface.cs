using System;
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

		var platformDll = GetPlatformDllName( dll );
		if ( !NativeLibrary.TryLoad( platformDll, out module ) )
			return default;

		loadedModules[dll] = module;
		return module;
	}

	static string GetPlatformDllName( string dll )
	{
		if ( OperatingSystem.IsWindows() ) return dll;
		if ( OperatingSystem.IsMacOS() ) return dll.Replace( ".dll", ".dylib" );
		if ( OperatingSystem.IsLinux() ) return dll.Replace( ".dll", ".so" );
		return dll;
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
