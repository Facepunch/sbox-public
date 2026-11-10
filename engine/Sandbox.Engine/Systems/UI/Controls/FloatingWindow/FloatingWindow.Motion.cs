namespace Sandbox.UI;

/// <summary>
/// Lets deliberate flicks carry the window after release while ordinary drags stay where they are placed.
/// </summary>
public partial class FloatingWindow
{
	const float ThrowSpeed = 900;
	const float MaximumThrowSpeed = 3000;
	const float StopSpeed = 25;
	const float Friction = 3;
	const float BounceRetention = 0.65f;
	const double VelocityHistory = 0.1;
	const double ReleaseHistory = 0.035;

	readonly DragSample[] dragSamples = new DragSample[64];
	int dragSampleCount;
	int nextDragSample;
	Vector2 throwVelocity;

	readonly record struct DragSample( Vector2 Position, double Time );

	/// <summary>
	/// Whether a thrown window is still moving after the mouse was released.
	/// </summary>
	public bool IsThrowing => throwVelocity.LengthSquared > 0;

	void BeginDragMotion( Vector2 delta )
	{
		StopThrow();
		dragSampleCount = 0;
		nextDragSample = 0;
		SampleDragMotion( delta );
	}

	void SampleDragMotion( Vector2 delta )
	{
		var sample = new DragSample( delta * ScaleFromScreen, RealTime.Now );
		var lastIndex = (nextDragSample + dragSamples.Length - 1) % dragSamples.Length;

		// Drag-end can arrive in the same frame as the final movement.
		if ( dragSampleCount > 0 && dragSamples[lastIndex].Time == sample.Time )
		{
			dragSamples[lastIndex] = sample;
			return;
		}

		dragSamples[nextDragSample] = sample;
		nextDragSample = (nextDragSample + 1) % dragSamples.Length;
		dragSampleCount = Math.Min( dragSampleCount + 1, dragSamples.Length );
	}

	void StartThrow()
	{
		if ( dragSampleCount < 2 ) return;

		var last = dragSamples[(nextDragSample + dragSamples.Length - 1) % dragSamples.Length];
		var start = last;
		var recent = last;

		for ( var i = 1; i < dragSampleCount; i++ )
		{
			var sample = dragSamples[(nextDragSample + dragSamples.Length - 1 - i) % dragSamples.Length];
			var age = last.Time - sample.Time;
			if ( age > VelocityHistory ) break;
			start = sample;
			if ( age <= ReleaseHistory ) recent = sample;
		}

		var duration = (float)(last.Time - start.Time);
		var recentDuration = (float)(last.Time - recent.Time);
		if ( duration < 0.025f || recentDuration < 0.01f ) return;

		var distance = last.Position - start.Position;
		var velocity = distance / duration;
		var releaseVelocity = (last.Position - recent.Position) / recentDuration;

		// Both the gesture and its final movement must be fast. Slowing down to place the
		// window, pausing, or reversing direction should never turn into an accidental throw.
		if ( distance.Length < 35 || velocity.Length < ThrowSpeed || releaseVelocity.Length < ThrowSpeed ) return;
		if ( Vector2.Dot( velocity.Normal, releaseVelocity.Normal ) < 0.8f ) return;

		throwVelocity = Vector2.Lerp( velocity, releaseVelocity, 0.7f );
		if ( throwVelocity.Length > MaximumThrowSpeed ) throwVelocity = throwVelocity.Normal * MaximumThrowSpeed;
	}

	void StopThrow()
	{
		throwVelocity = Vector2.Zero;
	}

	void TickThrow()
	{
		if ( !IsThrowing ) return;
		if ( !IsVisible || IsDragging || IsMinimized || IsAnimating )
		{
			StopThrow();
			return;
		}

		var location = position ?? Box.Rect.Position;
		var bounds = FindRootPanel().Box.Rect;
		var right = Math.Max( bounds.Left, bounds.Right - Box.Rect.Width );
		var bottom = Math.Max( bounds.Top, bounds.Bottom - Box.Rect.Height );
		var remaining = Math.Min( RealTime.Delta, 0.05f );

		// Small steps keep fast throws from skipping over an edge on a slow frame.
		while ( remaining > 0 )
		{
			var step = Math.Min( remaining, 1f / 120 );
			remaining -= step;
			var damping = MathF.Exp( -Friction * step );
			location += throwVelocity * ((1 - damping) / Friction) * ScaleToScreen;
			throwVelocity *= damping;

			if ( KeepOnScreen )
			{
				(location.x, throwVelocity.x) = BounceAxis( location.x, throwVelocity.x, bounds.Left, right );
				(location.y, throwVelocity.y) = BounceAxis( location.y, throwVelocity.y, bounds.Top, bottom );
			}
		}

		position = location;
		SetNeedsFinalLayout();
		if ( throwVelocity.Length < StopSpeed ) StopThrow();
	}

	static (float Position, float Velocity) BounceAxis( float location, float velocity, float minimum, float maximum )
	{
		if ( maximum <= minimum )
		{
			location = minimum;
			velocity = 0;
		}
		else if ( location < minimum )
		{
			location = minimum;
			if ( velocity < 0 ) velocity = -velocity * BounceRetention;
		}
		else if ( location > maximum )
		{
			location = maximum;
			if ( velocity > 0 ) velocity = -velocity * BounceRetention;
		}

		return (location, velocity);
	}
}
