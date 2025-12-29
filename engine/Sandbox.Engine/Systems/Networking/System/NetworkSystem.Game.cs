using Sandbox.Engine;

namespace Sandbox.Network;


internal partial class NetworkSystem
{
	internal GameNetworkSystem GameSystem { get; set; }

	public void InitializeGameSystem()
	{
		// If we are unit testing we dont want to do any of this for now, this only works with a gamepackage loaded
		if ( IGameInstanceDll.Current is null || Application.IsUnitTest )
			return;

		GameSystem = IGameInstanceDll.Current.CreateGameNetworking( this );
		GameSystem?.OnInitialize();

		if ( GameSystem is null )
			Disconnect();
	}
}
