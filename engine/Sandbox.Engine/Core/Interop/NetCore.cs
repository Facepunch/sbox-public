internal static class NetCore
{
	/// <summary>
	/// Interop will try to load dlls from this path, e.g bin/win64/
	/// </summary>
	internal static string NativeDllPath { get; set; } = "bin/win64/";


	internal static string NativizeModuleName( string abstractDLL, bool shouldAlterLibName= true )
	{
		if ( OperatingSystem.IsWindows() )
		{
			return $"{abstractDLL}.dll";
		}
		else if ( OperatingSystem.IsLinux() )
		{
			if(shouldAlterLibName)
				return $"lib{abstractDLL}.so";
			else
				return $"{abstractDLL}.so";
		}
		else if ( OperatingSystem.IsMacOS() )
		{
			if(shouldAlterLibName)
				return $"lib{abstractDLL}.dylib";
			else
				return $"{abstractDLL}.dylib";
		}
		else
		{
			throw new Exception( "Cannot nativize the module name." );
		}
		;
	}

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

		// engine is always initialized
		Managed.SandboxEngine.NativeInterop.Initialize();

		// set engine paths etc
		NativeEngine.EngineGlobal.Plat_SetModuleFilename( $"{gameFolder}\\sbox.exe" );
		NativeEngine.EngineGlobal.Plat_SetCurrentDirectory( $"{gameFolder}" );
	}
}
