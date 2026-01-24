#if !WIN


using System.Reflection;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Sandbox;

public class SboxNativesResolver
{
	private static Dictionary<string,IntPtr> cachedLibHandles = new Dictionary<string, IntPtr>();
	public static void SetupResolvers()
	{
		Assembly skiaSharpAssebmlyRef = typeof( SKAlphaType ).Assembly, harfBuzzSharpAssemblyRef = typeof( HarfBuzzSharp.Font ).Assembly;
		NativeLibrary.SetDllImportResolver( skiaSharpAssebmlyRef, SkiaSharpImportResolver );
		NativeLibrary.SetDllImportResolver( harfBuzzSharpAssemblyRef, HarfBuzzSharpImportResolver );
	}

	//TODO: Rename these files.
	private static IntPtr HarfBuzzSharpImportResolver( string libraryName, Assembly assembly, DllImportSearchPath? searchPath )
	{
		if ( libraryName == "libHarfBuzzSharp" )
		{
			if ( cachedLibHandles.ContainsKey( libraryName ) )
			{
				return cachedLibHandles[libraryName];
			}
			IntPtr libHandle;
			if ( OperatingSystem.IsLinux() )
			{
				libHandle =  NativeLibrary.Load( $"{NetCore.NativeDllPath}/libHarfBuzzSharp.so.0.60830.0" );
				cachedLibHandles.Add(libraryName,libHandle);
				return libHandle;
			}
			else if ( OperatingSystem.IsMacOS() )
			{
				libHandle =  NativeLibrary.Load( $"{NetCore.NativeDllPath}/libHarfBuzzSharp.dylib" );
				cachedLibHandles.Add(libraryName,libHandle);
				return libHandle;
			}
		}
		return IntPtr.Zero;
	}


	private static IntPtr SkiaSharpImportResolver( string libraryName, Assembly assembly, DllImportSearchPath? searchPath )
	{
		if ( libraryName == "libSkiaSharp" )
		{
			if ( cachedLibHandles.ContainsKey( libraryName ) )
			{
				return cachedLibHandles[libraryName];
			}
			IntPtr libHandle;
			if ( OperatingSystem.IsLinux() )
			{
				libHandle = NativeLibrary.Load( $"{NetCore.NativeDllPath}/libSkiaSharp.so.116.0.0" );
				cachedLibHandles.Add(libraryName,libHandle);
				return libHandle;
			}
			else if ( OperatingSystem.IsMacOS() )
			{
				libHandle = NativeLibrary.Load( $"{NetCore.NativeDllPath}/libSkiaSharp.dylib" );
				cachedLibHandles.Add(libraryName,libHandle);
				return libHandle;
			}
		}
		return IntPtr.Zero;
	}

}
#endif
