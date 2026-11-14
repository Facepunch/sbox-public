global using Sandbox;
global using System;
global using System.Collections.Generic;
global using System.Linq;
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
		if ( Lab.GoldenTruthRun.Enabled ) Lab.GoldenTruthRun.WatchValidation();

		// -shader-route: the core shaders' materials as their .shader, not the surfaces standing in for them (for A/B)
		if ( Lab.Args.Has( "-shader-route" ) ) Basalt.Backend.SurfaceShaders.ConvertByDefault = false;

		return BasaltLab.BasaltLabApp.Run();
	}
}
