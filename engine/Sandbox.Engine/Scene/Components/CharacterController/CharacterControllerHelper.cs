using System.Buffers;

namespace Sandbox;

[Expose]
public struct CharacterControllerHelper
{
	//
	// Inputs and Outputs
	//
	public Vector3 Position;
	public Vector3 Velocity;

	//
	// Config
	//
	public float Bounce;
	public float MaxStandableAngle;
	public SceneTrace Trace;

	public CharacterControllerHelper( SceneTrace trace, Vector3 position, Vector3 velocity ) : this()
	{
		Velocity = velocity;
		Position = position;
		Bounce = 0.0f;
		MaxStandableAngle = 10.0f;
		Trace = trace;
	}

	bool Is2D => Trace.scene is { Is2D: true };
	Vector3 UpDirection => Is2D ? Vector2.Up : Vector3.Up;

	Vector3 WithoutVertical( Vector3 value )
	{
		var up = UpDirection;
		return value - up * value.Dot( up );
	}

	/// <summary>
	/// Trace this from one position to another
	/// </summary>
	public SceneTraceResult TraceFromTo( Vector3 start, Vector3 end )
	{
		return Trace.FromTo( start, end ).Run();
	}

	/// <summary>
	/// Try to move to the position. Will return the fraction of the desired velocity that we traveled.
	/// Position and Velocity will be what we recommend using.
	/// </summary>
	public float TryMove( float timestep )
	{
		var timeLeft = timestep;
		float travelFraction = 0;

		using var moveplanes = new VelocityClipPlanes( Velocity, 3 );

		for ( int bump = 0; bump < moveplanes.Max; bump++ )
		{
			if ( Velocity.Length.AlmostEqual( 0.0f ) )
				break;

			var pm = TraceFromTo( Position, Position + Velocity * timeLeft );

			travelFraction += pm.Fraction;
			timeLeft -= timeLeft * pm.Fraction;

			if ( !pm.Hit )
			{
				Position = pm.EndPosition;
				break;
			}

			if ( pm.Fraction > 0 )
			{
				Position = pm.EndPosition;// + pm.Normal * 0.001f;
				moveplanes.StartBump( Velocity );
			}

			bool standable = pm.Normal.Angle( UpDirection ) <= MaxStandableAngle;

			//Gizmo.Transform = Transform.Zero;

			//var dot = Velocity.Normal.Dot( pm.Normal );
			//Gizmo.Draw.Color = Color.White;
			//Gizmo.Draw.Text( $"{bump}\n{pm.Normal}\n{dot}", new Transform( pm.StartPosition ) );
			//Gizmo.Draw.Line( pm.StartPosition + Vector3.Up * 5, pm.EndPosition + Vector3.Up * 5 );

			//Gizmo.Draw.Color = Color.Green;
			//Gizmo.Draw.Line( pm.StartPosition + Vector3.Up * 5, pm.StartPosition + Vector3.Up * 5 + pm.Normal * 3 );

			if ( !moveplanes.TryAdd( pm.Normal, ref Velocity, standable ? 0 : Bounce ) )
				break;
		}

		return travelFraction;
	}

	/// <summary>
	/// Move our position by this delta using trace. If we hit something we'll stop,
	/// we won't slide across it nicely like TryMove does.
	/// </summary>
	public SceneTraceResult TraceMove( Vector3 delta )
	{
		var tr = TraceFromTo( Position, Position + delta );
		Position = tr.EndPosition;
		return tr;
	}

	/// <summary>
	/// Like TryMove but will also try to step up if it hits a wall
	/// </summary>
	public float TryMoveWithStep( float timeDelta, float stepsize )
	{
		var startPosition = Position;

		// Make a copy of us to stepMove
		var stepMove = this;

		// Do a regular move
		var fraction = TryMove( timeDelta );

		// If it got almost all the way then that's cool, use it
		if ( fraction >= 0.99f )
			return fraction;

		// Move up (as much as we can)
		stepMove.TraceMove( UpDirection * stepsize );

		// if the move delta is too low, we probably won't get up a step
		Vector3 moveBack = 0;
		var moveDelta = WithoutVertical( stepMove.Velocity ) * timeDelta;
		var deltaLen = moveDelta.Length;

		// if it's really low, then we're probably moving straight up or down
		// so lets just early out now
		if ( deltaLen < 0.001f )
			return fraction;

		if ( deltaLen < 0.5f )
		{
			var newDelta = moveDelta.Normal * 0.5f;
			moveBack = moveDelta - newDelta;
			moveDelta = newDelta;
		}

		// Move across (using existing velocity)
		var stepFraction = stepMove.TraceMove( moveDelta );

		// Move back down
		var tr = stepMove.TraceMove( -UpDirection * stepsize );

		// if we didn't land on something, return
		if ( !tr.Hit ) return fraction;

		// If we landed on a wall then this is no good
		if ( tr.Normal.Angle( UpDirection ) > MaxStandableAngle )
			return fraction;

		// if the original non stepped attempt moved further use that
		if ( WithoutVertical( Position - startPosition ).LengthSquared > WithoutVertical( stepMove.Position - startPosition ).LengthSquared )
			return fraction;

		if ( !moveBack.IsNearZeroLength )
		{
			stepMove.TraceMove( moveBack );
		}

		// step move moved further, copy its data to us
		Position = stepMove.Position;
		Velocity = stepMove.Velocity;

		return stepFraction.Fraction;
	}
}



/// <summary>
/// Used to store a list of planes that an object is going to hit, and then
/// remove velocity from them so the object can slide over the surface without
/// going through any of the planes.
/// </summary>
file struct VelocityClipPlanes : IDisposable
{
	Vector3 OrginalVelocity;
	Vector3 BumpVelocity;
	Vector3[] Planes;

	/// <summary>
	/// Maximum number of planes that can be hit
	/// </summary>
	public int Max { get; private set; }

	/// <summary>
	/// Number of planes we're currently holding
	/// </summary>
	public int Count { get; private set; }

	public VelocityClipPlanes( Vector3 originalVelocity, int max )
	{
		Max = max;
		OrginalVelocity = originalVelocity;
		BumpVelocity = originalVelocity;
		Planes = ArrayPool<Vector3>.Shared.Rent( max );
		Count = 0;
	}

	/// <summary>
	/// Try to add this plane and restrain velocity to it (and its brothers)
	/// </summary>
	/// <returns>False if we ran out of room and should stop adding planes</returns>
	public bool TryAdd( Vector3 normal, ref Vector3 velocity, float bounce )
	{
		if ( Count == Max )
		{
			return false;
		}

		Planes[Count++] = normal;

		//
		// if we only hit one plane then apply the bounce
		//
		if ( Count == 1 )
		{
			BumpVelocity = ClipVelocity( OrginalVelocity, normal, 1.0f + bounce );
			OrginalVelocity = BumpVelocity;
			velocity = BumpVelocity;

			return true;
		}

		if ( TryClip( ref velocity ) )
		{
			// Hit the floor and the wall, go along the join
			if ( Count == 2 )
			{
				var dir = Vector3.Cross( Planes[0], Planes[1] ).Normal;
				var d = dir.Dot( velocity );
				velocity = dir * d;
			}
			else
			{
				velocity = Vector3.Zero;
				return false;
			}
		}

		//
		// We're moving in the opposite direction to our
		// original intention so just stop right there.
		//
		if ( velocity.Dot( OrginalVelocity ) <= 0 )
		{
			velocity = 0;
			return false;
		}

		return true;
	}

	/// <summary>
	/// Try to clip our velocity to all the planes, so we're not travelling into them
	/// Returns true if we clipped properly
	/// </summary>
	bool TryClip( ref Vector3 velocity )
	{
		for ( int i = 0; i < Count; i++ )
		{
			velocity = ClipVelocity( OrginalVelocity, Planes[i] );

			if ( MovingTowardsAnyPlane( velocity, i ) )
				return false;
		}

		return true;
	}

	/// <summary>
	/// Returns true if we're moving towards any of our planes (except for skip)
	/// </summary>
	bool MovingTowardsAnyPlane( Vector3 velocity, int iSkip )
	{
		for ( int j = 0; j < Count; j++ )
		{
			if ( j == iSkip ) continue;
			if ( velocity.Dot( Planes[j] ) < 0 ) return false;
		}

		return true;
	}

	/// <summary>
	/// Start a new bump. Clears planes and resets BumpVelocity
	/// </summary>
	public void StartBump( Vector3 velocity )
	{
		BumpVelocity = velocity;
		Count = 0;
	}

	/// <summary>
	/// Clip the velocity to the normal
	/// </summary>
	Vector3 ClipVelocity( Vector3 vel, Vector3 norm, float overbounce = 1.0f )
	{
		var backoff = Vector3.Dot( vel, norm ) * overbounce;
		var o = vel - norm * backoff;

		// garry: I don't totally understand how we could still
		//		  be travelling towards the norm, but the hl2 code
		//		  does another check here, so we're going to too.

		var adjust = Vector3.Dot( o, norm );

		if ( adjust < 0.0f )
		{
			o -= norm * adjust;
		}

		return o;
	}

	public void Dispose()
	{
		ArrayPool<Vector3>.Shared.Return( Planes );
	}
}
