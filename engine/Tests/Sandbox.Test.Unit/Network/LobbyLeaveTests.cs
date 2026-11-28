using Sandbox.SteamApi;

namespace NetworkTests;

/// <summary>
/// Exercises local lobby membership when Steam matchmaking is unavailable.
/// </summary>
[TestClass]
[DoNotParallelize]
public class LobbyLeaveTests
{
	/// <summary>
	/// A lobby socket can be disposed after the menu has released Steam.
	/// </summary>
	[TestMethod]
	public void LeaveWithoutMatchmakingRemovesLocalMembership()
	{
		const ulong lobbyId = 27;
		var lobby = new Lobby( lobbyId );
		LobbyManager.OnEntered( lobbyId );

		try
		{
			lobby.Leave();
			Assert.IsFalse( LobbyManager.ActiveLobbies.Contains( lobbyId ) );

			lobby.Leave();
		}
		finally
		{
			LobbyManager.ActiveLobbies.Remove( lobbyId );
		}
	}
}
