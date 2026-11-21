/// <summary>
/// Shakes nearby cameras when a physics prop or building block strikes static world geometry.
/// Listening at scene level also covers map props, duplicates and restored saves.
/// </summary>
public sealed partial class GameManager : ISceneCollisionEvents
{
	private const float MinimumImpactSpeed = 100f;
	private const float MinimumAmplitude = 0.1f;
	private const float MaximumAmplitude = 4f;
	private const float ShakeRadius = 512f;
	private const float ShakeDuration = 0.32f;
	private const float ImpactCooldown = 0.1f;

	private readonly record struct Impact( Vector3 Position, float Amplitude );
	private readonly Dictionary<Component, Impact> _pending = new();
	private readonly Dictionary<Component, float> _nextImpactTime = new();
	private PropImpactCameraEffect _propImpactEffect;

	void IScenePhysicsEvents.PostPhysicsStep() => ProcessImpacts();

	void ISceneCollisionEvents.OnCollisionHit( Collision collision )
	{
		if ( Scene.IsEditor || !Networking.IsHost ) return;
		if ( collision.Self.IsTrigger || collision.Other.IsTrigger ) return;

		// The physics event can report the prop on either side of the contact.
		QueueImpact( collision.Self, collision.Other, collision.Contact );
		QueueImpact( collision.Other, collision.Self, collision.Contact );
	}

	private void QueueImpact( CollisionSource source, CollisionSource world, PhysicsContact contact )
	{
		if ( !source.Body.IsValid() || !world.Body.IsValid() ) return;
		if ( source.Body.BodyType != PhysicsBodyType.Dynamic || !source.Body.MotionEnabled ) return;
		if ( world.Body.BodyType != PhysicsBodyType.Static ) return;
		if ( !source.GameObject.IsValid() ) return;

		var prop = FindProp( source.GameObject );
		if ( !prop.IsValid() || prop.IsProxy ) return;

		// Frozen/static props are still props, rather than part of the world.
		if ( world.GameObject.IsValid() && FindProp( world.GameObject ).IsValid() ) return;

		var speed = MathF.Abs( contact.NormalSpeed );
		if ( speed < MinimumImpactSpeed ) return;

		// Use the body's current mass so the mass tool and model physics both apply.
		// A 50 kg prop hitting at 200 units/sec produces one unit of shake at the impact.
		var amplitude = (source.Body.Mass * speed / 10000f).Clamp( 0f, MaximumAmplitude );

		if ( amplitude < MinimumAmplitude ) return;
		if ( _nextImpactTime.TryGetValue( prop, out var nextTime ) && Time.Now < nextTime ) return;

		// Several shapes/substeps can report the same impact; keep only the strongest.
		if ( _pending.TryGetValue( prop, out var pending ) && pending.Amplitude >= amplitude ) return;
		_pending[prop] = new Impact( contact.Point, amplitude );
	}

	private static Component FindProp( GameObject go )
	{
		var prop = go.GetComponentInParent<Prop>();
		if ( prop.IsValid() ) return prop;

		return go.GetComponentInParent<BuilderProp>();
	}

	private void ProcessImpacts()
	{
		// Dictionary allows Remove while enumerating
		foreach ( var (prop, nextTime) in _nextImpactTime )
		{
			if ( !prop.IsValid() || Time.Now >= nextTime )
				_nextImpactTime.Remove( prop );
		}

		foreach ( var (prop, impact) in _pending )
		{
			BroadcastImpact( impact.Position, impact.Amplitude );
			if ( prop.IsValid() )
				_nextImpactTime[prop] = Time.Now + ImpactCooldown;
		}

		_pending.Clear();
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Unreliable )]
	private void BroadcastImpact( Vector3 position, float amplitude )
	{
		if ( Scene.IsEditor || GamePreferences.Screenshake <= 0f ) return;

		var effects = CameraEffectSystem.Get( Scene );
		if ( effects is null ) return;
		effects.Scale = GamePreferences.Screenshake;
		var camera = Scene.Camera;
		if ( !camera.IsValid() ) return;
		if ( camera.WorldPosition.Distance( position ) >= ShakeRadius ) return;

		// A pile of colliding props should produce one readable jolt, not stacked jitter.
		if ( _propImpactEffect is { IsDone: false } active )
		{
			var incomingStrength = amplitude * (1f - camera.WorldPosition.Distance( position ) / ShakeRadius);
			var activeStrength = active.Amplitude * active.ScaleFor( camera );
			if ( active.TimeAlive < 0.12f && incomingStrength <= activeStrength ) return;
			active.Stop();
		}

		var direction = (position - camera.View.Position).Normal;
		_propImpactEffect = effects.Add( new PropImpactCameraEffect
		{
			Camera = camera,
			Epicenter = position,
			Radius = ShakeRadius,
			Duration = ShakeDuration,
			Amplitude = amplitude,
			Side = camera.View.Rotation.Right.Dot( direction )
		} );
	}
}
