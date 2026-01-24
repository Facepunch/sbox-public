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
}

/// <summary>
/// Unused, will describe starting / stopping an action in the scene.
/// </summary>
/// <param name="TimeRange">Start and end time of this block.</param>
[Expose]
public sealed record CompiledActionBlock( MovieTimeRange TimeRange ) : ICompiledBlock
{
	// ReSharper disable once WithExpressionModifiesAllMembers
	public ICompiledBlock Shift( MovieTime offset ) => this with { TimeRange = TimeRange + offset };
}

/// <summary>
/// Interface for blocks describing a property changing value over time.
/// </summary>
public interface ICompiledPropertyBlock : ICompiledBlock, IPropertyBlock
{
	/// <inheritdoc cref="ICompiledBlock.Shift"/>
	new ICompiledPropertyBlock Shift( MovieTime offset );

	ICompiledBlock ICompiledBlock.Shift( MovieTime offset ) => Shift( offset );
}


/// <summary>
/// Interface for blocks describing a property changing value over time.
/// Typed version of <see cref="ICompiledPropertyBlock"/>.
/// </summary>
// ReSharper disable once TypeParameterCanBeVariant
public interface ICompiledPropertyBlock<T> : ICompiledPropertyBlock, IPropertyBlock<T>
{
	/// <inheritdoc cref="ICompiledBlock.Shift"/>
	new ICompiledPropertyBlock<T> Shift( MovieTime offset );

	ICompiledPropertyBlock ICompiledPropertyBlock.Shift( MovieTime offset ) => Shift( offset );
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

	public ICompiledPropertyBlock<T> Shift( MovieTime offset ) =>
		this with { TimeRange = TimeRange + offset };
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

	public ICompiledPropertyBlock<T> Shift( MovieTime offset ) =>
		this with { TimeRange = TimeRange + offset };

	private static ImmutableArray<T> Validate( ImmutableArray<T> samples )
	{
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
