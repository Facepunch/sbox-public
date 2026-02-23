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
		if ( IsEmpty )
			throw new InvalidOperationException( "No snapshots in interpolation buffer!" );

		if ( _buffer.Count == 1 ) return First.State;
		if ( First.Time > now ) return First.State;
		if ( Last.Time < now ) return Last.State;

		for ( var i = 0; i < _buffer.Count - 1; i++ )
		{
			var from = _buffer[i];
			var to = _buffer[i + 1];
			var fromTime = _buffer[i].Time;
			var toTime = _buffer[i + 1].Time;

			if ( fromTime <= now && now <= toTime )
			{
				var delta = now.Remap( fromTime, toTime );
				return _interpolator.Interpolate( from.State, to.State, (float)delta );
			}
		}

		return Last.State;
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
