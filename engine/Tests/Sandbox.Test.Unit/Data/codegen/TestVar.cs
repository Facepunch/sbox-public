using Sandbox;

public partial class TestVar
{
	[ConVar.Replicated( "debug_playercontroller" )]
	public static bool Debug { get; set; } = false;

	[ConVar.ClientData( "debug_playercontroller" )]
	public static bool Debug { get; set; } = false;
}
