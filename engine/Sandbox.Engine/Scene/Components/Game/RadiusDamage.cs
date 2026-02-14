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
	/// Define extra tags for colliders that shield from damage.
	/// "map" tagged colliders always shield from damage if Occlusion is true
	/// </summary>
	[Property] public TagSet ExtraOccludingTags { get; set; } = new TagSet();
	
	/// <summary>
	/// Define extra tags for colliders that don't shield from damage.
	/// "trigger", "gib", "debris", "player" tagged colliders never shield from damage
	/// </summary>
	[Property] public TagSet ExtraNotOccludingTags { get; set; } = new TagSet();

	/// <summary>
	/// The amount of damage inflicted
	/// </summary>
	[Property]
	public float DamageAmount { get; set; } = 100;
	
	/// <summary>
	/// Damage falloff over distance
	/// </summary>
	[Property] public Curve DamageFalloff { get; set; } = new Curve( new Curve.Frame( 0.0f, 1.0f ), new Curve.Frame( 1.0f, 0.0f ) );

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
		
		if ( DamageFalloff.Frames.IsEmpty )
		{
			DamageFalloff = new Curve( new Curve.Frame( 0.0f, 1.0f ), new Curve.Frame( 1.0f, 0.0f ) );
		}

		var dmg = new DamageInfo();
		dmg.Weapon = GameObject;
		dmg.Damage = DamageAmount;
		dmg.Tags.Add( DamageTags );
		dmg.Attacker = Attacker;

		ApplyDamage( sphere, dmg, DamageFalloff, PhysicsForceScale, Occlusion, ExtraOccludingTags, ExtraNotOccludingTags );
	}

	public static void ApplyDamage( Sphere sphere, DamageInfo damage, float physicsForce = 1, GameObject ignore = null )
	{
		ApplyDamage(sphere, damage, new Curve( new Curve.Frame( 0.0f, 1.0f ), new Curve.Frame( 1.0f, 0.0f ) ), physicsForce, true , null, null, ignore);
	}
	
	public static void ApplyDamage( Sphere sphere, DamageInfo damage, Curve damageFalloff, float physicsForce = 1,
		bool occlusion = true, GameObject ignore = null )
	{
		ApplyDamage(sphere, damage, damageFalloff, physicsForce, occlusion, null, null, ignore);
	}
	
	public static void ApplyDamage( Sphere sphere, DamageInfo damage, Curve damageFalloff, float physicsForce = 1, bool occlusion = true, TagSet extraOccludingTags = null, TagSet extraNotOccludingTags = null, GameObject ignore = null )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;

		var point = sphere.Center;
		var damageAmount = damage.Damage;
		var objectsInArea = scene.FindInPhysics( sphere );

		var occludingTags = extraOccludingTags != null ? new TagSet(extraOccludingTags) : new TagSet();
		occludingTags.Add( "map" );
		var losTrace = scene.Trace.WithAnyTags(occludingTags).WithoutTags( "trigger", "gib", "debris", "player" );
		
		if (extraNotOccludingTags != null)
			losTrace = losTrace.WithoutTags(extraNotOccludingTags);

		foreach ( var rb in objectsInArea.SelectMany( x => x.GetComponents<Rigidbody>() ).Distinct() )
		{
			if ( rb.IsProxy ) continue;
			if ( !rb.MotionEnabled ) continue;

			if ( ignore.IsValid() && ignore.IsDescendant( rb.GameObject ) )
				continue;

			if ( occlusion )
			{
				// If the object isn't in line of sight, fuck it off
				var tr = losTrace.Ray( point, rb.WorldPosition + rb.MassCenter ).Run();
				if ( tr.Hit && tr.GameObject.IsValid() )
				{
					if ( !rb.GameObject.Root.IsDescendant( tr.GameObject ) )
						continue;
				}
			}

			var dir = (rb.WorldPosition - point).Normal;
			var distance = rb.WorldPosition.Distance( sphere.Center );

			var forceMagnitude = Math.Clamp( 10000000000f / (distance * distance + 1), 0, 10000000000f );
			forceMagnitude += physicsForce * damageFalloff.Evaluate( distance / sphere.Radius );

			rb.ApplyForceAt( point, dir * forceMagnitude );
		}

		foreach ( var damageable in objectsInArea.SelectMany( x => x.GetComponentsInParent<Component.IDamageable>().Distinct() ) )
		{
			// no proxy checks needed, it's up to the OnDamage call to filter

			var target = damageable as Component;

			if ( ignore.IsValid() && ignore.IsDescendant( target.GameObject ) )
				continue;

			var tr = losTrace.Ray( point, target.WorldPosition ).Run();
			if ( occlusion )
			{
				// If the object isn't in line of sight, fuck it off
				if ( tr.Hit && tr.GameObject.IsValid() )
				{
					if ( !target.GameObject.Root.IsDescendant( tr.GameObject ) )
						continue;
				}
			}

			var distance = target.WorldPosition.Distance( point );

			damage.Damage = damageAmount * damageFalloff.Evaluate( distance / sphere.Radius );
			var direction = (target.WorldPosition - point).Normal;
			var force = direction * distance * 50f;

			damage.Origin = sphere.Center;
			damage.Position = tr.HitPosition;
			damageable.OnDamage( damage );
		}

		damage.Damage = damageAmount;
	}
}
