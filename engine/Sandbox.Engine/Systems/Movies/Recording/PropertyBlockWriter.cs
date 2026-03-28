using Sandbox.MovieMaker.Compiled;

namespace Sandbox.MovieMaker;

#nullable enable

internal class PropertyBlockWriter<T>( int sampleRate ) : IPropertyBlock<T>, IDynamicBlock
{
	// Special handling for properties that never change (constants):
	// only actually start putting values in the _samples list if we
	// see a change. Otherwise we just remember _lastValue, and _constantSampleCount.

	// As soon as we see a change, fill up _samples with copies of _lastValue, set
	// _constantSampleCount to zero, and start always putting new values in _samples.

	// We'll only ever have either _constantSampleCount or _samples.Count be > 0, never both.

	private T _lastValue = default!;

	private List<T>? _samples;
	private int _constantSampleCount = 0;

	public bool IsEmpty => _samples?.Count is not > 0 && _constantSampleCount == 0;
	public bool IsConstant => _constantSampleCount > 0;

	public MovieTime StartTime { get; set; }

	public MovieTimeRange TimeRange => (StartTime, StartTime + MovieTime.FromFrames( (_samples?.Count ?? 0) + _constantSampleCount, sampleRate ));

	public event Action<MovieTimeRange>? Changed;

	public void Clear()
	{
		_samples?.Clear();
		_constantSampleCount = 0;
	}

	public void Write( T value )
	{
		if ( IsEmpty || IsConstant && Comparer.Equals( _lastValue, value ) )
		{
			_constantSampleCount += 1;
		}
		else if ( IsConstant )
		{
			_samples ??= [];
			_samples.EnsureCapacity( _constantSampleCount + 1 );

			for ( var i = 0; i < _constantSampleCount; i++ )
			{
				_samples.Add( _lastValue );
			}

			_constantSampleCount = 0;
		}

		_lastValue = value;

		if ( !IsConstant )
		{
			_samples!.Add( value );
		}

		var time = TimeRange.End;
		var samplePeriod = MovieTime.FromFrames( 1, sampleRate );

		Changed?.Invoke( (time - samplePeriod, time) );
	}

	/// <summary>
	/// Compiles the samples written by this writer to a block, clamped to the given <paramref name="timeRange"/>.
	/// </summary>
	public ICompiledPropertyBlock<T> Compile( MovieTimeRange timeRange )
	{
		if ( IsEmpty ) throw new InvalidOperationException( "Block is empty!" );

		timeRange = timeRange.Clamp( TimeRange );

		if ( IsConstant ) return new CompiledConstantBlock<T>( timeRange, _lastValue );

		return new CompiledSampleBlock<T>( timeRange, StartTime - timeRange.Start, sampleRate, [.. _samples!] );
	}

	public IEnumerable<MovieTimeRange> GetPaintHints( MovieTimeRange timeRange ) => [TimeRange];

	public T GetValue( MovieTime time )
	{
		return _samples?.Count is > 0
			? _samples.Sample( time - StartTime, sampleRate, Interpolator )
			: _lastValue;
	}

	private static IInterpolator<T>? Interpolator { get; } = MovieMaker.Interpolator.GetDefault<T>();
	private static EqualityComparer<T> Comparer { get; } = EqualityComparer<T>.Default;
}
