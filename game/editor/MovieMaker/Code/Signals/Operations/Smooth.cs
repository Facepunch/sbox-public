using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;
using System.Text.Json.Serialization;

namespace Editor.MovieMaker;

#nullable enable

partial record PropertySignal<T>
{
	public PropertySignal<T> Smooth( MovieTime size ) => size <= 0d || Interpolator.GetDefault<T>() is null ? this : OnSmooth( size );
	protected virtual PropertySignal<T> OnSmooth( MovieTime size ) => new SmoothOperation<T>( this, size );
}

[JsonDiscriminator( "Smooth" )]
[method: JsonConstructor]
file sealed record SmoothOperation<T>( PropertySignal<T> Signal, MovieTime Size ) : UnaryOperation<T>( Signal )
{
	// TODO: Vary this based on Size?
	private const int CacheSampleRate = 60;

	private readonly Dictionary<int, CompiledSampleBlock<T>> _cache = new();

	public SmoothOperation( SmoothOperation<T> copy )
		: base( copy )
	{
		// Copy constructor to avoid _cache getting copied

		Signal = copy.Signal;
		Size = copy.Size;

		_cache = new();
	}

	private MovieTime CacheBlockDuration => MovieTime.FromSeconds( 2d );
	private int GetCacheIndex( MovieTime time ) => time.GetFrameIndex( CacheBlockDuration );

	private CompiledSampleBlock<T> GetCachedBlock( MovieTime time )
	{
		var index = GetCacheIndex( time );

		if ( _cache.TryGetValue( index, out var cached ) )
		{
			return cached;
		}

		var start = index * CacheBlockDuration;
		var cachedTimeRange = new MovieTimeRange( start, start + CacheBlockDuration );
		var sourceTimeRange = new MovieTimeRange( cachedTimeRange.Start - Size, cachedTimeRange.End + Size );

		var sourceSampleCount = sourceTimeRange.Duration.GetFrameCount( CacheSampleRate );
		var samples = new T[sourceSampleCount];

		Signal.Sample( sourceTimeRange.Start, CacheSampleRate, samples );

		var smoothingPasses = Size.GetFrameCount( CacheSampleRate );
		var interpolator = Interpolator.GetDefaultOrThrow<T>();

		for ( var i = 0; i < smoothingPasses; ++i )
		{
			SmoothPass( samples, interpolator );
		}

		_cache[index] = cached = new CompiledSampleBlock<T>( cachedTimeRange, cachedTimeRange.Start - sourceTimeRange.Start, CacheSampleRate, [..samples] );

		return cached;
	}

	private static void SmoothPass( Span<T> samples, IInterpolator<T> interpolator )
	{
		if ( samples.Length < 2 ) return;

		var prev = samples[0];
		var curr = samples[0];

		for ( var i = 0; i < samples.Length; i++ )
		{
			var next = samples[Math.Min( i + 1, samples.Length - 1 )];

			var prevCurr = interpolator.Interpolate( prev, curr, 0.5f );
			var currNext = interpolator.Interpolate( curr, next, 0.5f );

			samples[i] = interpolator.Interpolate( prevCurr, currNext, 0.5f );

			prev = curr;
			curr = next;
		}
	}

	public override T GetValue( MovieTime time ) => GetCachedBlock( time ).GetValue( time );
}
