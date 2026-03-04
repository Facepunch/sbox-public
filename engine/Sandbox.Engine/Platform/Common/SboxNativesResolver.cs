using System.Reflection;
using System.Runtime.InteropServices;
using NativeEngine;
using SkiaSharp;

namespace Sandbox;

public class SboxNativesResolver
{
	private static readonly Dictionary<string, IntPtr> cachedLibHandles = new Dictionary<string, IntPtr>();
	private static bool isSDL3System = false;
	public static void SetupResolvers()
	{
		Assembly skiaSharpAssebmlyRef = typeof( SKAlphaType ).Assembly,
				harfBuzzSharpAssemblyRef = typeof( HarfBuzzSharp.Font ).Assembly,
				selfAssembly = typeof( SboxNativesResolver ).Assembly;
		NativeLibrary.SetDllImportResolver( skiaSharpAssebmlyRef, SkiaSharpImportResolver );
		NativeLibrary.SetDllImportResolver( harfBuzzSharpAssemblyRef, HarfBuzzSharpImportResolver );
		NativeLibrary.SetDllImportResolver( selfAssembly, SelfImportResolver );
	}


	~SboxNativesResolver()
	{
		foreach ( KeyValuePair<string, IntPtr> libHandle in cachedLibHandles )
		{
			NativeLibrary.Free( libHandle.Value );
		}
	}


	private static IntPtr SelfImportResolver( string libraryName, Assembly assembly, DllImportSearchPath? searchPath )
	{

		try
		{
			
			//NOTE: This will hijack every DllImport using this!
			if ( cachedLibHandles.TryGetValue( libraryName, out IntPtr outPtr ) )
			{
				return outPtr;
			}
			IntPtr libHandle;
			libHandle = NativeLibrary.Load( $"{NetCore.NativeDllPath}/${NetCore.NativizeModuleName(libraryName)}" );
			cachedLibHandles.Add( libraryName, libHandle );
			return libHandle;
		} catch (DllNotFoundException)
		{
			//That's fine. Pull from system.
			return IntPtr.Zero;
		}
	}
	//TODO: Rename these files.
	private static IntPtr HarfBuzzSharpImportResolver( string libraryName, Assembly assembly, DllImportSearchPath? searchPath )
	{
		if ( libraryName == "libHarfBuzzSharp" )
		{
			if ( cachedLibHandles.TryGetValue( libraryName, out IntPtr outPtr ) )
			{
				return outPtr;
			}
			IntPtr libHandle;
			libHandle = NativeLibrary.Load( $"{NetCore.NativeDllPath}/${NetCore.NativizeModuleName(libraryName,false)}" );
			cachedLibHandles.Add( libraryName, libHandle );
			return libHandle;
		}
		return IntPtr.Zero;
	}


	private static IntPtr SkiaSharpImportResolver( string libraryName, Assembly assembly, DllImportSearchPath? searchPath )
	{

		if ( libraryName == "libSkiaSharp" )
		{
			if ( cachedLibHandles.TryGetValue( libraryName, out IntPtr outPtr ) )
			{
				return outPtr;
			}
			IntPtr libHandle;
			libHandle = NativeLibrary.Load( $"{NetCore.NativeDllPath}/${NetCore.NativizeModuleName(libraryName,false)}" );
			cachedLibHandles.Add( libraryName, libHandle );
			return libHandle;
		}
		return IntPtr.Zero;
	}

}
