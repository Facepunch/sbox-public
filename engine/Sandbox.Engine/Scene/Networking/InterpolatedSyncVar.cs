using Sandbox.Interpolation;

namespace Sandbox;

internal interface IInterpolatedSyncVar
{
	public static readonly DelegateInterpolator<float> FloatInterpolator = new( ( a, b, delta ) => a.LerpTo( b, delta ) );

	/// <summary>
	/// Create a new interpolator for the type of the provided value.
	/// </summary>
	public static IInterpolator<T> Create<T>( T value )
	{
		var i = value switch
		{
			IInterpolator<T> interpolator => interpolator,
			float => (IInterpolator<T>)FloatInterpolator,
			_ => null
		};

		return i;
	}

	/// <summary>
	/// Query the interpolated value at the provided time.
	/// </summary>
	public object Query( double time );
}

/// <summary>
/// Contains a target value and the current interpolated value for the
/// property it represents.
/// </summary>
internal class InterpolatedSyncVar<T>( IInterpolator<T> interpolator ) : IInterpolatedSyncVar
{
	object IInterpolatedSyncVar.Query( double time ) => Query( time );

	private readonly InterpolationBuffer<T> _buffer = new( interpolator );

	/// <summary>
	/// Query the value at the specified time.
	/// </summary>
	private T Query( double time )
	{
		return _buffer.Query( time - Networking.InterpolationTime );
	}

	bool _lastUntimed;

	/// <summary>
	/// Update the value with the latest value from the network.
	/// </summary>
	public void Update( T value )
	{
		// When the sender took it if this came in a snapshot
		var timed = !double.IsNaN( Network.NetworkTable.ReadTime );
		var time = timed ? Network.NetworkTable.ReadTime : Time.NowDouble;
		var interval = 1.0 / ProjectSettings.Networking.UpdateRate.Clamp( 1, 500 );

		// Values from creation or a refresh sit on our receive time, which can be ahead of the sender's timeline
		if ( timed && _lastUntimed && !_buffer.IsEmpty && time < _buffer.Last.Time )
			_buffer.Clear();

		_lastUntimed = !timed;

		// After a pause, hold the old value until one update before this one instead of jumping to it
		if ( !_buffer.IsEmpty && time - _buffer.Last.Time > interval * 2 + 0.1 )
			_buffer.Add( _buffer.Last.State, time - interval );

		_buffer.Add( value, time );
		_buffer.CullOlderThan( Time.NowDouble - (Networking.InterpolationTime * 3f) );
	}
}
