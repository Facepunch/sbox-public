namespace Sandbox;

/// <summary>
/// A sound's lipsync track - visemes over time, authored in the sound editor and
/// stored in the sound's .meta file. Sample it at the sound's playback time to get
/// viseme weights to drive a face's morphs.
/// </summary>
public sealed class VisemeTrack
{
	/// <summary>
	/// The viseme frames, sorted by start time.
	/// </summary>
	public IReadOnlyList<VisemeFrame> Frames { get; }

	/// <summary>
	/// End time of the last frame, in seconds.
	/// </summary>
	public float Duration { get; }

	// How long visemes blend in and out around their frame, minimum
	const float BlendTime = 0.08f;

	internal VisemeTrack( IEnumerable<VisemeFrame> frames )
	{
		Frames = frames.OrderBy( x => x.StartTime ).ToArray();
		Duration = Frames.Count > 0 ? Frames.Max( x => x.EndTime ) : 0.0f;
	}

	/// <summary>
	/// Get the weight of each viseme at a time, one per entry in
	/// <see cref="Visemes.MorphNames"/>. Frames blend in and out around their span,
	/// stretching the blend to bridge gaps to the next frame - the same shaping the
	/// sound editor preview uses. Returns false if nothing is active at this time,
	/// in which case the weights are all zero.
	/// </summary>
	public bool Sample( float time, Span<float> weights )
	{
		if ( weights.Length < Visemes.Count )
			throw new ArgumentException( $"Need space for {Visemes.Count} weights", nameof( weights ) );

		weights[..Visemes.Count].Clear();

		var frames = Frames;
		var count = frames.Count;
		var any = false;

		// The blend window. When we're inside a frame this stretches to cover the
		// gap to the next frame, and stays stretched for the frames after it - that
		// overlap is what crossfades one viseme into the next.
		var dt = BlendTime;

		for ( var k = 0; k < count; k++ )
		{
			var frame = frames[k];
			var startTime = frame.StartTime;
			var endTime = frame.EndTime;

			if ( time > startTime && time < endTime )
			{
				if ( k < count - 1 )
				{
					var next = frames[k + 1];

					// Determine the blend length based on the current and next viseme
					if ( next.StartTime == endTime )
					{
						// No gap, increase the blend length to the end of the next viseme
						dt = MathF.Max( dt, MathF.Min( next.EndTime - time, endTime - startTime ) );
					}
					else
					{
						// Dead space, increase the blend length to the start of the next viseme
						dt = MathF.Max( dt, MathF.Min( next.StartTime - time, endTime - startTime ) );
					}
				}
				else
				{
					// Last viseme in list, increase the blend length to its own length
					dt = MathF.Max( dt, endTime - startTime );
				}
			}

			var t1 = (startTime - time) / dt;
			var t2 = (endTime - time) / dt;

			// Frames are sorted by start time and the blend window only grows while
			// we're inside a frame - so once a frame starts beyond the window, every
			// frame after it does too. Don't walk the rest of the track.
			if ( t1 >= 1.0f )
				break;

			// Check for overlap of the current time with the viseme duration
			if ( t1 < 1.0f && t2 > 0.0f )
			{
				t1 = MathF.Max( t1, 0 );
				t2 = MathF.Min( t2, 1 );

				var scale = t2 - t1;

				if ( frame.Viseme >= 0 && frame.Viseme < Visemes.Count && scale > 0.0f )
				{
					weights[frame.Viseme] = MathF.Max( weights[frame.Viseme], scale );
					any = true;
				}
			}
		}

		return any;
	}
}
