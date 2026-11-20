using System;
using System.Threading.Tasks;

namespace Sandbox;

public static class Launcher
{
	public static int Main()
	{
		var appSystem = new GameAppSystem();
		appSystem.Run();

		return 0;
	}
}

public class GameAppSystem : AppSystem
{
	public override void Init()
	{
		DrawOnBorealisGpu();
		LoadSteamDll();
		base.Init();

		Environment.SetEnvironmentVariable( "SBOX_MODE", "BENCHMARK" );

		CreateGame();
		CreateMenu();

		// Disable asserts for all benchmarks
		NativeEngine.EngineGlobal.Plat_SetNoAssert();

		var createInfo = new AppSystemCreateInfo()
		{
			WindowTitle = "s&box benchmark",
			Flags = AppSystemFlags.IsGameApp
		};

		InitGame( createInfo );
	}
}
