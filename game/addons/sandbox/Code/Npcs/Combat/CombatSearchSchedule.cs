using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.CombatNpc;

/// <summary>
/// Hunt for a hostile we can't see: run to where it was heading, look around, then sweep a couple of
/// nearby spots at a cautious pace. Re-plans if fresh intel moves the last known position, and
/// spotting the hostile preempts it with <see cref="CombatEngageSchedule"/>.
/// </summary>
public class CombatSearchSchedule : ScheduleBase
{
	/// <summary>
	/// Speed to run to the last known position at.
	/// </summary>
	public float Speed { get; set; } = 180f;

	/// <summary>
	/// Cautious speed to sweep the area at once we get there.
	/// </summary>
	public float SweepSpeed { get; set; } = 85f;

	/// <summary>
	/// Chance to sweep a nearby spot after looking around, rather than holding where we lost them.
	/// </summary>
	public float SweepChance { get; set; } = 0.5f;

	/// <summary>
	/// How far ahead to lead a moving hostile, in seconds of its last known velocity.
	/// </summary>
	public float LeadTime { get; set; } = 0.75f;

	/// <summary>
	/// How far around the last known position to sweep.
	/// </summary>
	public float SearchRadius { get; set; } = 400f;

	public override int Priority => SchedulePriority.Alert;

	// Positions are on the ground - look at them at head height, not at the floor
	private static readonly Vector3 EyeHeight = Vector3.Up * 64f;

	// The intel this search was planned from
	private Vector3 _plannedFrom;

	// Intel we've already run down. This schedule instance is reused, so it carries over to the
	// next round - which then holds and watches instead of rushing back to the same spot.
	private Vector3? _alreadySearched;

	private CombatNpc Combat => Npc as CombatNpc;

	protected override void OnStart()
	{
		if ( Combat?.LastKnownPosition is not { } lastKnown )
		{
			AddTask( new Wait( 0.5f ) );
			return;
		}

		_plannedFrom = lastKnown;

		if ( _alreadySearched is { } searched && searched.Distance( lastKnown ) < 150f )
		{
			AddWatch( lastKnown );
			return;
		}

		_alreadySearched = lastKnown;

		// Head where they were going, not just where they were
		var lead = Combat.LastKnownVelocity.WithZ( 0 ) * LeadTime;
		if ( lead.Length > 300f ) lead = lead.Normal * 300f;
		var destination = SnapToNavMesh( lastKnown + lead );

		// The way they were heading, or failing that, the way we were going to get here
		var heading = lead.Length > 1f ? lead.Normal : (destination - Npc.WorldPosition).WithZ( 0 ).Normal;

		// A beat to react before setting off, then eyes on where we're heading
		Npc.Animation.SetLookTarget( destination + EyeHeight );
		AddTask( new Wait( Game.Random.Float( 0.3f, 0.8f ) ) );
		AddTask( new MoveTo( destination, 48f ) { Speed = Speed } );

		// Take a good look the way they went
		AddLookAround( destination, heading );

		// Sometimes push on a little further, weapon up, and look again
		if ( Game.Random.Float() < SweepChance )
		{
			var spot = SnapToNavMesh( destination + Rotate( heading, 60f ) * Game.Random.Float( SearchRadius * 0.4f, SearchRadius ) );
			AddTask( new MoveTo( spot, 32f ) { Speed = SweepSpeed } );
			AddLookAround( spot, heading );
		}
	}

	// Been there already: mostly stand and watch the area, now and then edge somewhere nearby
	private void AddWatch( Vector3 lastKnown )
	{
		var toward = (lastKnown - Npc.WorldPosition).WithZ( 0 );
		var heading = toward.Length > 32f ? toward.Normal : Npc.WorldRotation.Forward.WithZ( 0 ).Normal;

		if ( Game.Random.Float() < SweepChance * 0.5f )
		{
			var spot = SnapToNavMesh( lastKnown + Rotate( heading, 180f ) * Game.Random.Float( SearchRadius * 0.3f, SearchRadius * 0.7f ) );
			AddTask( new MoveTo( spot, 32f ) { Speed = SweepSpeed } );
		}

		AddLookAround( Npc.WorldPosition, heading );
	}

	// One slow look, within a cone ahead - not snapping all the way round
	private void AddLookAround( Vector3 from, Vector3 heading )
	{
		AddTask( new LookAt( from + Rotate( heading, 70f ) * 400f + EyeHeight ) );
		AddTask( new Wait( Game.Random.Float( 1.5f, 3f ) ) );
	}

	private static Vector3 Rotate( Vector3 heading, float maxYaw )
	{
		return Rotation.FromYaw( Game.Random.Float( -maxYaw, maxYaw ) ) * heading;
	}

	protected override bool ShouldCancel()
	{
		// Fresh intel moved them somewhere else -- re-plan
		return Combat?.LastKnownPosition is { } lastKnown && lastKnown.Distance( _plannedFrom ) > 150f;
	}

	protected override void OnEnd()
	{
		Npc.Animation.ClearLookTarget();
	}

	private Vector3 SnapToNavMesh( Vector3 position )
	{
		return Npc.Scene.NavMesh.GetClosestPoint( position ) ?? position;
	}
}
