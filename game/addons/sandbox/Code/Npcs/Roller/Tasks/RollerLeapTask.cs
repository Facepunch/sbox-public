using Sandbox.Npcs.Roller;
using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.Roller.Tasks;

/// <summary>
/// Leaps at the current target with a clean impulse (velocity zeroed first),
/// then waits for cooldown before handing back to the roll task.
/// </summary>
public sealed class RollerLeapTask : BaseTask
{
	private const float LeapCooldown = 1.2f;

	private TimeUntil _cooldown;

	protected override void OnStart()
	{
		var rollermine = Npc as RollerNpc;
		if ( rollermine is null ) return;

		var rb = rollermine.Rigidbody;
		if ( !rb.IsValid() ) return;

		var target = Npc.Senses.FindBestTarget();
		if ( !target.IsValid() ) return;

		var dir = (target.WorldPosition - Npc.WorldPosition).Normal;
		var leapDir = (dir + Vector3.Up * rollermine.LeapUpwardBias).Normal;

		// Zero existing velocity so the leap feels snappy rather than additive
		rb.Velocity = Vector3.Zero;
		rb.AngularVelocity = Vector3.Zero;

		rb.ApplyImpulse( leapDir * rollermine.LeapForce );

		rollermine.BroadcastLeapEffect();

		_cooldown = LeapCooldown;
	}

	protected override TaskResult OnUpdate()
	{
		return _cooldown ? TaskResult.Success : TaskResult.Running;
	}
}
