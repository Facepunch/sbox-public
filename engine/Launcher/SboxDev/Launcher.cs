using Sandbox.Engine;
using System;
using System.Diagnostics;
using System.Linq;

namespace Sandbox;

public static class Launcher
{
	public static int Main()
	{
		if ( HasCommandLineSwitch( "-generatesolution" ) )
		{
			NetCore.InitializeInterop( Environment.CurrentDirectory );
			Bootstrap.InitMinimal( Environment.CurrentDirectory );
			Project.InitializeBuiltIn( false ).GetAwaiter().GetResult();
			Project.GenerateSolution().GetAwaiter().GetResult();
			Managed.SandboxEngine.NativeInterop.Free();
			EngineFileSystem.Shutdown();
			return 0;
		}

		if ( !HasCommandLineSwitch( "-project" ) && !HasCommandLineSwitch( "-test" ) )
		{
			// we pass the command line, so we can pass it on to the sbox-launcher (for -game etc)
			ProcessStartInfo info = new ProcessStartInfo( "sbox-launcher.exe", Environment.CommandLine );
			info.UseShellExecute = true;
			info.CreateNoWindow = true;
			info.WorkingDirectory = System.Environment.CurrentDirectory;

			Process.Start( info );
			return 0;
		}

		var appSystem = new EditorAppSystem();
		appSystem.Run();

		return 0;
	}

	private static bool HasCommandLineSwitch( string switchName )
	{
		return Environment.GetCommandLineArgs().Any( arg => arg.Equals( switchName, StringComparison.OrdinalIgnoreCase ) );
	}
}
