using Sandbox.Engine;

namespace Sandbox.Network;

internal sealed class ProcessClientEnvironment : INetworkClientEnvironment
{
	public static ProcessClientEnvironment Instance { get; } = new();

	public bool ReadsReplicatedConVars => true;

	public async Task<bool> LoadGamePackageAsync( NetworkSystem system, ServerInfo msg )
	{
		if ( !string.IsNullOrEmpty( msg.GamePackage ) )
		{
			LoadingScreen.Title = $"Loading {msg.GamePackage}";

			var flags = GameLoadingFlags.Remote | GameLoadingFlags.Reload;
			if ( system.IsDeveloperHost ) flags |= GameLoadingFlags.Developer;

			if ( !Application.IsStandalone )
			{
				LaunchArguments.Map = msg.Map;

				bool success = await IGameInstanceDll.Current.LoadGamePackageAsync( msg.GamePackage, flags, default );
				if ( !success )
				{
					Networking.Disconnect();
					return false;
				}
			}
		}

		if ( IGameInstanceDll.Current is not null )
		{
			system.TypeLibrary = IGameInstanceDll.Current.TypeLibrary;
		}

		return true;
	}

	public async Task<bool> MountMapAsync( NetworkSystem system, ServerInfo msg )
	{
		if ( !Mounting.MountUtility.TryParse( msg.Map, out string ident ) )
			return true;

		var mount = Mounting.Directory.Get( ident );
		if ( mount is null || !mount.IsInstalled )
		{
			IGameInstanceDll.Current.Disconnect( $"Mount is not available: {ident}" );
			Networking.Disconnect();
			return false;
		}

		LoadingScreen.Title = $"Mounting {mount.Title}";
		await Mounting.Directory.Mount( ident );

		var scenefile = SceneFile.Load( msg.MapName );
		if ( scenefile is null )
		{
			IGameInstanceDll.Current.Disconnect( $"Map not found: {msg.MapName}" );
			Networking.Disconnect();
			return false;
		}

		return true;
	}

	public void InstallNetworkTables( NetworkSystem system )
	{
		IGameInstanceDll.Current?.InstallNetworkTables( system );
	}

	public async Task<bool> LoadNetworkTablesAsync( NetworkSystem system )
	{
		if ( !await IGameInstanceDll.Current?.LoadNetworkTables( system ) )
		{
			Networking.Disconnect();
			return false;
		}

		return true;
	}

	public async Task InitializeGameSystemAsync( NetworkSystem system )
	{
		if ( IGameInstanceDll.Current is null || Application.IsUnitTest )
			return;

		system.GameSystem = await IGameInstanceDll.Current.CreateGameNetworkingAsync( system );
		system.GameSystem?.OnInitialize();

		if ( system.GameSystem is null )
			system.Disconnect();
	}

	public void SetLoadingTitle( string title )
	{
		LoadingScreen.Title = title;
	}

	public void OnActivated( NetworkSystem system )
	{
		if ( Application.IsEditor )
		{
			IToolsDll.Current?.SetPlaying();
		}

		LoadingScreen.IsVisible = false;
		Api.Activity.LoadFinished();
	}

	public void Disconnect( NetworkSystem system, string reason )
	{
		IGameInstanceDll.Current.Disconnect( reason );
	}
}
