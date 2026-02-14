namespace Sandbox;

/// <summary>
/// Applies damage in a radius, with physics force, and optional occlusion
/// </summary>
[Category( "Game" ), Icon( "flare" ), EditorHandle( Icon = "💥" )]
public sealed class RadiusDamage : Component
{
	/// <summary>
	/// The radius of the damage area.
	/// </summary>
	[Property]
	public float Radius { get; set; } = 512;

	/// <summary>
	/// How much physics force should be applied on explosion?
	/// </summary>
	[Property]
	public float PhysicsForceScale { get; set; } = 1;

	/// <summary>
	/// If enabled we'll apply damage once as soon as enabled
	/// </summary>
	[Property]
	public bool DamageOnEnabled { get; set; } = true;

	/// <summary>
	/// Should the world shield victims from damage?
	/// </summary>
	[Property]
	public bool Occlusion { get; set; } = true;

	/// <summary>
	/// The amount of damage inflicted
	/// </summary>
	[Property]
	public float DamageAmount { get; set; } = 100;

	/// <summary>
	/// Tags to apply to the damage
	/// </summary>
	[Property]
	public TagSet DamageTags { get; set; } = new TagSet();

	/// <summary>
	/// Who should we credit with this attack?
	/// </summary>
	[Property]
	public GameObject Attacker { get; set; }

	protected override void OnEnabled()
	{
		base.OnEnabled();

		if ( DamageOnEnabled )
		{
			Apply();
		}
	}

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected )
			return;

		Gizmo.Draw.LineSphere( new Sphere( 0, Radius ), 16 );
	}

	/// <summary>
	/// Apply the damage now
	/// </summary>
	public void Apply()
	{
		var sphere = new Sphere( WorldPosition, Radius );

		var dmg = new DamageInfo();
		dmg.Weapon = GameObject;
		dmg.Damage = DamageAmount;
		dmg.Tags.Add( DamageTags );
		dmg.Attacker = Attacker;

		ApplyDamage( sphere, dmg, PhysicsForceScale );
	}

	public static void ApplyDamage( Sphere sphere, DamageInfo damage, float physicsForce = 1, GameObject ignore = null )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;

		var point = sphere.Center;
		var damageAmount = damage.Damage;
		var objectsInArea = scene.FindInPhysics( sphere );

		var losTrace = scene.Trace.WithTag( "map" ).WithoutTags( "trigger", "gib", "debris", "player" );

		foreach ( var rb in objectsInArea.SelectMany( x => x.GetComponents<Rigidbody>() ).Distinct() )
		{
			if ( rb.IsProxy ) continue;
			if ( !rb.MotionEnabled ) continue;

			if ( ignore.IsValid() && ignore.IsDescendant( rb.GameObject ) )
				continue;

			// If the object isn't in line of sight, fuck it off
			var tr = losTrace.Ray( point, rb.WorldPosition ).Run();
			if ( tr.Hit && tr.GameObject.IsValid() )
			{
				if ( !rb.GameObject.Root.IsDescendant( tr.GameObject ) )
					continue;
			}

			var dir = (rb.WorldPosition - point).Normal;
			var distance = rb.WorldPosition.Distance( sphere.Center );

			var forceMagnitude = Math.Clamp( 10000000000f / (distance * distance + 1), 0, 10000000000f );
			forceMagnitude += physicsForce * (1 - (distance / sphere.Radius));

			rb.ApplyForceAt( point, dir * forceMagnitude );
		}

		foreach ( var damageable in objectsInArea.SelectMany( x => x.GetComponentsInParent<Component.IDamageable>().Distinct() ) )
		{
			// no proxy checks needed, it's up to the OnDamage call to filter

			var target = damageable as Component;

			if ( ignore.IsValid() && ignore.IsDescendant( target.GameObject ) )
				continue;

			// If the object isn't in line of sight, fuck it off
			var tr = losTrace.Ray( point, target.WorldPosition ).Run();
			if ( tr.Hit && tr.GameObject.IsValid() )
			{
				if ( !target.GameObject.Root.IsDescendant( tr.GameObject ) )
					continue;
			}

			var distance = target.WorldPosition.Distance( point );
			var distanceLinear = distance / sphere.Radius;
			distanceLinear = distanceLinear.Clamp( 0, 1 );

			damage.Damage = damageAmount * distanceLinear;
			var direction = (target.WorldPosition - point).Normal;
			var force = direction * distance * 50f;

			damage.Origin = sphere.Center;
			damage.Position = tr.HitPosition;
			damageable.OnDamage( damage );
		}

		damage.Damage = damageAmount;
	}
}
