using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;
using System.Linq;
using System.Text.Json.Serialization;

namespace Editor.MovieMaker;

#nullable enable

public abstract partial record PropertySignal : IPropertySignal
{
	[JsonIgnore]
	public abstract Type PropertyType { get; }

	protected PropertySignal( PropertySignal copy )
	{
		// Empty so any lazily computed fields aren't copied
	}

	object? IPropertySignal.GetValue( MovieTime time ) => OnGetValue( time );

	protected abstract object? OnGetValue( MovieTime time );

	/// <summary>
	/// Gets time ranges within the given <paramref name="timeRange"/> that have changing values.
	/// For painting in the timeline.
	/// </summary>
	public virtual IEnumerable<MovieTimeRange> GetPaintHints( MovieTimeRange timeRange ) => [timeRange];
}

/// <summary>
/// A <see cref="IPropertySignal{T}"/> that can be composed with <see cref="PropertyOperation{T}"/>s,
/// and stored in a <see cref="IPropertyBlock{T}"/>.
/// </summary>
public abstract partial record PropertySignal<T>() : PropertySignal, IPropertySignal<T>
{
	[JsonIgnore]
	public sealed override Type PropertyType => typeof(T);

	protected PropertySignal( PropertySignal<T> copy )
		: base( copy )
	{
		// Empty so any lazily computed fields aren't copied
	}

	public abstract T GetValue( MovieTime time );

	protected sealed override object? OnGetValue( MovieTime time ) => GetValue( time );

	/// <summary>
	/// Try to make a more minimal composition for this signal, optionally within a time range.
	/// </summary>
	/// <param name="start">Optional start time, we can discard any features before this if given.</param>
	/// <param name="end">Optional end time, we can discard any features after this if given.</param>
	public PropertySignal<T> Reduce( MovieTime? start = null, MovieTime? end = null )
	{
		return start >= end && !GetKeyframes( start.Value ).Any() ? GetValue( start.Value ) : OnReduce( start, end );
	}

	public PropertySignal<T> Reduce( MovieTimeRange timeRange ) =>
		Reduce( timeRange.Start, timeRange.End );

	protected abstract PropertySignal<T> OnReduce( MovieTime? start, MovieTime? end );

	public virtual IEnumerable<ICompiledPropertyBlock<T>> Compile( MovieTimeRange timeRange, int sampleRate )
	{
		var samples = Sample( timeRange, sampleRate );
		var sampleSpans = new List<SampleSpan>();

		FindConstantSpans( sampleSpans, samples );

		// If we have this many identical samples in a row, just make it a constant block.
		// Let's have a lower threshold for types that can't interpolate, like strings or ints

		var canInterpolate = Interpolator.GetDefault<T>() is not null;
		var minConstBlockSampleCount = canInterpolate
			? Math.Max( sampleRate / 2, 10 )
			: Math.Max( sampleRate / 4, 1 );

		// We take an extra sample at the end so we can interpolate smoothly to the next span

		var trailingExtraSamples = canInterpolate ? 1 : 0;

		MergeSpans( sampleSpans, minConstBlockSampleCount );

		foreach ( var span in sampleSpans )
		{
			var startTime = timeRange.Start + MovieTime.FromFrames( span.Start, sampleRate );
			var endTime = timeRange.Start + MovieTime.FromFrames( span.Start + span.Count, sampleRate );

			if ( span.IsConstant )
			{
				yield return new CompiledConstantBlock<T>( (startTime, endTime), samples[span.Start] );
				continue;
			}

			var spanSamples = samples.Skip( span.Start ).Take( span.Count + trailingExtraSamples );

			yield return new CompiledSampleBlock<T>( (startTime, endTime), 0d, sampleRate, [.. spanSamples] );
		}
	}

	private readonly record struct SampleSpan( int Start, int Count, bool IsConstant );

	/// <summary>
	/// Appends all the ranges of <paramref name="samples"/> that have a constant value to <paramref name="sampleSpans"/>.
	/// </summary>
	private static void FindConstantSpans( List<SampleSpan> sampleSpans, IReadOnlyList<T> samples )
	{
		var comparer = EqualityComparer<T>.Default;

		var currentSpanStart = 0;
		var prevSample = samples[0];

		for ( var i = 1; i < samples.Count; i++ )
		{
			var sample = samples[i];

			if ( comparer.Equals( prevSample, sample ) ) continue;

			sampleSpans.Add( new SampleSpan( currentSpanStart, i - currentSpanStart, true ) );

			currentSpanStart = i;
			prevSample = sample;
		}

		sampleSpans.Add( new SampleSpan( currentSpanStart, samples.Count - currentSpanStart, true ) );
	}

	/// <summary>
	/// Merge sample spans that are less than <paramref name="minConstSampleCount"/>, marking them as non-constant.
	/// </summary>
	private static void MergeSpans( List<SampleSpan> sampleSpans, int minConstSampleCount )
	{
		if ( minConstSampleCount < 2 ) return;

		for ( var i = sampleSpans.Count - 2; i >= 0; i-- )
		{
			var prev = sampleSpans[i];
			var next = sampleSpans[i + 1];

			if ( prev.IsConstant && prev.Count >= minConstSampleCount ) continue;
			if ( next.IsConstant && next.Count >= minConstSampleCount ) continue;

			sampleSpans.RemoveAt( i + 1 );
			sampleSpans[i] = new SampleSpan( prev.Start, prev.Count + next.Count, false );
		}
	}
}

/// <summary>
/// Extension methods for creating and composing <see cref="IPropertySignal"/>s.
/// </summary>
// ReSharper disable once UnusedMember.Global
public static partial class PropertySignalExtensions;
