using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Sandbox;

[Expose]
public partial class Particle : IDynamicFloatContext
{
	[ActionGraphInclude]
	public Vector3 Position;

	[ActionGraphInclude]
	public Vector3 Size;

	[ActionGraphInclude]
	public Vector3 Velocity;
	public Color Color;
	public Color OverlayColor;
	public float Alpha;
	public float BornTime;
	public float Age;
	public float Radius;
	public Angles Angles;
	public int Sequence;
	public Vector3 SequenceTime;
	public int Frame;

	int RandomSeed;
	internal bool hasUpdated;

	[System.Obsolete]
	public float Random01;

	[System.Obsolete]
	public float Random02;

	[System.Obsolete]
	public float Random03;

	[System.Obsolete]
	public float Random04;

	[System.Obsolete]
	public float Random05;

	[System.Obsolete]
	public float Random06;

	[System.Obsolete]
	public float Random07;

	[ActionGraphInclude]
	public Vector3 HitPos;
	[ActionGraphInclude]
	public Vector3 HitNormal;

	public float HitTime;
	public float LastHitTime;
	public Vector3 StartPosition;

	/// <summary>
	/// A range from 0 to 1 descriving how long this particle has been alive
	/// </summary>
	public float LifeDelta;

	/// <summary>
	/// The time that this particle is scheduled to die
	/// </summary>
	public float DeathTime;


	public float TimeScale;

	/// <summary>
	/// A GameObject that is following us. Might be emitting other particles or something.
	/// </summary>
	internal GameObject Follower;


	public float LifeTimeRemaining => DeathTime - BornTime;

	float IDynamicFloatContext.LifetimeDelta => LifeDelta;
	int IDynamicFloatContext.RandomSeed => RandomSeed;

	private Dictionary<string, object> _data;

	public static Queue<Particle> Pool = new( 512 );

	public static Particle Create()
	{
		if ( !Pool.TryDequeue( out Particle p ) )
		{
			p = new Particle();
		}

		p.RandomSeed = Random.Shared.Int( 0, 10000 );
		p.BornTime = Time.Now;
		p.Age = 0;
		p.Angles = Angles.Zero;
		p.Frame = 0;
		p.Velocity = 0;
		p.Color = Color.White;
		p.OverlayColor = Color.White.WithAlpha( 0 );
		p.Alpha = 1;
		p.Sequence = 0;
		p.SequenceTime = 0;
		p.Size = 5;
		p.HitTime = -1000;
		p.LastHitTime = -1000;
		p.TimeScale = 1;
		p._data?.Clear();
		p._controllers?.Clear();
		p.hasUpdated = false;

		return p;
	}

	/// <summary>
	/// Get an arbituary data value
	/// </summary>
	public T Get<T>( string key )
	{
		if ( _data is null ) return default;

		if ( !_data.TryGetValue( key, out var val ) )
			return default;

		return (T)val;
	}

	/// <summary>
	/// Set an arbituary data value
	/// </summary>
	public void Set<T>( string key, T tvalue )
	{
		_data ??= new Dictionary<string, object>( 4, StringComparer.OrdinalIgnoreCase );
		_data[key] = tvalue;
	}

	public void ApplyDamping( in float amount )
	{
		Velocity = Velocity.WithFriction( amount, 100.0f );
	}

	internal bool MoveWithCollision( in float bounce, in float friction, in float bumpiness, in float push, in bool die, in float dt, float radius, in SceneTrace trace, ConcurrentBag<ParticleEffect.DeferredParticleForce> deferredForces )
	{
		const float surfaceOffset = 0.1f;

		// We previously hit something.
		// Keep the surface normal out of our velocity
		// Periodically check whether it's still there.
		if ( HitTime > 0 )
		{
			// if time passed, or we moved too far, see if it's still there
			bool recheck = HitTime < Time.Now - 0.1f || HitPos.Distance( Position ) > 16;

			if ( recheck )
			{
				var checkTrace = trace.Ray( Position, Position + HitNormal * surfaceOffset * -2.0f )
								.Radius( radius * Radius )
								.Run();

				if ( checkTrace.Hit )
				{
					HitPos = checkTrace.HitPosition;
					HitNormal = checkTrace.Normal;
					HitTime = Time.Now;
				}
				else
				{
					HitTime = 0;
					HitPos = 0;
					HitNormal = 0;
				}
			}

			if ( HitTime > 0 )
			{
				LastHitTime = Time.Now;
				// Keep removing the ground velocity
				Velocity = Velocity.SubtractDirection( HitNormal );
			}

		}

		if ( LastHitTime > Time.Now - 0.03f )
		{
			ApplyDamping( friction * dt * 5.0f );
		}

		var targetPosition = Position + Velocity * dt;

		var tr = trace.Ray( Position, targetPosition )
										.Radius( radius * Radius )
										.Run();
		if ( !tr.Hit )
		{
			Position = targetPosition;
			return false;
		}

		//
		// If we want to die on collision then set its age to max
		//
		if ( die )
		{
			Age = float.MaxValue;
		}

		//
		// If we have push, then push the physics object we hit
		//
		if ( push != 0 && tr.Body is not null )
		{
			deferredForces?.Add( new( tr.Body, tr.HitPosition, Velocity * tr.Body.Mass * push ) );
		}

		HitPos = tr.HitPosition;
		HitNormal = tr.Normal;
		HitTime = Time.Now;

		var velocity = Velocity;
		var speed = Velocity.Length;

		var surfaceNormal = tr.Normal;

		// make the hit normal bumpy if we have bumpiness
		if ( speed > 10f && bumpiness > 0 )
		{
			surfaceNormal += Vector3.Random * bumpiness * 0.5f;
		}

		var surfaceVelocityNormal = velocity.SubtractDirection( surfaceNormal, 1 + bounce ).Normal;

		targetPosition = tr.EndPosition;// + tr.Normal * surfaceOffset;

		Velocity = surfaceVelocityNormal * speed;

		if ( bounce > 0 && Velocity.Dot( tr.Normal ) > 5.0f )
		{
			HitTime = 0;
		}

		Position = targetPosition;
		return true;
	}


	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public float Rand( int seed = 0, [CallerLineNumber] int line = 0 )
	{
		int i = RandomSeed + (line * 20) + seed;
		return Game.Random.FloatDeterministic( i );
	}
}
