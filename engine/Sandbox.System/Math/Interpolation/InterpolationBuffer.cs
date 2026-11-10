namespace Sandbox.Interpolation;

/// <summary>
/// Contains information in a buffer for interpolation.
/// </summary>
class InterpolationBuffer<T>
{
	internal struct Entry
	{
		public readonly double Time;
		public readonly T State;

		public Entry( T state, double time )
		{
			State = state;
			Time = time;
		}
	}

	private readonly List<Entry> _buffer = new();
	private readonly IInterpolator<T> _interpolator;

	public InterpolationBuffer( IInterpolator<T> interpolator )
	{
		_interpolator = interpolator;
	}

	/// <summary>
	/// Is the buffer currently empty?
	/// </summary>
	public bool IsEmpty => _buffer.Count == 0;

	/// <summary>
	/// How many entries are in the buffer?
	/// </summary>
	public int Count => _buffer.Count;

	/// <summary>
	/// The first entry in the buffer.
	/// </summary>
	public Entry First => _buffer[0];

	/// <summary>
	/// The last entry in the buffer.
	/// </summary>
	public Entry Last => _buffer[Count - 1];

	/// <summary>
	/// Query the interpolation buffer for a specific time.
	/// </summary>
	/// <param name="now">The time you want to query (usually now.)</param>
	/// <exception cref="InvalidOperationException">Throws if there are no snapshots in the interpolation buffer.</exception>
	public T Query( double now )
	{
		// A cull threshold of negative infinity never matches an entry, so nothing is removed.
		return QueryAndCull( now, double.NegativeInfinity );
	}

	/// <summary>
	/// Interpolates the buffer at <paramref name="now"/> and culls entries older than
	/// <paramref name="cullBefore"/> in a single pass. <see cref="Query"/> is the no-cull case.
	/// Assumes <paramref name="cullBefore"/> is not later than <paramref name="now"/>.
	/// </summary>
	public T QueryAndCull( double now, double cullBefore )
	{
		if ( IsEmpty ) throw new InvalidOperationException( "No snapshots in interpolation buffer!" );

		int count = _buffer.Count;
		int removeCount = 0;
		T result;

		if ( _buffer[0].Time > now )
		{
			// now precedes the buffer: clamp to first, nothing is old enough to cull.
			result = _buffer[0].State;
		}
		else if ( _buffer[count - 1].Time < now )
		{
			// now is past the buffer: clamp to last, then count the cullable prefix.
			result = _buffer[count - 1].State;
			while ( removeCount < count && _buffer[removeCount].Time < cullBefore ) removeCount++;
		}
		else
		{
			result = _buffer[count - 1].State;

			// Single pass: find the [i, i+1] bracket around now and count the leading
			// entries older than cullBefore in the same walk. Since cullBefore <= now, every
			// cullable entry is at or before the bracket.
			for ( int i = 0; i < count; i++ )
			{
				if ( _buffer[i].Time < cullBefore )
					removeCount++;

				if ( i < count - 1 && _buffer[i].Time <= now && now <= _buffer[i + 1].Time )
				{
					var delta = now.Remap( _buffer[i].Time, _buffer[i + 1].Time );
					result = _interpolator.Interpolate( _buffer[i].State, _buffer[i + 1].State, (float)delta );

					// Every entry old enough to cull is at or before the bracket, so we're done counting.
					break;
				}
			}
		}

		if ( removeCount > 0 )
			_buffer.RemoveRange( 0, removeCount );

		return result;
	}

	/// <summary>
	/// Add a new state to the buffer at the specified time.
	/// </summary>
	public void Add( T state, double time )
	{
		if ( !IsEmpty && time < Last.Time )
		{
			// This would cause the buffer to be out of order.
			return;
		}

		// Cull entries with this time or before.
		while ( _buffer.Count > 0 )
		{
			var lastEntry = _buffer.Last();

			if ( lastEntry.Time < time )
				break;

			_buffer.RemoveAt( _buffer.Count - 1 );
		}

		_buffer.Add( new Entry( state, time ) );
	}

	/// <summary>
	/// Clear the interpolation buffer.
	/// </summary>
	public void Clear()
	{
		_buffer.Clear();
	}

	/// <summary>
	/// Cull entries in the buffer older than the specified time.
	/// </summary>
	public void CullOlderThan( double oldTime )
	{
		// Entries are sorted by time, so we can just count and remove from the start
		// This avoids the Predicate<T> allocation from RemoveAll
		int removeCount = 0;
		for ( int i = 0; i < _buffer.Count; i++ )
		{
			if ( _buffer[i].Time >= oldTime )
				break;

			removeCount++;
		}

		if ( removeCount > 0 )
			_buffer.RemoveRange( 0, removeCount );
	}
}
