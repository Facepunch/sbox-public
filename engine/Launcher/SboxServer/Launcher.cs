using System;
using System.Threading.Tasks;

namespace Sandbox;

public static class Launcher
{
	public static int Main()
	{
		var appSystem = new DedicatedServerAppSystem();
		appSystem.Run();

		return 0;
	}
}

public class DedicatedServerAppSystem : AppSystem
{
	Sandbox.Diagnostics.Logger Log = new Diagnostics.Logger( "Console" );

	DedicatedServerConsole console;

	public override void Init()
	{
		LoadSteamDll();
		TestSystemRequirements();

		base.Init();

		CreateGame();

		var createInfo = new AppSystemCreateInfo()
		{
			WindowTitle = "s&box server",
			Flags = AppSystemFlags.IsDedicatedServer | AppSystemFlags.IsGameApp | AppSystemFlags.IsConsoleApp
		};

		InitGame( createInfo );

		// No game specified, show some help
		if ( !Environment.CommandLine.Contains( "game " ) )
		{
			PrintHelp();
		}

		// Redirecting console output (docker/wine/etc..) won't support this overlay, so don't bother with it
		if ( !Console.IsOutputRedirected )
		{
			console = new DedicatedServerConsole();
			console.Update();
		}
	}

	protected override bool RunFrame()
	{
		console?.Update();

		return base.RunFrame();
	}

	public override void Shutdown()
	{
		base.Shutdown();
	}

	void PrintHelp()
	{
		Log.Info( "Welcome to the s&box dedicated server. This is currently a work in progress." );
		Log.Info( "Submit any issues to https://github.com/facepunch/sbox-public/issues" );
		Log.Info( "" );
		Log.Info( "game <gameident> - load a game" );
		Log.Info( "game <gameident> <mapident> - load a game with a map" );
		Log.Info( "find <text> - find a concommand or convar" );
		Log.Info( "kick <id> - kick a player by name or steam id" );
		Log.Info( "status - current game status" );
		Log.Info( "quit - quit" );
		Log.Info( "" );
	}
}
