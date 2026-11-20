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
	/// Should objects shield victims from damage?
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
	/// The weapon that caused this damage, if any.
	/// </summary>
	[Property]
	public GameObject Weapon { get; set; }

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
		dmg.Weapon = Weapon.IsValid() ? Weapon : GameObject;
		dmg.Damage = DamageAmount;
		dmg.Tags.Add( DamageTags );
		dmg.Attacker = Attacker;

		ApplyDamage( sphere, dmg, PhysicsForceScale, occlusion: Occlusion );
	}

	public static void ApplyDamage( Sphere sphere, DamageInfo damage, float physicsForce = 1, GameObject ignore = null, bool occlusion = true )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;

		var workspace = ObjectPool<DamageWorkspace>.Get();
		try
		{
			ApplyDamage( scene, sphere, damage, physicsForce, ignore, occlusion, workspace );
		}
		finally
		{
			workspace.Clear();
			ObjectPool<DamageWorkspace>.Return( workspace );
		}
	}

	private sealed class DamageWorkspace
	{
		public readonly HashSet<GameObject> ObjectsInArea = new();
		public readonly HashSet<Rigidbody> Rigidbodies = new();
		public readonly HashSet<Component.IDamageable> Damageables = new();
		public readonly Dictionary<GameObject, int> RootToIndex = new();
		public readonly List<GameObject> RootList = new();
		public readonly List<Rigidbody> ObjectRigidbodies = new();
		public readonly List<Component.IDamageable> ObjectDamageables = new();
		public bool[] PassedLos = Array.Empty<bool>();
		public Vector3[] TraceHitPositions = Array.Empty<Vector3>();

		public void Clear()
		{
			ObjectsInArea.Clear();
			Rigidbodies.Clear();
			Damageables.Clear();
			RootToIndex.Clear();
			RootList.Clear();
			ObjectRigidbodies.Clear();
			ObjectDamageables.Clear();
		}

		public void EnsureTraceCapacity( int count )
		{
			if ( count <= PassedLos.Length ) return;
			var capacity = Math.Max( count, PassedLos.Length * 2 );
			PassedLos = new bool[capacity];
			TraceHitPositions = new Vector3[capacity];
		}
	}

	private static void ApplyDamage( Scene scene, Sphere sphere, DamageInfo damage, float physicsForce, GameObject ignore, bool occlusion, DamageWorkspace workspace )
	{
		var point = sphere.Center;
		var damageAmount = damage.Damage;
		var objectsInArea = workspace.ObjectsInArea;
		scene.FindInPhysics( sphere, objectsInArea );

		var rigidbodies = workspace.Rigidbodies;
		var damageables = workspace.Damageables;
		var rootToIndex = workspace.RootToIndex;
		var rootList = workspace.RootList;
		var objectRigidbodies = workspace.ObjectRigidbodies;
		var objectDamageables = workspace.ObjectDamageables;

		foreach ( var go in objectsInArea )
		{
			// Reuse the query storage across candidates. Chained blasts visit the same dense area repeatedly.
			objectRigidbodies.Clear();
			go.Components.GetAll( objectRigidbodies, FindMode.InSelf | FindMode.Enabled );
			foreach ( var rb in objectRigidbodies )
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

			objectDamageables.Clear();
			go.Components.GetAll( objectDamageables, FindMode.InSelf | FindMode.InAncestors | FindMode.Enabled );
			foreach ( var d in objectDamageables )
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
		workspace.EnsureTraceCapacity( traceCount );
		var passedLos = workspace.PassedLos;
		var traceHitPositions = workspace.TraceHitPositions;

		if ( occlusion && traceCount > 0 )
		{
			var losTrace = scene.Trace.WithoutTags( "trigger", "gib", "debris", "player", "npc" );

			if ( damage.Weapon.IsValid() ) losTrace = losTrace.IgnoreGameObjectHierarchy( damage.Weapon );

			var traces = (Trace: losTrace, Point: point, Ignore: ignore, Roots: rootList, PassedLos: passedLos, HitPositions: traceHitPositions);
			JobDispatcher.For( traceCount, traces, static ( s, idx ) =>
			{
				if ( s.Ignore.IsValid() && s.Ignore.IsDescendant( s.Roots[idx] ) )
				{
					s.PassedLos[idx] = false;
					return;
				}

				var tr = s.Trace.Ray( s.Point, s.Roots[idx].WorldPosition ).Run();
				s.HitPositions[idx] = tr.HitPosition;

				var hitObject = tr.Body?.GameObject;
				s.PassedLos[idx] = !tr.Hit || hitObject is null || s.Roots[idx].IsDescendant( hitObject );
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

		damage.Damage = damageAmount;
	}
}
