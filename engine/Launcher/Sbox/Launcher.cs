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
		TestSystemRequirements();

		base.Init();

		CreateGame();
		CreateMenu();

		var createInfo = new AppSystemCreateInfo()
		{
			WindowTitle = "s&box",
			Flags = AppSystemFlags.IsGameApp
		};

		InitGame( createInfo );
	}
}
