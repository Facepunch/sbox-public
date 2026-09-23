using System;
using System.Linq;
using System.Text;

namespace Sandbox;

public static class Launcher
{
	public static int Main()
	{
		Console.OutputEncoding = new UTF8Encoding( encoderShouldEmitUTF8Identifier: false );

		return ProjectAutomationCli.Run(
			Environment.GetCommandLineArgs().Skip( 1 ).ToArray(),
			LauncherEnvironment.InvocationDirectory,
			LauncherEnvironment.GamePath,
			Console.Out,
			Console.Error );
	}
}
