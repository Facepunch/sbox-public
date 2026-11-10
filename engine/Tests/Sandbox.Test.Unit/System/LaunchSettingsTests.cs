using System.Reflection;

namespace SystemTests;

[TestClass]
[DoNotParallelize]
public class LaunchSettingsTests
{
	internal static int GameSettingProbe { get; set; }
	internal static int EngineProbe { get; set; }

	[TestInitialize]
	public void Setup()
	{
		Register( nameof( GameSettingProbe ), "launch_game_setting", ConVarFlags.GameSetting | ConVarFlags.Protected );
		Register( nameof( EngineProbe ), "launch_engine_setting", ConVarFlags.Protected );
	}

	[TestCleanup]
	public void Cleanup()
	{
		LaunchArguments.GameSettings = null;
		ConVarSystem.Members.Remove( "launch_game_setting" );
		ConVarSystem.Members.Remove( "launch_engine_setting" );
	}

	static void Register( string property, string name, ConVarFlags flags )
	{
		var member = typeof( LaunchSettingsTests ).GetProperty( property, BindingFlags.Static | BindingFlags.NonPublic );
		ConVarSystem.Members[name] = new ManagedCommand( typeof( LaunchSettingsTests ).Assembly, member, new ConVarAttribute( name, flags ) );
	}

	[TestMethod]
	public void OnlyTheEngineCanGrantProtectedLaunchSettings()
	{
		LaunchArguments.SetTrustedGameSettings( new() { ["launch_game_setting"] = "1" } );
		Assert.IsTrue( LaunchArguments.CanSetProtected( "launch_game_setting", "1" ) );

		LaunchArguments.GameSettings["launch_game_setting"] = "2";
		Assert.IsFalse( LaunchArguments.CanSetProtected( "launch_game_setting", "2" ) );

		LaunchArguments.GameSettings = new() { ["launch_game_setting"] = "1" };
		Assert.IsFalse( LaunchArguments.CanSetProtected( "launch_game_setting", "1" ) );
	}

	[TestMethod]
	public void TrustedLaunchSettingsOnlyReachGameSettings()
	{
		LaunchArguments.SetTrustedGameSettings( new() { ["launch_engine_setting"] = "1", ["voip_mode"] = "OpenMicrophone" } );

		Assert.IsFalse( LaunchArguments.CanSetProtected( "launch_engine_setting", "1" ) );
		Assert.IsFalse( LaunchArguments.CanSetProtected( "voip_mode", "OpenMicrophone" ) );
	}
}
