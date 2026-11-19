using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.Schedules;

/// <summary>
/// Wander to a random reachable point nearby, then pause. A simple ambient behaviour any
/// NPC can use as its default.
/// </summary>
public sealed class WanderSchedule : ScheduleBase
{
	/// <summary>How far to roam from the current position.</summary>
	public float Radius { get; set; } = 300f;

	protected override void OnStart()
	{
		var direction = Vector3.Random.WithZ( 0 ).Normal;
		var target = Npc.WorldPosition + direction * Game.Random.Float( Radius * 0.3f, Radius );

		// Snap to the navmesh so we pick somewhere actually reachable.
		if ( Npc.Scene.NavMesh.GetClosestPoint( target ) is { } navPoint )
			target = navPoint;

		AddTask( new MoveTo( target, 20f ) );
		AddTask( new Wait( Game.Random.Float( 1f, 3f ) ) );
	}
}
