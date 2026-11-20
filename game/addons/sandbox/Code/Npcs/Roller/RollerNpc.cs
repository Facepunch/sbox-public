using Sandbox.Npcs.Layers;
using Sandbox.Npcs.Roller.Schedules;

namespace Sandbox.Npcs.Roller;

/// <summary>
/// A physics-driven NPC that chases players, leaps at them, and bounces off dealing damage on contact.
/// </summary>
public sealed class RollerNpc : Npc, Component.ICollisionListener
{
	/// <summary>
	/// Continuous force applied per-frame while rolling toward a target.
	/// </summary>
	[Property, Group( "Balance" )]
	public float RollForce { get; set; } = 80000f;

	/// <summary>
	/// Torque applied per-frame to spin the sphere visually.
	/// </summary>
	[Property, Group( "Balance" )]
	public float RollTorque { get; set; } = 40000f;

	/// <summary>
	/// Upward impulse applied when the rollermine gets stuck.
	/// </summary>
	[Property, Group( "Balance" )]
	public float StuckJumpForce { get; set; } = 500f;

	/// <summary>
	/// Impulse applied when leaping at the target.
	/// </summary>
	[Property, Group( "Balance" )]
	public float LeapForce { get; set; } = 60000f;

	/// <summary>
	/// Upward component added to the leap direction (0 = flat, 1 = 45°).
	/// </summary>
	[Property, Group( "Balance" )]
	public float LeapUpwardBias { get; set; } = 0.2f;

	/// <summary>
	/// Impulse magnitude applied to self when bouncing off a surface/player.
	/// </summary>
	[Property, Group( "Balance" )]
	public float BounceForce { get; set; } = 450f;

	/// <summary>
	/// Damage applied to anything we crash into.
	/// </summary>
	[Property, Group( "Balance" )]
	public float ContactDamage { get; set; } = 20f;

	/// <summary>
	/// Distance at which we switch from rolling to leaping.
	/// </summary>
	[Property, Group( "Balance" )]
	public float LeapRange { get; set; } = 160f;

	/// <summary>
	/// Eye child GameObject — assign in editor.
	/// Rotated to face the current target each frame.
	/// </summary>
	[Property]
	public GameObject Eye { get; set; }

	/// <summary>
	/// GameObject whose child ParticleEffects are enabled while actively hunting a target.
	/// </summary>
	[Property, Group( "Effects" )]
	public GameObject HuntingEffects { get; set; }

	/// <summary>
	/// Prefab cloned at world position when leaping at the target.
	/// </summary>
	[Property, Group( "Effects" )]
	public GameObject LeapEffect { get; set; }

	/// <summary>
	/// Prefab cloned at the contact point when hitting a player.
	/// </summary>
	[Property, Group( "Effects" )]
	public GameObject ContactEffect { get; set; }

	/// <summary>
	/// Looping roll sound — pitch is driven by speed.
	/// </summary>
	[Property, Group( "Effects" )]
	public SoundEvent RollSound { get; set; }

	/// <summary>Speed (units/s) at which pitch reaches PitchMax.</summary>
	[Property, Group( "Effects" )]
	public float PitchSpeedMax { get; set; } = 600f;

	/// <summary>Pitch at rest.</summary>
	[Property, Group( "Effects" )]
	public float PitchMin { get; set; } = 0.6f;

	/// <summary>Pitch at full speed.</summary>
	[Property, Group( "Effects" )]
	public float PitchMax { get; set; } = 1.6f;

	private SoundHandle _rollSound;

	public Rigidbody Rigidbody { get; private set; }

	[Sync] public bool IsHunting { get; private set; }
	[Sync( SyncFlags.FromHost )]
	private TimeSince _lastBounce { get; set; }
	private const float BounceCooldown = 0.25f;

	private SphereCollider _collider;
	private float _baseRadius;

	/// <summary>
	/// Called by chase/idle schedules to toggle the hunting particle children.
	/// </summary>
	public void SetHunting( bool hunting )
	{
		if ( IsHunting == hunting ) return;
		IsHunting = hunting;

		if ( HuntingEffects.IsValid() )
			HuntingEffects.Enabled = hunting;

		if ( _collider.IsValid() )
		{
			_collider.Radius = hunting ? _baseRadius * 1.4f : _baseRadius;

			if ( hunting && Rigidbody.IsValid() )
				Rigidbody.ApplyImpulse( Vector3.Up * 50000f );
		}
	}

	/// <summary>
	/// Clones the leap effect prefab at our current position (broadcast so all clients see it).
	/// </summary>
	[Rpc.Broadcast]
	public void BroadcastLeapEffect()
	{
		if ( LeapEffect is null ) return;
		LeapEffect.Clone( WorldPosition );
	}

	/// <summary>
	/// Clones the contact effect prefab at the hit position (broadcast so all clients see it).
	/// </summary>
	[Rpc.Broadcast]
	public void BroadcastContactEffect( Vector3 position )
	{
		if ( ContactEffect is null ) return;
		ContactEffect.Clone( position );
	}

	protected override void OnStart()
	{
		base.OnStart();

		// A spinning sphere has no stable "forward", so give it all-round sight
		// (range + line-of-sight still filter) instead of a tumbling view cone.
		Senses.FieldOfView = 360f;

		Rigidbody = GetComponent<Rigidbody>();
		_collider = GetComponent<SphereCollider>();

		if ( _collider.IsValid() )
			_baseRadius = _collider.Radius;

		if ( Rigidbody.IsValid() )
			Rigidbody.MotionEnabled = true;

		if ( HuntingEffects.IsValid() )
			HuntingEffects.Enabled = false;

		StartRollSound();
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();
		StopRollSound();
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		TrackEye();
		UpdateRollSound();
	}

	// Hunts players, ignores everything else.
	public override string Faction => Factions.Monster;

	protected override void SetupRelationships() => Hates( Factions.Player );

	public override ScheduleBase GetSchedule()
	{
		var target = Senses.GetNearestVisible();
		if ( target.IsValid() )
			return GetSchedule<RollerChaseSchedule>();

		return GetSchedule<RollerIdleSchedule>();
	}

	protected override void Die( in DamageInfo damage )
	{
		// Broadcast the death so nearby NPCs notice (base Die does this; we override it).
		EmitStimulus( StimulusKind.Death, radius: 1024f, lifetime: 2f );

		GameManager.Current?.OnNpcDeath( this, damage );

		// TODO: explosion effect / sound

		GameObject.Destroy();
	}

	void ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( IsProxy ) return;
		if ( !Rigidbody.IsValid() ) return;
		if ( _lastBounce < BounceCooldown ) return;

		var root = collision.Other.GameObject?.Root;
		if ( !root.IsValid() ) return;

		// Only react to damageable targets (players, NPCs) — not terrain/props
		if ( !root.Components.TryGet( out IDamageable damageable ) )
			return;

		_lastBounce = 0f;

		damageable.OnDamage( new DamageInfo
		{
			Damage = ContactDamage,
			Attacker = GameObject,
			Position = collision.Contact.Point,
		} );

		BroadcastContactEffect( collision.Contact.Point );

		// Bounce up and awayfrom the player rather than a pure reflection
		var away = (WorldPosition - root.WorldPosition).WithZ( 0 );
		if ( away.LengthSquared < 0.01f )
			away = WorldRotation.Backward.WithZ( 0 );

		var bounceDir = (away.Normal + Vector3.Up * 2f).Normal;
		Rigidbody.Velocity = Vector3.Zero;
		Rigidbody.ApplyImpulse( bounceDir * BounceForce );
	}

	private void StartRollSound()
	{
		if ( RollSound is null ) return;
		if ( _rollSound.IsValid() && !_rollSound.IsStopped ) return;

		_rollSound = Sound.Play( RollSound, WorldPosition );
		_rollSound.Parent = GameObject;
		_rollSound.FollowParent = true;
		_rollSound.Pitch = PitchMin;
	}

	private void StopRollSound()
	{
		if ( _rollSound.IsValid() )
		{
			_rollSound.Stop();
			_rollSound = default;
		}
	}

	private void UpdateRollSound()
	{
		if ( !_rollSound.IsValid() || _rollSound.IsStopped ) return;
		if ( !Rigidbody.IsValid() ) return;

		var speed = Rigidbody.Velocity.Length;
		var t = MathX.Clamp( speed / PitchSpeedMax, 0f, 1f );
		_rollSound.Pitch = MathX.Lerp( PitchMin, PitchMax, t );
	}

	private void TrackEye()
	{
		if ( !Eye.IsValid() ) return;

		var target = Senses.GetNearestVisible();
		if ( !target.IsValid() ) return;

		var dir = (target.WorldPosition - Eye.WorldPosition).Normal;
		if ( dir.LengthSquared < 0.01f ) return;

		Eye.WorldRotation = Rotation.LookAt( dir, Vector3.Up );
	}
}
