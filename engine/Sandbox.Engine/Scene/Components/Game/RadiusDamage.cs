using System.Buffers;

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

		ApplyDamage( sphere, dmg, PhysicsForceScale, occlusion: Occlusion );
	}

	public static void ApplyDamage( Sphere sphere, DamageInfo damage, float physicsForce = 1, GameObject ignore = null, bool occlusion = true )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;

		var point = sphere.Center;
		var damageAmount = damage.Damage;
		var objectsInArea = scene.FindInPhysics( sphere );
		var estimatedCount = (objectsInArea as ICollection<GameObject>)?.Count ?? 16;

		var rigidbodies = new HashSet<Rigidbody>( estimatedCount );
		var damageables = new HashSet<Component.IDamageable>( estimatedCount );
		var rootToIndex = occlusion ? new Dictionary<GameObject, int>( estimatedCount ) : null;
		var rootList = occlusion ? new List<GameObject>( estimatedCount ) : null;

		foreach ( var go in objectsInArea )
		{
			foreach ( var rb in go.GetComponents<Rigidbody>() )
			{
				if ( rb.IsProxy || !rb.MotionEnabled ) continue;
				if ( !rigidbodies.Add( rb ) ) continue;

				if ( occlusion )
				{
					var root = rb.GameObject.Root;
					if ( rootToIndex.TryAdd( root, rootList.Count ) )
						rootList.Add( root );
				}
			}

			foreach ( var d in go.GetComponentsInParent<Component.IDamageable>() )
			{
				if ( !damageables.Add( d ) ) continue;

				if ( occlusion )
				{
					var root = (d as Component).GameObject.Root;
					if ( rootToIndex.TryAdd( root, rootList.Count ) )
						rootList.Add( root );
				}
			}
		}

		if ( rigidbodies.Count == 0 && damageables.Count == 0 ) return;

		var traceCount = occlusion ? rootList.Count : 0;
		var passedLos = occlusion ? ArrayPool<bool>.Shared.Rent( traceCount ) : null;
		var traceHitPositions = occlusion ? ArrayPool<Vector3>.Shared.Rent( traceCount ) : null;

		if ( occlusion && traceCount > 0 )
		{
			var losTrace = scene.PhysicsWorld.Trace.WithTag( "map" ).WithoutTags( "trigger", "gib", "debris", "player" );

			System.Threading.Tasks.Parallel.For( 0, traceCount, idx =>
			{
				if ( ignore.IsValid() && ignore.IsDescendant( rootList[idx] ) )
				{
					passedLos[idx] = false;
					return;
				}

				var tr = losTrace.Ray( point, rootList[idx].WorldPosition ).Run();
				traceHitPositions[idx] = tr.HitPosition;

				var hitObject = tr.Body?.GameObject;
				passedLos[idx] = !tr.Hit || hitObject is null || rootList[idx].IsDescendant( hitObject );
			} );
		}

		foreach ( var rb in rigidbodies )
		{
			if ( ignore.IsValid() && ignore.IsDescendant( rb.GameObject ) )
				continue;

			if ( occlusion && !passedLos[rootToIndex[rb.GameObject.Root]] )
				continue;

			var dir = (rb.WorldPosition - point).Normal;
			var distance = rb.WorldPosition.Distance( sphere.Center );

			var forceMagnitude = Math.Clamp( 10000000000f / (distance * distance + 1), 0, 10000000000f );
			forceMagnitude += physicsForce * (1 - (distance / sphere.Radius));

			rb.ApplyForceAt( point, dir * forceMagnitude );
		}

		foreach ( var damageable in damageables )
		{
			var target = damageable as Component;

			if ( ignore.IsValid() && ignore.IsDescendant( target.GameObject ) )
				continue;

			var rootIdx = occlusion ? rootToIndex[target.GameObject.Root] : -1;

			if ( occlusion && !passedLos[rootIdx] )
				continue;

			var distance = target.WorldPosition.Distance( point );
			var distanceFalloff = 1 - (distance / sphere.Radius).Clamp( 0, 1 );

			damage.Damage = damageAmount * distanceFalloff;
			damage.Origin = sphere.Center;
			damage.Position = occlusion ? traceHitPositions[rootIdx] : target.WorldPosition;
			damageable.OnDamage( damage );
		}

		if ( passedLos is not null ) ArrayPool<bool>.Shared.Return( passedLos );
		if ( traceHitPositions is not null ) ArrayPool<Vector3>.Shared.Return( traceHitPositions );

		damage.Damage = damageAmount;
	}
}
