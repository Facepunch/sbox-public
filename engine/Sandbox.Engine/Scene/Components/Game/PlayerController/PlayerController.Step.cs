namespace Sandbox;

public sealed partial class PlayerController : Component
{
	/// <summary>
	/// Enable debug overlays for this character
	/// </summary>
	public bool StepDebug { get; set; } = false;

	// set when we stepped this tick, so at the end of the physics step we can restore our position
	bool _didstep;

	// if we stepped, this holds the position we moved to
	Vector3 _stepPosition;

	/// <summary>
	/// Try to step up. Will trace forward, then up, then across, then down.
	/// </summary>
	internal void TryStep( float maxDistance )
	{
		_didstep = false;

		if ( !Body.IsValid() ) return;
		var up = UpDirection;
		var velocity = WithoutVertical( Body.Velocity );
		if ( velocity.IsNearlyZero() ) return;
		if ( _timeUntilAllowedGround > 0 ) return;

		var from = WorldPosition;
		var vel = velocity * Time.Delta;
		float radiusScale = 1.0f;

		SceneTraceResult result;

		//
		// Trace forwards, in our current velocity direction
		//
		{
			var traceOffset = Scene.Is2D ? up : 0;
			var a = from + traceOffset - vel.Normal * _skin;
			var b = from + traceOffset + vel;

			result = TraceBody( a, b, radiusScale );

			// If we're inside something, lose girth until we're not
			while ( result.StartedSolid )
			{
				radiusScale = radiusScale - 0.1f;
				if ( radiusScale < 0.6f )
					return;

				result = TraceBody( a, b, radiusScale );
			}

			// If we didn't hit anything, we're done here
			if ( !result.Hit )
				return;

			if ( StepDebug )
			{
				DebugOverlay.Line( a, b, duration: 10, color: Color.Green );
			}

			var direction = vel.Normal;
			var remainingDistance = vel.Length - result.Distance;
			from = result.EndPosition;

			if ( Scene.Is2D )
			{
				from -= direction * _skin;
				remainingDistance = (b - from).Dot( direction );
			}

			if ( remainingDistance <= 0 ) return;
			vel = direction * remainingDistance;
		}

		//
		// We hit a step, move upwards from this point, one step up
		//
		{
			var uppoint = from + up * maxDistance;

			// move up 
			result = TraceBody( from, uppoint, radiusScale );

			if ( result.StartedSolid )
				return;

			// If we hit our head almost immediately, it's too tight to step up
			// we need to draw the line somewhere
			if ( result.Distance < 2 )
			{
				if ( StepDebug ) DebugOverlay.Line( from, result.EndPosition, duration: 10, color: Color.Red );
				return;
			}

			if ( StepDebug )
			{
				DebugOverlay.Line( from, result.EndPosition, duration: 10, color: Color.Green );
			}
		}

		// Move across
		{
			// move across
			var a = result.EndPosition;
			var b = a + vel;

			result = TraceBody( a, b, radiusScale );
			if ( result.StartedSolid )
				return;

			if ( StepDebug )
			{
				DebugOverlay.Line( a, b, duration: 10, color: Color.Green );
			}
		}

		//
		// Step Down, back to the ground
		// 
		{
			var dist = result.Distance;
			var top = result.EndPosition;
			var bottom = result.EndPosition - up * maxDistance;

			result = TraceBody( top, bottom, radiusScale );

			// no ground here (!)
			if ( !result.Hit )
			{
				if ( StepDebug ) DebugOverlay.Line( top, bottom, duration: 10, color: Color.Red );
				return;
			}

			// can't stand here
			if ( !Mode.IsStandableSurface( result ) )
				return;

			// didn't step up enough to bother - returning here avoids getting stuck on corners when there's a ceiling above (due to RestoreStep preventing moving forward)
			if ( (result.EndPosition - Body.WorldPosition).Dot( up ).AlmostEqual( 0, 0.015f ) )
				return;

			_didstep = true;
			_stepPosition = result.EndPosition + up * _skin;

			Body.WorldPosition = _stepPosition;

			// Kill vertical velocity when stepping
			// so we don't launch into the air
			Body.Velocity = WithoutVertical( Body.Velocity ) * 0.9f;

			if ( StepDebug )
			{
				DebugOverlay.Line( top, _stepPosition, duration: 10, color: Color.Green );
			}
		}
	}

	/// <summary>
	/// If we stepped up on the previous step, we suck our position back to the previous position after the physics step
	/// to avoid adding double velocity. This is technically wrong but doesn't seem to cause any harm right now
	/// </summary>
	void RestoreStep()
	{
		if ( _didstep )
		{
			_didstep = false;
			Body.WorldPosition = _stepPosition;
		}
	}
}
