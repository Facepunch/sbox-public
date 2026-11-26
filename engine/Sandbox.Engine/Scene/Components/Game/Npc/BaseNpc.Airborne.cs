using Sandbox.Engine.Resources;
using Sandbox.Npcs.Layers;

namespace Sandbox.Npcs;

public partial class BaseNpc : Component.ICollisionListener
{
	/// <summary>
	/// Hitting something at or below this speed while airborne doesn't hurt - landing from a throw or
	/// a fall, or being slammed into a wall.
	/// </summary>
	[Property, Group( "Impact Damage" )]
	public float MaxSafeImpactSpeed { get; set; } = 350f;

	/// <summary>
	/// Hitting something at or above this speed is fatal.
	/// </summary>
	[Property, Group( "Impact Damage" )]
	public float FatalImpactSpeed { get; set; } = 1100f;

	/// <summary>
	/// Off our feet - thrown, dropped, or carried. Only NPCs whose mover keeps them on the
	/// ground (<see cref="MovementLayer.UsesGround"/>); ones that fly or roll around on physics are never "airborne".
	/// </summary>
	public bool IsAirborne => UsesGround && !IsOnGround;

	private TimeSince _timeSinceImpact;
	private bool _wasAirborne;
	private bool _launched;
	private TimeSince _sinceLaunch;

	// Our velocity last frame in the air - by the time we land or hit something, it's already slowed us
	private Vector3 _airVelocity;

	/// <summary>
	/// While airborne the animation falls with the body - the animgraph's in-air pose, driven by the
	/// rigidbody rather than the mover.
	/// </summary>
	private void UpdateAirborne()
	{
		var airborne = IsAirborne;

		// The ground check catches us a few units up, a frame before the body touches down - so a
		// landing often never reports a collision. Take the hit here, from how fast we were falling.
		if ( PhysicsCarrier.IsValid() )
			_launched = false;

		if ( _wasAirborne && !airborne )
			TakeImpact( MathF.Max( -_airVelocity.z, 0f ), WorldPosition, Vector3.Up, "fall" );

		if ( !airborne )
			_launched = false;

		_wasAirborne = airborne;
		// A mover that doesn't use the ground says for itself whether the NPC is grounded
		if ( UsesGround )
			Animation.IsGrounded = !airborne && !(Animation.Traversal is { } crossing && (crossing == NavMeshLinkType.Jump || crossing == NavMeshLinkType.Drop));

		if ( !airborne || !_rigidbody.IsValid() )
			return;

		_airVelocity = _rigidbody.Velocity;
		Animation.SetMove( _airVelocity );
	}

	/// <summary>
	/// Throw the NPC into the air with this velocity, such as a leap or a shove: the mover lets go and
	/// physics carries the body until it lands. Nothing it hits before landing, nor the landing, does impact damage. Host only.
	/// </summary>
	public void Launch( Vector3 velocity )
	{
		if ( !Networking.IsHost )
			return;

		// A freshly cloned NPC can be thrown before OnStart caches its body.
		if ( !_rigidbody.IsValid() )
			_rigidbody = GetComponent<Rigidbody>();
		if ( !_rigidbody.IsValid() )
			return;

		_launched = true;
		_sinceLaunch = 0;

		UpdateGroundFromTraceResult( default );
		_rigidbody.MotionEnabled = true;
		if ( UsesGround ) Movement.OnPhysicsControlStarted();
		_rigidbody.Velocity = velocity;
	}

	/// <summary>
	/// Hitting something hard while airborne - a wall we've been thrown into, or the floor if the
	/// collision beats the ground check to it. Only the speed straight into the surface counts, so
	/// skidding along it doesn't.
	/// </summary>
	void ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( !Networking.IsHost || !IsAirborne )
			return;

		var other = collision.Other.GameObject;
		_airContact = other.IsValid() && !other.Tags.Has( "npc" ) ? other : null;

		var point = collision.Contact.Point;
		var normal = collision.Contact.Normal;
		var speed = MathF.Max( MathF.Abs( collision.Contact.NormalSpeed ), MathF.Abs( Vector3.Dot( _airVelocity, normal ) ) );

		// Face the normal out of the surface, toward us
		if ( Vector3.Dot( EyePosition - point, normal ) < 0f )
			normal = -normal;

		// Hitting the floor is a fall, being slammed into a wall (or ceiling) is a crush
		TakeImpact( speed, point, normal, MathF.Abs( normal.z ) > 0.7f ? "fall" : "crush" );
	}

	/// <summary>
	/// A hard hit hurts - harsher than a player's fall damage, being flung around
	/// should hurt. Nothing at or below <see cref="MaxSafeImpactSpeed"/>, fatal at <see cref="FatalImpactSpeed"/>.
	/// </summary>
	private void TakeImpact( float speed, Vector3 position, Vector3 normal, string tag )
	{
		if ( _launched || Health < 1f || speed <= MaxSafeImpactSpeed || FatalImpactSpeed <= MaxSafeImpactSpeed )
			return;

		// One hit, however many ways it gets reported
		if ( _timeSinceImpact < 0.25f )
			return;

		var amount = MathX.Remap( speed, MaxSafeImpactSpeed, FatalImpactSpeed, 0f, 100f );
		if ( amount < 1f )
			return;

		_timeSinceImpact = 0;

		var damage = new DamageInfo( amount, null, null ) { Position = position, Normal = normal };
		damage.Tags.Add( tag );
		TakeDamage( damage );
	}
}
