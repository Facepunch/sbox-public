using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;

namespace Editor.MovieMaker;

#nullable enable

partial record PropertySignal<T>
{
	public static implicit operator PropertySignal<T>( T value ) => new ConstantSignal<T>( value );
	public static implicit operator PropertySignal<T>( CompiledConstantBlock<T> block ) => new ConstantSignal<T>( block.Serialized );
}

partial record PropertyBlock<T>
{
	public static implicit operator PropertyBlock<T>( CompiledConstantBlock<T> block ) =>
		new ( new ConstantSignal<T>( block.Serialized ), block.TimeRange );
}

[JsonDiscriminator( "Constant" )]
[method: JsonConstructor]
file sealed record ConstantSignal<T>( JsonNode? Serialized ) : PropertySignal<T>
{
	private JsonNode? _serialized = Serialized;

	private T? _value;
	private bool _deserialized;
	private bool _isIdentity;

	[JsonPropertyName( "Value" )]
	public JsonNode? Serialized
	{
		get => _serialized;
		init
		{
			_serialized = value;
			_value = default;
			_isIdentity = false;
			_deserialized = false;
		}
	}

	public ConstantSignal( T value )
		: this( Json.ToNode( value ) )
	{
		_value = value;
		_isIdentity = Transformer.GetDefault<T>() is { } transformer && EqualityComparer<T>.Default.Equals( _value, transformer.Identity );
		_deserialized = true;
	}

	private void EnsureDeserialized()
	{
		// Defer deserializing because some types (Sandbox.Model)
		// can't be deserialized off the main thread

		if ( _deserialized ) return;

		_value = Json.FromNode<T>( Serialized );
		_isIdentity = Transformer.GetDefault<T>() is { } transformer && EqualityComparer<T>.Default.Equals( _value, transformer.Identity );
		_deserialized = true;
	}

	public override T GetValue( MovieTime time )
	{
		EnsureDeserialized();
		return _value!;
	}

	[JsonIgnore]
	public override bool IsIdentity
	{
		get
		{
			EnsureDeserialized();
			return _isIdentity;
		}
	}

	protected override PropertySignal<T> OnTransform( MovieTransform value ) => this;
	protected override PropertySignal<T> OnReduce( MovieTime? start, MovieTime? end ) => this;

	public override IEnumerable<ICompiledPropertyBlock<T>> Compile( MovieTimeRange timeRange, int sampleRate ) =>
		[new CompiledConstantBlock<T>( timeRange, Serialized )];

	public override IEnumerable<MovieTimeRange> GetPaintHints( MovieTimeRange timeRange ) => [timeRange.Start, timeRange.End - MovieTime.Epsilon];
}

partial class PropertySignalExtensions
{
	/// <summary>
	/// Creates a constant signal with the given value.
	/// </summary>
	public static PropertySignal<T> AsSignal<T>( this T value ) => value;

	public static IPropertySignal AsSignal( this object? value, Type targetType )
	{
		var signalType = typeof(ConstantSignal<>).MakeGenericType( targetType );

		return (IPropertySignal)Activator.CreateInstance( signalType, value )!;
	}

	/// <inheritdoc cref="AsSignal{T}(IReadOnlyList{PropertyBlock{T}})"/>
	public static PropertySignal<T>? AsSignal<T>( this IEnumerable<PropertyBlock<T>> blocks ) =>
		blocks.ToImmutableArray().AsSignal<T>();

	/// <summary>
	/// Creates a signal that joins together the given blocks.
	/// </summary>
	public static PropertySignal<T>? AsSignal<T>( this IReadOnlyList<PropertyBlock<T>> blocks )
	{
		if ( blocks.Count == 0 ) return null;

		// TODO: balance?

		var signal = blocks[0].Signal.Clamp( blocks[0].TimeRange );

		foreach ( var block in blocks.Skip( 1 ) )
		{
			signal = signal.HardCut( block.Signal.ClampEnd( block.TimeRange.End ), block.TimeRange.Start );
		}

		return signal;
	}
}
