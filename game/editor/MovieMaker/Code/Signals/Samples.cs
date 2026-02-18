using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;

namespace Editor.MovieMaker;

#nullable enable

partial record PropertySignal<T>
{
	public IReadOnlyList<T> Sample( MovieTimeRange timeRange, int sampleRate )
	{
		var sampleCount = timeRange.Duration.GetFrameCount( sampleRate );
		var samples = new T[sampleCount];

		Sample( timeRange.Start, sampleRate, samples.AsSpan() );

		return samples;
	}

	public void Sample( MovieTime startTime, int sampleRate, Span<T> dst )
	{
		OnSample( startTime, sampleRate, dst );
	}

	protected virtual void OnSample( MovieTime startTime, int sampleRate, Span<T> dst )
	{
		for ( var i = 0; i < dst.Length; i++ )
		{
			dst[i] = GetValue( startTime + MovieTime.FromFrames( i, sampleRate ) );
		}
	}

	public static PropertySignal<T> FromSamples( MovieTime startTime, int sampleRate, IEnumerable<T> samples )
	{
		ImmutableArray<T> sampleArray = [..samples];

		var duration = MovieTime.FromFrames( sampleArray.Length, sampleRate );

		return new SamplesSignal<T>( (startTime, startTime + duration), default, sampleRate, sampleArray );
	}

	public static implicit operator PropertySignal<T>( CompiledSampleBlock<T> sampleBlock )
	{
		return new SamplesSignal<T>( sampleBlock.TimeRange, sampleBlock.Offset, sampleBlock.SampleRate, sampleBlock.Samples );
	}
}

partial record PropertyBlock<T>
{
	public static implicit operator PropertyBlock<T>( CompiledSampleBlock<T> sampleBlock ) =>
		new( new SamplesSignal<T>( sampleBlock.TimeRange, sampleBlock.Offset, sampleBlock.SampleRate, sampleBlock.Samples ), sampleBlock.TimeRange );
}

[JsonDiscriminator( "Samples" )]
[JsonConverter( typeof( SamplesSignalConverterFactory ) )]
[method: JsonConstructor]
file sealed record SamplesSignal<T>(
	MovieTimeRange TimeRange,
	MovieTime Offset,
	int SampleRate,
	ImmutableArray<T> Samples ) : PropertySignal<T>
{
	public override T GetValue( MovieTime time ) =>
		Samples.Sample( time.Clamp( TimeRange ) - TimeRange.Start + Offset, SampleRate, _interpolator );

	protected override PropertySignal<T> OnReduce( MovieTime? start, MovieTime? end )
	{
		// TODO: clip samples

		return this;
	}

	protected override void OnSample( MovieTime startTime, int sampleRate, Span<T> dst )
	{
		if ( SampleRate == sampleRate && Offset == default )
		{
			// Fast path for just copying existing samples

			// TODO: handle srcStartIndex < 0 or srcEndIndex > Samples.Length
			// TODO: generalize for remainder aligning with Offset

			var srcStartIndex = (startTime - TimeRange.Start).GetFrameIndex( sampleRate, out var remainder );
			var srcEndIndex = srcStartIndex + dst.Length;

			if ( srcStartIndex >= 0 && srcEndIndex <= Samples.Length && remainder == default )
			{
				Samples.AsSpan( srcStartIndex, srcEndIndex - srcStartIndex ).CopyTo( dst );
				return;
			}
		}

		base.OnSample( startTime, sampleRate, dst );
	}

	public override IEnumerable<ICompiledPropertyBlock<T>> Compile( MovieTimeRange timeRange, int sampleRate )
	{
		// TODO: fast-path if samples align

		return base.Compile( timeRange, sampleRate );
	}

	public override IEnumerable<MovieTimeRange> GetPaintHints( MovieTimeRange timeRange ) =>
		[timeRange.Clamp( TimeRange )];

	[SkipHotload] private static readonly IInterpolator<T>? _interpolator = Interpolator.GetDefault<T>();
}

file sealed class SamplesSignalConverterFactory : JsonConverterFactory
{
	public override bool CanConvert( Type typeToConvert ) =>
		typeToConvert.IsConstructedGenericType && typeToConvert.GetGenericTypeDefinition() == typeof( SamplesSignal<> );

	public override JsonConverter? CreateConverter( Type typeToConvert, JsonSerializerOptions options )
	{
		if ( !typeToConvert.IsConstructedGenericType ) return null;
		if ( typeToConvert.GetGenericTypeDefinition() != typeof( SamplesSignal<> ) ) return null;

		var valueType = typeToConvert.GetGenericArguments()[0];
		var converterType = typeof( SamplesSignalConverter<> ).MakeGenericType( valueType );

		return (JsonConverter)Activator.CreateInstance( converterType )!;
	}
}

file sealed class SamplesSignalConverter<T> : JsonConverter<SamplesSignal<T>>
{
	public override SamplesSignal<T>? Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		var compiled = JsonSerializer.Deserialize<CompiledSampleBlock<T>>( ref reader, options );

		return compiled is not null
			? new SamplesSignal<T>( compiled.TimeRange, compiled.Offset, compiled.SampleRate, compiled.Samples )
			: null;
	}

	public override void Write( Utf8JsonWriter writer, SamplesSignal<T> value, JsonSerializerOptions options )
	{
		JsonSerializer.Serialize( writer, new CompiledSampleBlock<T>( value.TimeRange, value.Offset, value.SampleRate, value.Samples ), options );
	}
}
