namespace Sandbox;

public static class Launcher
{
	public static int Main()
	{
		var appSystem = new StandaloneAppSystem();
		appSystem.Run();

		// Non-zero when a -test-standalone run failed to load the game
		return System.Environment.ExitCode;
	}
}
