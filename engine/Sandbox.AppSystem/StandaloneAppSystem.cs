using Sandbox.Engine;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sandbox;

public class StandaloneAppSystem : AppSystem
{
	private StandaloneManifest LoadManifest()
	{
		// Load game info from file
		var manifestPath = Path.Combine( Standalone.GamePath, Standalone.ManifestName );
		var manifestContents = File.ReadAllText( manifestPath );
		var properties = JsonSerializer.Deserialize<StandaloneManifest>( manifestContents );

		return properties;
	}

	public override void Init()
	{
		LoadSteamDll();

		base.Init();

		// Standalone setup
		Standalone.SetupFromManifest( LoadManifest() );

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

	private Task _standaloneLoadTask;

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
				_standaloneLoadTask.GetAwaiter().GetResult();
				_standaloneLoadTask = null;
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
