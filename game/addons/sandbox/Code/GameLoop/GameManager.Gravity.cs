/// <summary>
/// Drives the scene's gravity from server settings
/// </summary>
public sealed partial class GameManager
{
	public const float DefaultGravity = 850f;

	[Title( "Gravity" ), Group( "Gameplay" ), ServerSettings.Section( "World" ), Order( 10 ), Range( 0f, 2000f ), Step( 10f )]
	[ConVar( "sandbox.gravity", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "World gravity in inches per second squared. 850 is the default." )]
	public static float Gravity { get; set; } = DefaultGravity;

	void IScenePhysicsEvents.PrePhysicsStep() => ApplyGravity();

	private void ApplyGravity()
	{
		if ( Scene.IsEditor || Scene.PhysicsWorld is not { } world ) return;

		var gravity = Vector3.Down * Gravity;
		if ( world.Gravity != gravity )
			world.Gravity = gravity;
	}
}
