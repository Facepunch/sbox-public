namespace Sandbox.Npcs;

public partial class Npc : Component.ICollisionListener
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
	/// Off our feet - thrown, dropped, or dangling from a physgun. Only NPCs that walk on the navmesh;
	/// ones that roll around on physics (rollermines) are never "airborne".
	/// </summary>
	public bool IsAirborne => _navAgent.IsValid() && !IsOnGround;

	private TimeSince _timeSinceImpact;
	private bool _wasAirborne;
	private bool _wasCarried;

	// Our velocity last frame in the air - by the time we land or hit something, it's already slowed us
	private Vector3 _airVelocity;

	/// <summary>
	/// While airborne the animation falls with the body - the animgraph's in-air pose, driven by the
	/// rigidbody rather than the (disabled) nav agent.
	/// </summary>
	private void UpdateAirborne()
	{
		var airborne = IsAirborne;

		// The ground check catches us a few units up, a frame before the body touches down - so a
		// landing often never reports a collision. Take the hit here, from how fast we were falling.
		if ( isPickedUp )
			_wasCarried = true;

		if ( _wasAirborne && !airborne )
		{
			TakeImpact( MathF.Max( -_airVelocity.z, 0f ), WorldPosition, Vector3.Up, GroundObject, DamageTags.Fall );

			if ( _wasCarried && Health >= 1f )
				OnPutDown();

			_wasCarried = false;
		}

		_wasAirborne = airborne;
		Animation.Grounded = !airborne;

		if ( !airborne || !_rigidbody.IsValid() )
			return;

		_airVelocity = _rigidbody.Velocity;
		Animation.SetMove( _airVelocity, WorldRotation );
	}

	/// <summary>
	/// Back on our feet after a physgun carried us - keep whichever way it turned us, rather than
	/// snapping back to whatever we were looking at or doing before.
	/// </summary>
	protected virtual void OnPutDown()
	{
		Animation.ClearLookTarget();
		EndCurrentSchedule();
	}

	/// <summary>
	/// Hitting something hard while airborne - a wall we've been thrown into, or the floor if the
	/// collision beats the ground check to it. Only the speed straight into the surface counts, so
	/// skidding along it doesn't.
	/// </summary>
	void ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( IsProxy || !IsAirborne )
			return;

		var point = collision.Contact.Point;
		var normal = collision.Contact.Normal;
		var speed = MathF.Max( MathF.Abs( collision.Contact.NormalSpeed ), MathF.Abs( Vector3.Dot( _airVelocity, normal ) ) );

		// Face the normal out of the surface, toward us
		if ( Vector3.Dot( WorldPosition + Vector3.Up * 40f - point, normal ) < 0f )
			normal = -normal;

		// Hitting the floor is a fall, being slammed into a wall (or ceiling) is a crush
		TakeImpact( speed, point, normal, collision.Other.GameObject, MathF.Abs( normal.z ) > 0.7f ? DamageTags.Fall : DamageTags.Crush );
	}

	/// <summary>
	/// A hard hit hurts - harsher than a player's fall damage, being flung around by a physgun
	/// should hurt. Nothing at or below <see cref="MaxSafeImpactSpeed"/>, fatal at <see cref="FatalImpactSpeed"/>.
	/// </summary>
	private void TakeImpact( float speed, Vector3 position, Vector3 normal, GameObject surface, string tag )
	{
		if ( Health < 1f || speed <= MaxSafeImpactSpeed || FatalImpactSpeed <= MaxSafeImpactSpeed )
			return;

		// One hit, however many ways it gets reported
		if ( _timeSinceImpact < 0.25f )
			return;

		var amount = MathX.Remap( speed, MaxSafeImpactSpeed, FatalImpactSpeed, 0f, 100f );
		if ( amount < 1f )
			return;

		_timeSinceImpact = 0;

		// Before the damage - it might kill us, and then there's nobody to send it from
		SplatterBlood( position, normal, surface, amount / 100f );

		var damage = new DamageInfo( amount, null, null ) { Position = position };
		damage.Tags.Add( tag );
		(this as IDamageable).OnDamage( damage );
	}

	private static readonly string[] BloodDecals =
	[
		"decals/blood/blood_splatter_01.decal",
		"decals/blood/blood_splatter_02.decal",
		"decals/blood/blood_splatter_03.decal",
		"decals/blood/blood_splatter_04.decal",
	];

	/// <summary>
	/// Paint the surface we smacked into - a big splat where we hit, with droplets around it. Harder
	/// hits make more mess. Purely cosmetic, so every client makes its own.
	/// </summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void SplatterBlood( Vector3 point, Vector3 normal, GameObject surface, float severity )
	{
		if ( Application.IsDedicatedServer )
			return;

		// A knock that only bruises doesn't bleed
		severity = severity.Clamp( 0f, 1f );
		if ( severity < 0.15f )
			return;

		var facing = Rotation.LookAt( normal );

		// Splats on walls are half the size of the ones on the floor - a body lands flat on the floor,
		// but only part of it hits a wall. On the floor the spatter stays close round the body too.
		var floor = MathF.Abs( normal.z ) > 0.7f;
		var size = floor ? 1f : 0.5f;

		// The mark where we hit grows with the hit - a smear for a solid knock, body-sized for a brutal one
		var main = PickBloodDecal();
		if ( main is null )
			return;

		var mainScale = MathX.Lerp( 1f, 6f, severity ) * size;
		SpawnBloodDecal( main, point, normal, surface, mainScale );

		// Spatter only really flies on the violent ones - none on a moderate hit, a handful on a hard
		// slam, the lot when it's near fatal - scattered anywhere out to a reach that grows with the
		// hit. On the floor it stays close round the body; on a wall it's thrown further. Droplets can
		// land over the main splat, but not on top of each other.
		var mainRadius = BloodRadius( main, mainScale );
		var reach = floor ? mainRadius * 0.8f + 16f * severity : mainRadius + 48f * severity;
		var placed = new List<(Vector3 Position, float Radius)>();

		var droplets = (int)(10f * severity * severity);
		for ( var i = 0; i < droplets; i++ )
		{
			var definition = PickBloodDecal();
			var scale = Game.Random.Float( 0.6f, 1.6f ) * MathX.Lerp( 0.6f, 1.2f, severity ) * size;
			var radius = BloodRadius( definition, scale );

			for ( var attempt = 0; attempt < 6; attempt++ )
			{
				var direction = facing * new Vector3( 0f, Game.Random.Float( -1f, 1f ), Game.Random.Float( -1f, 1f ) ).Normal;
				var distance = reach * MathF.Sqrt( Game.Random.Float( 0.02f, 1f ) );
				var spot = point + direction * distance;

				if ( placed.Any( x => x.Position.Distance( spot ) < (x.Radius + radius) * 0.6f ) )
					continue;

				// Find the surface again, so droplets follow it round corners instead of hanging in the air
				var tr = Scene.Trace.Ray( spot + normal * 8f, spot - normal * 16f )
					.IgnoreGameObjectHierarchy( GameObject )
					.WithoutTags( "npc", "player", "ragdoll" )
					.Run();

				if ( !tr.Hit )
					continue;

				SpawnBloodDecal( definition, tr.HitPosition, tr.Normal, tr.GameObject, scale );
				placed.Add( (spot, radius) );
				break;
			}
		}
	}

	private static DecalDefinition PickBloodDecal()
	{
		return ResourceLibrary.Get<DecalDefinition>( Game.Random.FromArray( BloodDecals ) );
	}

	// Roughly how far the visible blood reaches from the decal's centre - the textures have a clear
	// border around the splat, so a bit under half the decal's width
	private static float BloodRadius( DecalDefinition definition, float scale )
	{
		return definition is null ? 0f : definition.Width * scale * 0.35f;
	}

	private static void SpawnBloodDecal( DecalDefinition definition, Vector3 point, Vector3 normal, GameObject surface, float scale )
	{
		if ( definition is null )
			return;

		var go = new GameObject( true, "blood" );
		go.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		go.WorldPosition = point + normal;
		go.WorldRotation = Rotation.LookAt( -normal );

		// Stick to whatever we hit, so it moves with props
		if ( surface.IsValid() )
			go.SetParent( surface, true );

		var decal = go.Components.Create<Decal>();
		decal.Decals = [definition];
		decal.Scale = scale;
		decal.Rotation = Game.Random.Float( 0f, 360f );
		decal.Transient = true;
	}
}
