using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// Who may create building blocks. Checked on the host for the Builder tool, the spawn menu and dupes;
/// the Stacker checks <see cref="CanBuild"/> itself since its events don't say what it's copying.
/// </summary>
public sealed class BuilderPolicy : GameObjectSystem<BuilderPolicy>, Global.ISpawnEvents, IToolActionEvents
{
	[Title( "Building Blocks" ), Group( "Gameplay" ), ServerSettings.Section( "Props" ), Order( 24 )]
	[ConVar( "sandbox.allow.builder", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Players can create building blocks. Admins and players with the Building Blocks permission always can." )]
	public static bool AllowBuilder { get; set; } = true;

	[Permission, Title( "Building Blocks" ), Description( "Create building blocks when it's off for everyone." )]
	public const string Permission = "sandbox.builder";

	public BuilderPolicy( Scene scene ) : base( scene )
	{
	}

	public static bool CanBuild( Connection connection ) => Permissions.Allows( connection, AllowBuilder, Permission );

	/// <summary>
	/// True if <paramref name="connection"/> may build, otherwise tells them why not.
	/// </summary>
	public static bool CheckCanBuild( Connection connection )
	{
		if ( CanBuild( connection ) ) return true;

		Notices.SendLocalizedNotice( connection, "block", Color.Red, "#builder.denied", null, 3 );
		return false;
	}

	void Global.ISpawnEvents.OnSpawn( Global.ISpawnEvents.SpawnData e )
	{
		if ( e.Cancelled || e.Player is null ) return;

		if ( e.Spawner is DuplicatorSpawner { BuilderBlocks.TooDeep: true } )
		{
			Log.Warning( $"Refused a dupe from {e.Player.DisplayName} nested deeper than {BuilderDupe.MaxDepth} objects" );
			e.Cancelled = true;
			return;
		}

		var creates = e.Spawner switch
		{
			BuilderSpawner => true,
			DuplicatorSpawner dupe => dupe.BuilderBlocks.Count > 0,
			_ => false
		};

		if ( creates && !CheckCanBuild( e.Player ) )
			e.Cancelled = true;
	}

	void IToolActionEvents.OnToolAction( IToolActionEvents.ActionData e )
	{
		if ( e.Cancelled || e.Player is null || e.Tool is not BuilderTool || e.Input != ToolInput.Primary ) return;

		if ( !CheckCanBuild( e.Player ) )
			e.Cancelled = true;
	}
}
