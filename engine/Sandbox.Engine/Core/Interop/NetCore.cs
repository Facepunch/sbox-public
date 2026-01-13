internal static class NetCore
{
	/// <summary>
	/// Interop will try to load dlls from this path, e.g bin/win64/
	/// </summary>
	internal static string NativeDllPath { get; set; } = string.Empty;

	/// <summary>
	/// From here we'll open the native dlls and inject our function pointers into them,
	/// and retrieve function pointers from them.
	/// </summary>
	internal static void InitializeInterop( string gameFolder )
	{
		// make sure currentdir to the game folder. This is just to setr a baseline for the rest
		// of the managed system to work with - since they can all assume CurrentDirectory is
		// where you would expect it to be instead of in the fucking bin folder.
		System.Environment.CurrentDirectory = gameFolder;

		//Unfortunately I have to duplicate logic from launcher.
		//The reason being in most cases NativeDllPath is injected from Sandbox.LaunchEnvironment,
		//but ShaderCompiler uses that property directly.
		if ( string.IsNullOrEmpty( NativeDllPath ) )
		{
			if ( OperatingSystem.IsWindows() )
			{
				NativeDllPath = "bin/win64/";
			}
			//Absolute paths for these two.
			else if ( OperatingSystem.IsLinux() )
			{
				NativeDllPath = $"{gameFolder}/bin/linuxsteamrt64/";
			}
			else if ( OperatingSystem.IsMacOS() )
			{
				//TODO: Also check for architecture here.
				NativeDllPath = $"{gameFolder}/bin/osxarm64/";
			}
			else
			{
				throw new Exception( "Unsupported platform for interop." );
			}
		}

		// engine is always initialized
		Managed.SandboxEngine.NativeInterop.Initialize();

		// set engine paths etc
		if ( OperatingSystem.IsWindows() )
			NativeEngine.EngineGlobal.Plat_SetModuleFilename( $"{gameFolder}\\sbox.exe" );
		else
			NativeEngine.EngineGlobal.Plat_SetModuleFilename( $"{gameFolder}/sbox" );
		NativeEngine.EngineGlobal.Plat_SetCurrentDirectory( $"{gameFolder}" );
	}
}
