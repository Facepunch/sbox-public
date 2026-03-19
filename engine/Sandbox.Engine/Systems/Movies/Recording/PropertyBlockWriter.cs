using Sandbox.MovieMaker.Compiled;

namespace Sandbox.MovieMaker;

#nullable enable

internal class PropertyBlockWriter<T>( int sampleRate ) : IPropertyBlock<T>, IDynamicBlock
{
	private readonly List<T> _samples = new();
	private T _defaultValue = default!;

	public bool IsEmpty => _samples.Count == 0;
	public bool IsConstant { get; private set; }

	public MovieTime StartTime { get; set; }

	public MovieTimeRange TimeRange => (StartTime, StartTime + MovieTime.FromFrames( _samples.Count, sampleRate ));

	public event Action<MovieTimeRange>? Changed;

	public void Clear()
	{
		_samples.Clear();
	}

	public void Write( T value )
	{
		if ( _samples.Count == 0 )
		{
			IsConstant = true;
		}
		else if ( IsConstant )
		{
			IsConstant = Comparer.Equals( _samples[0], value );
		}

		_samples.Add( value );
		_defaultValue = value;

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

		if ( IsConstant ) return new CompiledConstantBlock<T>( timeRange, _samples[0] );

		return new CompiledSampleBlock<T>( timeRange, StartTime - timeRange.Start, sampleRate, [.. _samples] );
	}

	public IEnumerable<MovieTimeRange> GetPaintHints( MovieTimeRange timeRange ) => [TimeRange];

	public T GetValue( MovieTime time )
	{
		return _samples.Count != 0
			? _samples.Sample( time - StartTime, sampleRate, Interpolator )
			: _defaultValue;
	}

	private static IInterpolator<T>? Interpolator { get; } = MovieMaker.Interpolator.GetDefault<T>();
	private static EqualityComparer<T> Comparer { get; } = EqualityComparer<T>.Default;
}
