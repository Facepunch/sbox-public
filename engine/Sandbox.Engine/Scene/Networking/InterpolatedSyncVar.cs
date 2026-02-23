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

	/// <summary>
	/// Update the value with the latest value from the network.
	/// </summary>
	public void Update( T value )
	{
		_buffer.Add( value, Time.NowDouble );
		_buffer.CullOlderThan( Time.NowDouble - (Networking.InterpolationTime * 3f) );
	}
}
