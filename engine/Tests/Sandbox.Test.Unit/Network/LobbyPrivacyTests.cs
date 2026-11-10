using Sandbox.Network;

namespace NetworkTests;

// Shares LaunchArguments' static state, so the tests can't run alongside each other
[TestClass, DoNotParallelize]
public class LobbyPrivacyTests
{
	[TestCleanup]
	public void Cleanup() => LaunchArguments.Reset();

	[TestMethod]
	public void GameConfigIsKeptWhenTheMenuSaysNothing()
	{
		LaunchArguments.Reset();
		Assert.AreEqual( LobbyPrivacy.FriendsOnly, LaunchArguments.ResolvePrivacy( LobbyPrivacy.FriendsOnly ) );
		Assert.AreEqual( LobbyPrivacy.Public, LaunchArguments.ResolvePrivacy( LobbyPrivacy.Public ) );
	}

	[TestMethod]
	public void MenuPublicDoesNotOpenAFriendsOnlyGame()
	{
		LaunchArguments.Privacy = LobbyPrivacy.Public;
		Assert.AreEqual( LobbyPrivacy.FriendsOnly, LaunchArguments.ResolvePrivacy( LobbyPrivacy.FriendsOnly ) );
		Assert.AreEqual( LobbyPrivacy.Private, LaunchArguments.ResolvePrivacy( LobbyPrivacy.Private ) );
	}

	[TestMethod]
	public void MenuPrivacyClosesAPublicGame()
	{
		LaunchArguments.Privacy = LobbyPrivacy.FriendsOnly;
		Assert.AreEqual( LobbyPrivacy.FriendsOnly, LaunchArguments.ResolvePrivacy( LobbyPrivacy.Public ) );

		LaunchArguments.Privacy = LobbyPrivacy.Private;
		Assert.AreEqual( LobbyPrivacy.Private, LaunchArguments.ResolvePrivacy( LobbyPrivacy.Public ) );
	}

	[TestMethod]
	public void PrivateIsMorePrivateThanFriendsOnly()
	{
		LaunchArguments.Privacy = LobbyPrivacy.FriendsOnly;
		Assert.AreEqual( LobbyPrivacy.Private, LaunchArguments.ResolvePrivacy( LobbyPrivacy.Private ) );

		LaunchArguments.Privacy = LobbyPrivacy.Private;
		Assert.AreEqual( LobbyPrivacy.Private, LaunchArguments.ResolvePrivacy( LobbyPrivacy.FriendsOnly ) );
	}
}
