using System.Reflection;
using System.Runtime.InteropServices;
using System.IO;

namespace Sandbox;

internal static class DLLImportResolver
{
	private static readonly HashSet<string> registeredAssemblies = new();

	internal static void SetupResolvers()
	{
		// Register for all currently loaded assemblies
		foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
			TryRegister( assembly );

		// Register for any assemblies loaded in the future
		AppDomain.CurrentDomain.AssemblyLoad += ( _, args ) => TryRegister( args.LoadedAssembly );
	}

	private static void TryRegister( Assembly assembly )
	{
		if ( assembly.IsDynamic )
			return;

		// SetDllImportResolver throws if called twice for the same assembly
		if ( !registeredAssemblies.Add( assembly.FullName ) )
			return;

		NativeLibrary.SetDllImportResolver( assembly, ResolveFromNativePath );
	}

	private static IntPtr ResolveFromNativePath( string libraryName, Assembly assembly, DllImportSearchPath? searchPath )
	{
		// Specifically for fucking steam_api without using a preprocessor if statement on Platform.cs
		// should try to aim to have one set of "managed" binaries without platform specific variants
		if ( libraryName == "steam_api64" && !OperatingSystem.IsWindows() )
		{
			libraryName = "libsteam_api";
		}

		var nativeName = true switch
		{
			_ when OperatingSystem.IsWindows() => $"{libraryName}.dll",
			_ when OperatingSystem.IsMacOS() => $"{libraryName}.dylib",
			_ when OperatingSystem.IsLinux() => $"{libraryName}.so",
			_ => libraryName
		};

		if ( NativeLibrary.TryLoad( Path.Combine( NetCore.NativeDllPath, nativeName ), out var handle ) )
		{
			return handle;
		}

		return IntPtr.Zero;
	}
}
