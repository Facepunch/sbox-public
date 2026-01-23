#if !WIN


using System.Reflection;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Sandbox;

public class SboxNativesResolver
{
    public static void SetupResolvers()
    {
        Assembly skiaSharpAssebmlyRef = typeof(SKAlphaType).Assembly, harfBuzzSharpAssemblyRef = typeof(HarfBuzzSharp.Font).Assembly;
        NativeLibrary.SetDllImportResolver(skiaSharpAssebmlyRef,SkiaSharpImportResolver);
        NativeLibrary.SetDllImportResolver(harfBuzzSharpAssemblyRef,HarfBuzzSharpImportResolver);
    }

//TODO: Rename these files.
	private static IntPtr HarfBuzzSharpImportResolver( string libraryName, Assembly assembly, DllImportSearchPath? searchPath )
	{
        if (libraryName == "libHarfBuzzSharp")
        {
            if(OperatingSystem.IsLinux())
            {
                return NativeLibrary.Load($"{NetCore.NativeDllPath}/libHarfBuzzSharp.so.0.60830.0");
            } else if (OperatingSystem.IsMacOS())
            {
                
            return NativeLibrary.Load($"{NetCore.NativeDllPath}/libHarfBuzzSharp.dylib");
            }
        }
		return IntPtr.Zero;
	}


	private static IntPtr SkiaSharpImportResolver( string libraryName, Assembly assembly, DllImportSearchPath? searchPath )
	{
        if (libraryName == "libSkiaSharp")
        {
            if(OperatingSystem.IsLinux())
            {
            return NativeLibrary.Load($"{NetCore.NativeDllPath}/libSkiaSharp.so.116.0.0");
            } else if (OperatingSystem.IsMacOS())
            {
                
            return NativeLibrary.Load($"{NetCore.NativeDllPath}/libSkiaSharp.dylib");
            }
        }
		return IntPtr.Zero;
	}

}
#endif