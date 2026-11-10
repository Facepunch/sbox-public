global using Sandbox;
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using Sandbox.UI.Construct;
global using static Sandbox.Internal.GlobalSystemNamespace;

namespace Sandbox;

/// <summary>
/// Called by the shared launcher startup once the native dll paths are set up.
/// </summary>
public static class Launcher
{
	public static int Main()
	{
		// Before the engine starts, so validation messages from creating the device count too
		if ( SceneLab.GoldenTruthRun.Enabled ) SceneLab.GoldenTruthRun.WatchValidation();

		new SceneLab.SceneLabAppSystem().Run();
		return Environment.ExitCode;
	}
}
