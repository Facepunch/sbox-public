using Sandbox.Npcs.Layers;

namespace Sandbox.Npcs.Tasks;

/// <summary>
/// Fires a burst at a target. The burst length comes from the weapon's NPC usage - a random shot
/// count between its burst min and max. Shots only fire while the target is inside the weapon's
/// engagement band.
/// </summary>
public class FireWeapon : TaskBase
{
	/// <summary>The weapon component to fire.</summary>
	public BaseSandboxWeapon Weapon { get; }

	/// <summary>The GameObject to aim at.</summary>
	public GameObject Target { get; }

	/// <summary>Body rotation speed (degrees/s scale) used while actively aiming. Higher than the default look speed.</summary>
	public float AimTurnSpeed { get; set; } = 8f;

	private int _shotsLeft;
	private TimeUntil _timeout;

	public FireWeapon( BaseSandboxWeapon weapon, GameObject target )
	{
		Weapon = weapon;
		Target = target;
	}

	protected override void OnStart()
	{
		if ( !Weapon.IsValid() )
			return;

		_shotsLeft = Game.Random.Int( Weapon.Npc.BurstMin, Weapon.Npc.BurstMax );

		// Shoot at the target, not wherever the animated barrel happens to point.
		Weapon.NpcAimTarget = Target;

		// A burst that can't land its shots (target keeps out of range) gives up rather than stall.
		_timeout = 4f;

		// Let nearby NPCs hear the gunfire and come investigate.
		Npc.EmitStimulus( StimulusKind.Gunshot, radius: 2048f, lifetime: 1.5f );
	}

	protected override TaskStatus OnUpdate()
	{
		if ( !Weapon.IsValid() )
			return TaskStatus.Failed;

		if ( !Target.IsValid() )
			return TaskStatus.Failed;

		if ( _timeout )
			return TaskStatus.Failed;

		RotateBodyTowardTarget();

		// Only fire once we're facing the target, can see it, and it's inside the weapon's engagement
		// band. FirePrimary respects the weapon's fire rate - the burst paces itself.
		if ( Npc.Animation.IsFacingTarget() && CanSee() && InRange() && Weapon.FirePrimary() )
		{
			Npc.Animation.TriggerAttack();
			_shotsLeft--;
		}

		return _shotsLeft <= 0 ? TaskStatus.Success : TaskStatus.Running;
	}

	protected override void OnEnd()
	{
		if ( Weapon.IsValid() )
			Weapon.NpcAimTarget = null;
	}

	private bool CanSee() => Npc.Senses.VisibleTargets.Contains( Target );

	private bool InRange()
	{
		var distance = Npc.WorldPosition.Distance( Target.WorldPosition );
		return distance >= Weapon.Npc.MinRange && distance <= Weapon.Npc.MaxRange;
	}

	private void RotateBodyTowardTarget()
	{
		var toTarget = (Target.WorldPosition - Npc.WorldPosition).WithZ( 0 );
		if ( toTarget.LengthSquared < 1f ) return;

		var targetRot = Rotation.LookAt( toTarget.Normal, Vector3.Up );
		Npc.WorldRotation = Rotation.Lerp( Npc.WorldRotation, targetRot, AimTurnSpeed * Time.Delta );
	}
}
