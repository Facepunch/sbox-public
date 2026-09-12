using Sandbox.Engine;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Sandbox;

public static class Launcher
{
	public static int Main()
	{
		if ( HasCommandLineSwitch( "-generatesolution" ) )
			return GenerateSolution();

		if ( !HasCommandLineSwitch( "-project" ) && !HasCommandLineSwitch( "-test" ) )
		{
			// Nothing to edit - hand over to the launcher. This path touches nothing from the
			// engine on purpose: the moment a method that uses it is jitted the whole engine
			// assembly loads, and that's tens of milliseconds the launcher is waiting on
			var launcher = Path.Combine( AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "sbox-launcher.exe" : "sbox-launcher" );

			// we pass the command line, so we can pass it on to the sbox-launcher (for -game etc)
			ProcessStartInfo info = new ProcessStartInfo( launcher, Environment.CommandLine );

			// Only let the shell start it on Windows - on Linux UseShellExecute goes through
			// xdg-open, which opens the launcher in a web browser rather than running it.
			info.UseShellExecute = OperatingSystem.IsWindows();
			info.CreateNoWindow = true;
			info.WorkingDirectory = System.Environment.CurrentDirectory;

			Process.Start( info );
			return 0;
		}

		return RunEditor();
	}

	// Kept out of Main so that jitting Main doesn't load the engine - see above

	[MethodImpl( MethodImplOptions.NoInlining )]
	static int GenerateSolution()
	{
		NetCore.InitializeInterop( Environment.CurrentDirectory );
		Bootstrap.InitMinimal( Environment.CurrentDirectory );
		Project.InitializeBuiltIn( false ).GetAwaiter().GetResult();
		Project.GenerateSolution().GetAwaiter().GetResult();
		Managed.SandboxEngine.NativeInterop.Free();
		EngineFileSystem.Shutdown();
		return 0;
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	static int RunEditor()
	{
		var appSystem = new EditorAppSystem();
		appSystem.Run();

		return 0;
	}

	private static bool HasCommandLineSwitch( string switchName )
	{
		return Environment.GetCommandLineArgs().Any( arg => arg.Equals( switchName, StringComparison.OrdinalIgnoreCase ) );
	}
}
