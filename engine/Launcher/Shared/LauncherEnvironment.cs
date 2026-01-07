using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Sandbox;

public static class LauncherEnvironment
{
	/// <summary>
	/// The folder containing sbox.exe
	/// </summary>
	public static string GamePath { get; set; }

	/// <summary>
	/// The folder containing Sandbox.Engine.dll
	/// </summary>
	public static string ManagedDllPath { get; set; }

	public static string PlatformName
	{
		get
		{
			if ( OperatingSystem.IsWindows() )
				return "win64";
			if ( OperatingSystem.IsLinux() )
				return "linuxsteamrt64";
			if ( OperatingSystem.IsMacOS() )
				return "osxarm64";

			throw new Exception( "Unsupported platform" );
		}
	}

	public static void Init()
	{
		AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

		GamePath = AppContext.BaseDirectory;

		// this exe is in the bin folder
		if ( GamePath.EndsWith( System.IO.Path.Combine( "bin", PlatformName ) ) )
		{
			// go up two folders
			GamePath = System.IO.Path.GetDirectoryName( GamePath );
			GamePath = System.IO.Path.GetDirectoryName( GamePath );
		}

		// this exe is in the game folder
		ManagedDllPath = $"{GamePath}/bin/managed/";
		var nativeDllPath = $"{GamePath}/bin/{PlatformName}/";

		// make the game dir our current dir
		Environment.CurrentDirectory = GamePath;

		//
		// Allows unit tests and csproj to find the engine path.
		//
		if ( System.Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE", EnvironmentVariableTarget.User ) != GamePath )
		{
			System.Environment.SetEnvironmentVariable( "FACEPUNCH_ENGINE", GamePath, EnvironmentVariableTarget.User );
		}

		UpdateNativeDllPath( nativeDllPath );
	}

	private static void UpdateNativeDllPath( string nativeDllPath )
	{
		// WARNING: this calls into Sandbox.Engine.dll - so we need to put it in
		// this method, which is executed AFTER CurrentDomain_AssemblyResolve is set
		// so that managed can find the correct dll
		NetCore.NativeDllPath = nativeDllPath;

		//
		// Put our native dll path first so that when looking up native dlls we'll
		// always use the ones from our folder first
		//
		if (OperatingSystem.IsWindows())
		{
			var path = System.Environment.GetEnvironmentVariable( "PATH" );
			path = $"{nativeDllPath};{path}";
			System.Environment.SetEnvironmentVariable( "PATH", path );
		}
	}

	private static Assembly CurrentDomain_AssemblyResolve( object sender, ResolveEventArgs args )
	{
		var trim = args.Name.Split( ',' )[0];

		var name = $"{ManagedDllPath}/{trim}.dll";

		// dlls with resources inside appear as a different name
		name = name.Replace( ".resources.dll", ".dll" );

		if ( System.IO.File.Exists( name ) )
		{
			return Assembly.LoadFrom( name );
		}

		return null;
	}
}
