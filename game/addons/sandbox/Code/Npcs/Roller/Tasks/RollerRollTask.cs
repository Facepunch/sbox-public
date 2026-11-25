using Sandbox.Npcs.Roller;
using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.Roller.Tasks;

/// <summary>
/// Rolls toward the current target by applying force and torque.
/// Succeeds when within LeapRange.  Fails if target is lost.
/// Force/torque values are configured on RollerNpc.
/// </summary>
public sealed class RollerRollTask : BaseTask
{
	/// <summary>If lateral speed stays below this for StuckTime, jump to un-stick.</summary>
	private const float StuckSpeedThreshold = 40f;
	private const float StuckTime = 1.2f;

	private TimeSince _stuckTimer;
	private bool _stuckTimerRunning;

	protected override TaskResult OnUpdate()
	{
		var rollermine = Npc as RollerNpc;
		if ( rollermine is null ) return TaskResult.Failed;

		var rb = rollermine.Rigidbody;
		if ( !rb.IsValid() ) return TaskResult.Failed;

		var target = Npc.Senses.FindBestTarget();
		if ( !target.IsValid() ) return TaskResult.Failed;

		var toTarget = target.WorldPosition - Npc.WorldPosition;
		var flatToTarget = toTarget.WithZ( 0 );
		var dist = flatToTarget.Length;

		if ( dist <= rollermine.LeapRange )
			return TaskResult.Success;

		var dir = flatToTarget.Normal;

		rb.ApplyForce( dir * rollermine.RollForce );

		// Spin in the direction of travel (torque axis is perpendicular to direction in XY plane)
		var torqueAxis = new Vector3( -dir.y, dir.x, 0f );
		rb.ApplyTorque( torqueAxis * rollermine.RollTorque );

		var lateralSpeed = rb.Velocity.WithZ( 0 ).Length;
		if ( lateralSpeed < StuckSpeedThreshold )
		{
			if ( !_stuckTimerRunning )
			{
				_stuckTimer = 0f;
				_stuckTimerRunning = true;
			}
			else if ( _stuckTimer > StuckTime )
			{
				rb.ApplyImpulse( Vector3.Up * rollermine.StuckJumpForce );
				_stuckTimerRunning = false;
			}
		}
		else
		{
			_stuckTimerRunning = false;
		}

		return TaskResult.Running;
	}

	protected override void Reset()
	{
		_stuckTimerRunning = false;
	}
}
