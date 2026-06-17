internal static class NetCore
{
	static string DefaultNativeDllPath => true switch
	{
		_ when OperatingSystem.IsWindows() => "bin/win64/",
		_ when OperatingSystem.IsLinux() => "bin/linuxsteamrt64/",
		_ when OperatingSystem.IsMacOS() => "bin/osxarm64/",
		_ => throw new PlatformNotSupportedException()
	};

	/// <summary>
	/// Interop will try to load dlls from this path, e.g bin/win64/
	/// </summary>
	internal static string NativeDllPath { get; set; } = DefaultNativeDllPath;

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

		// Initialize native crash reporting (crashpad) as early as possible.
		if ( Sandbox.Engine.ErrorReporter.IsUsingSentry )
		{
			NativeErrorReporter.Init();
		}

		// set engine paths etc
		var exeName = OperatingSystem.IsWindows() ? "sbox.exe" : "sbox";
		NativeEngine.EngineGlobal.Plat_SetModuleFilename( System.IO.Path.Combine( gameFolder, exeName ) );
		NativeEngine.EngineGlobal.Plat_SetCurrentDirectory( $"{gameFolder}" );
	}
}
