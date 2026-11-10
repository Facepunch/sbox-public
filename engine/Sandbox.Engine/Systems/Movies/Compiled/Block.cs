using System.Collections;
using System.Collections.Immutable;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox.MovieMaker.Compiled;

#nullable enable

/// <summary>
/// A block of time where something happens in an <see cref="ICompiledTrack"/>.
/// </summary>
public interface ICompiledBlock : ITrackBlock
{
	/// <summary>
	/// Move this block by the given time <paramref name="offset"/>.
	/// </summary>
	ICompiledBlock Shift( MovieTime offset );

	/// <summary>
	/// Trim this block down to the given <paramref name="range"/>.
	/// </summary>
	/// <param name="range">Time range to clamp to.</param>
	ICompiledBlock Clamp( MovieTimeRange range );
}

/// <summary>
/// Unused, will describe starting / stopping an action in the scene.
/// </summary>
/// <param name="TimeRange">Start and end time of this block.</param>
[Expose]
public sealed record CompiledActionBlock( MovieTimeRange TimeRange ) : ICompiledBlock
{
	// ReSharper disable WithExpressionModifiesAllMembers
	public ICompiledBlock Shift( MovieTime offset ) => this with { TimeRange = TimeRange + offset };
	public ICompiledBlock Clamp( MovieTimeRange range ) => this with { TimeRange = TimeRange.Clamp( range ) };
	// ReSharper restore WithExpressionModifiesAllMembers
}

/// <summary>
/// Interface for blocks describing a property changing value over time.
/// </summary>
public interface ICompiledPropertyBlock : ICompiledBlock, IPropertyBlock
{
	/// <inheritdoc cref="ICompiledBlock.Shift"/>
	new ICompiledPropertyBlock Shift( MovieTime offset );

	/// <inheritdoc cref="ICompiledBlock.Clamp"/>
	new ICompiledPropertyBlock Clamp( MovieTimeRange timeRange );

	ICompiledBlock ICompiledBlock.Shift( MovieTime offset ) => Shift( offset );
	ICompiledBlock ICompiledBlock.Clamp( MovieTimeRange range ) => Clamp( range );
}

/// <summary>
/// Interface for blocks describing a property changing value over time.
/// Typed version of <see cref="ICompiledPropertyBlock"/>.
/// </summary>
// ReSharper disable once TypeParameterCanBeVariant
public partial interface ICompiledPropertyBlock<T> : ICompiledPropertyBlock, IPropertyBlock<T>
{
	/// <inheritdoc cref="ICompiledBlock.Shift"/>
	new ICompiledPropertyBlock<T> Shift( MovieTime offset );

	/// <inheritdoc cref="ICompiledBlock.Clamp"/>
	new ICompiledPropertyBlock<T> Clamp( MovieTimeRange range );

	ICompiledPropertyBlock ICompiledPropertyBlock.Shift( MovieTime offset ) => Shift( offset );
	ICompiledPropertyBlock ICompiledPropertyBlock.Clamp( MovieTimeRange range ) => Clamp( range );
}

/// <summary>
/// This block has a single constant value for the whole duration.
/// Useful for value types that can't be interpolated, and change infrequently.
/// </summary>
public interface ICompiledConstantBlock : ICompiledPropertyBlock
{
	/// <summary>
	/// Json-serialized constant value.
	/// </summary>
	JsonNode? Serialized { get; }
}

/// <summary>
/// This block has a single constant value for the whole duration.
/// Useful for value types that can't be interpolated, and change infrequently.
/// </summary>
/// <typeparam name="T">Property value type.</typeparam>
/// <param name="TimeRange">Start and end time of this block.</param>
/// <param name="Serialized">Json-serialized constant value.</param>
[Expose]
[method: JsonConstructor]
public sealed record CompiledConstantBlock<T>( MovieTimeRange TimeRange, JsonNode? Serialized ) : ICompiledPropertyBlock<T>, ICompiledConstantBlock
{
	private JsonNode? _serialized = Serialized;

	private T? _value;
	private bool _deserialized;

	[JsonPropertyName( "Value" )]
	public JsonNode? Serialized
	{
		get => _serialized;
		init
		{
			_serialized = value;
			_value = default;
			_deserialized = false;
		}
	}

	public CompiledConstantBlock( MovieTimeRange timeRange, T value )
		: this( timeRange, Json.ToNode( value ) )
	{
		_value = value;
		_deserialized = true;
	}

	public T GetValue( MovieTime time )
	{
		if ( _deserialized ) return _value!;

		_value = Json.FromNode<T>( Serialized );
		_deserialized = true;

		return _value;
	}

	/// <inheritdoc cref="ICompiledBlock.Shift"/>
	public ICompiledPropertyBlock<T> Shift( MovieTime offset ) =>
		offset == MovieTime.Zero ? this : this with { TimeRange = TimeRange + offset };

	/// <inheritdoc cref="ICompiledBlock.Clamp"/>
	public ICompiledPropertyBlock<T> Clamp( MovieTimeRange range ) =>
		range.Contains( TimeRange ) ? this : this with { TimeRange = TimeRange.Clamp( range ) };
}

/// <summary>
/// This block contains an array of values sampled at uniform intervals.
/// </summary>
public interface ICompiledSampleBlock : ICompiledPropertyBlock
{
	/// <summary>
	/// Time offset of the first sample.
	/// </summary>
	MovieTime Offset { get; }

	/// <summary>
	/// How many samples per second.
	/// </summary>
	int SampleRate { get; }

	/// <summary>
	/// Raw sample values.
	/// </summary>
	IReadOnlyList<object?> Samples { get; }
}

/// <summary>
/// This block contains an array of values sampled at uniform intervals.
/// </summary>
/// <typeparam name="T">Property value type.</typeparam>
/// <param name="TimeRange">Start and end time of this block.</param>
/// <param name="Offset">Time offset of the first sample.</param>
/// <param name="SampleRate">How many samples per second.</param>
/// <param name="Samples">Raw sample values.</param>
[Expose]
public sealed partial record CompiledSampleBlock<T>( MovieTimeRange TimeRange, MovieTime Offset, int SampleRate, ImmutableArray<T> Samples ) : ICompiledPropertyBlock<T>, ICompiledSampleBlock
{
	private readonly ImmutableArray<T> _samples = Validate( Samples );
	private IReadOnlyList<object?>? _wrappedSamples;

	public ImmutableArray<T> Samples
	{
		get => _samples;
		init
		{
			_samples = Validate( value );
			_wrappedSamples = null;
		}
	}

	public T GetValue( MovieTime time ) =>
		Samples.Sample( time.Clamp( TimeRange ) - TimeRange.Start + Offset, SampleRate, _interpolator );

	/// <inheritdoc cref="ICompiledBlock.Shift"/>
	public ICompiledPropertyBlock<T> Shift( MovieTime offset ) =>
		offset == MovieTime.Zero ? this : this with { TimeRange = TimeRange + offset };

	/// <inheritdoc cref="ICompiledBlock.Clamp"/>
	public ICompiledPropertyBlock<T> Clamp( MovieTimeRange range )
	{
		range = TimeRange.Clamp( range );

		if ( range == TimeRange )
		{
			return this;
		}

		var clamped = this with
		{
			TimeRange = range,
			Offset = Offset + range.Start - TimeRange.Start
		};

		return clamped.Reduce();
	}

	/// <summary>
	/// Returns a property block with only sample data within <see cref="TimeRange"/>.
	/// Returns the current instance if it represents an irreducible block.
	/// If only one sample is needed, will return a <see cref="CompiledConstantBlock{T}"/>.
	/// </summary>
	public ICompiledPropertyBlock<T> Reduce()
	{
		// Time range relative to the first sample

		MovieTimeRange localRange = (Offset, Offset + TimeRange.Duration);

		var firstSampleIndex = Math.Clamp( localRange.Start.GetFrameIndex( SampleRate ), 0, _samples.Length - 1 );
		var newOffset = localRange.Start - MovieTime.FromFrames( firstSampleIndex, SampleRate );

		var sampleDuration = localRange.Duration + newOffset;

		// We include the next sample after the end of the time range so we can interpolate to it

		var sampleCount = sampleDuration.IsPositive
			? Math.Min( sampleDuration.GetFrameCount( SampleRate ) + 1, _samples.Length - firstSampleIndex )
			: 1;

		if ( sampleCount <= 1 )
		{
			return new CompiledConstantBlock<T>( TimeRange, _samples[firstSampleIndex] );
		}

		if ( firstSampleIndex == 0 && sampleCount == _samples.Length )
		{
			return this;
		}

		return this with
		{
			Samples = _samples.Slice( firstSampleIndex, sampleCount ),
			Offset = newOffset
		};
	}

	private static ImmutableArray<T> Validate( ImmutableArray<T> samples )
	{
		if ( typeof( T ).IsAssignableTo( typeof( Resource ) ) )
		{
			throw new ArgumentException( "Invalid sample value type.", nameof( T ) );
		}

		if ( samples.IsDefaultOrEmpty )
		{
			throw new ArgumentException( "Expected at least one sample.", nameof( Samples ) );
		}

		return samples;
	}

	IReadOnlyList<object?> ICompiledSampleBlock.Samples => _wrappedSamples ??= new ReadOnlyListWrapper<T>( _samples );

#pragma warning disable SB3000
	private static readonly IInterpolator<T>? _interpolator = Interpolator.GetDefault<T>();
#pragma warning restore SB3000
}

file sealed class ReadOnlyListWrapper<T>( ImmutableArray<T> array ) : IReadOnlyList<object?>
{
	public IEnumerator<object?> GetEnumerator() => array.Cast<object?>().GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public int Count => array.Length;

	public object? this[int index] => array[index];
}
