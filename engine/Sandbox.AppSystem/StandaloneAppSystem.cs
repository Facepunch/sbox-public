using Sandbox.Engine;
using System;
using System.Threading.Tasks;

namespace Sandbox;

public class StandaloneAppSystem : AppSystem
{
	public override void Init()
	{
		LoadSteamDll();

		base.Init();

		// Everything about which game this is was written into the executable's resources by the exporter
		Standalone.LoadFromExecutable();

		Application.IsStandalone = true;
		Application.AppId = Standalone.Manifest.AppId;

		CreateGame();

		var createInfo = new AppSystemCreateInfo()
		{
			WindowTitle = Standalone.Manifest.Name,
			Flags = AppSystemFlags.IsGameApp | AppSystemFlags.IsStandaloneGame
		};

		if ( Utility.CommandLine.HasSwitch( "-headless" ) )
			createInfo.Flags |= AppSystemFlags.IsConsoleApp;

		InitGame( createInfo );

		LoadStandaloneGame();
	}

	private Task<bool> _standaloneLoadTask;

	private void LoadStandaloneGame()
	{
		_standaloneLoadTask = IGameInstanceDll.Current.LoadGamePackageAsync( Standalone.Manifest.Ident, GameLoadingFlags.Host, default );
	}

	protected override bool RunFrame()
	{

		EngineLoop.RunFrame( _appSystem, out bool wantsToQuit );
		// Still loading
		if ( _standaloneLoadTask is not null )
		{
			if ( _standaloneLoadTask.IsCompleted )
			{
				var loaded = _standaloneLoadTask.GetAwaiter().GetResult();
				_standaloneLoadTask = null;

				if ( !loaded )
				{
					log.Error( $"Failed to load standalone game {Standalone.Manifest.Ident}" );

					// A test run has to see this as a failure, not a clean exit after a broken load.
					// Shut down normally though - the native engine is still running under us.
					if ( Utility.CommandLine.HasSwitch( "-test-standalone" ) )
					{
						Environment.ExitCode = 1;
						Game.Close();
					}
				}
			}
		}
		// Quit next loop after load, if we are testing
		else if ( Utility.CommandLine.HasSwitch( "-test-standalone" ) )
		{
			Game.Close();
		}

		return !wantsToQuit;
	}
}
