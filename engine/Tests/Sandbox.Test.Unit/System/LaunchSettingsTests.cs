namespace SystemTests;

[TestClass]
[DoNotParallelize]
public class LaunchSettingsTests
{
	[TestCleanup]
	public void Cleanup() => LaunchArguments.GameSettings = null;

	[TestMethod]
	public void OnlyTheEngineCanGrantProtectedLaunchSettings()
	{
		LaunchArguments.SetTrustedGameSettings( new() { ["menu_setting"] = "1" } );
		Assert.IsTrue( LaunchArguments.CanSetProtected( "menu_setting", "1" ) );

		LaunchArguments.GameSettings["menu_setting"] = "2";
		Assert.IsFalse( LaunchArguments.CanSetProtected( "menu_setting", "2" ) );

		LaunchArguments.GameSettings = new() { ["protected_probe"] = "1" };
		Assert.IsFalse( LaunchArguments.CanSetProtected( "protected_probe", "1" ) );
	}
}
