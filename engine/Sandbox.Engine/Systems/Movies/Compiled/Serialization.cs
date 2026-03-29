using Sandbox.MovieMaker.Properties;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox.MovieMaker.Compiled;

#nullable enable

[JsonConverter( typeof( ClipConverter ) )]
partial class MovieClip
{
	public IMovieResource ToResource() => new EmbeddedMovieResource { Compiled = this };

	internal ImmutableArray<Package> ResolvePrimaryPackages()
	{
		var packages = new HashSet<Package>();

		foreach ( var track in Tracks.OfType<ICompiledPropertyTrack>() )
		{
			foreach ( var block in track.Blocks.OfType<ICompiledConstantBlock>() )
			{
				if ( block.Serialized is not { } node ) continue;

				foreach ( var package in Cloud.ResolvePrimaryAssetsFromJson( node ) )
				{
					packages.Add( package );
				}
			}
		}

		return [.. packages];
	}
}

file sealed class ClipConverter : JsonConverter<MovieClip>
{
	public override MovieClip Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		return JsonSerializer.Deserialize<ClipModel>( ref reader, options )!.Deserialize( options );
	}

	public override void Write( Utf8JsonWriter writer, MovieClip value, JsonSerializerOptions options )
	{
		var childDict = value.Tracks
			.Where( x => x.Parent is not null )
			.GroupBy( x => x.Parent! )
			.ToImmutableDictionary( x => x.Key, x => x.ToImmutableArray() );

		JsonSerializer.Serialize( writer, new ClipModel( value, childDict, options ), options );
	}
}

[method: JsonConstructor]
file sealed record ClipModel(
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	ImmutableArray<TrackModel>? Tracks )
{
	public ClipModel( MovieClip clip, ImmutableDictionary<ICompiledTrack, ImmutableArray<ICompiledTrack>> childDict, JsonSerializerOptions? options )
		: this( clip.Tracks is { Length: > 0 }
			? clip.Tracks.Where( x => x.Parent is null ).Select( x => new TrackModel( x, childDict, options ) ).ToImmutableArray()
			: null )
	{

	}

	public MovieClip Deserialize( JsonSerializerOptions? options )
	{
		return Tracks is { Length: > 0 } rootTracks
			? MovieClip.FromTracks( TrackModel.Deserialize( rootTracks, options ) )
			: MovieClip.Empty;
	}
}

file enum TrackKind
{
	Reference,
	Action,
	Property,
	ReferenceProperty
}

[method: JsonConstructor]
file sealed record TrackModel( TrackKind Kind, string Name, Type Type,
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] Guid? Id,
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] TrackMetadata? Metadata,
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] ImmutableArray<TrackModel>? Children,
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] JsonArray? Blocks )
{
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWriting ), JsonPropertyName( "ReferenceId" )]
	public Guid? LegacyReferenceId
	{
		get => null;
		init
		{
			if ( value is null ) return;

			Metadata = new TrackMetadata( ReferenceId: value );
		}
	}

	public TrackModel( ICompiledTrack track, ImmutableDictionary<ICompiledTrack, ImmutableArray<ICompiledTrack>> childDict, JsonSerializerOptions? options )
		: this(
			Kind: GetKind( track ),
			Name: track.Name,
			Type: GetTypeForSerialization( track ),
			Id: (track as IReferenceTrack)?.Id,
			Metadata: (track as IReferenceTrack)?.Metadata,
			Children: GetChildTrackModels( track, childDict, options ),
			Blocks: GetBlockModels( track, options ) )
	{

	}

	private static ImmutableArray<TrackModel>? GetChildTrackModels(
		ICompiledTrack track,
		ImmutableDictionary<ICompiledTrack, ImmutableArray<ICompiledTrack>> childDict,
		JsonSerializerOptions? options )
	{
		if ( !childDict.TryGetValue( track, out var children ) )
		{
			return null;
		}

		return
		[
			..children.Select( x => new TrackModel( x, childDict, options ) )
		];
	}

	private static JsonArray? GetBlockModels(
		ICompiledTrack track,
		JsonSerializerOptions? options )
	{
		if ( track is not ICompiledPropertyTrack { Blocks.Count: > 0 } blockTrack )
		{
			return null;
		}

		try
		{
			var blockType = typeof( ICompiledPropertyBlock<> ).MakeGenericType( track.TargetType );
			var listType = typeof( IReadOnlyList<> ).MakeGenericType( blockType );

			return JsonSerializer.SerializeToNode( blockTrack.Blocks, listType, options )?.AsArray();
		}
		catch ( Exception ex )
		{
			// Recover from a serialization exception so that the rest of the movie survives

			Log.Error( ex, $"Exception when serializing blocks for track \"{track.GetPathString()}\"." );

			return null;
		}
	}

	public static IReadOnlyList<ICompiledTrack> Deserialize( IEnumerable<TrackModel> models, JsonSerializerOptions? options )
	{
		return models.SelectMany( x => x.Deserialize( null, options ) ).ToArray();
	}

	private IEnumerable<ICompiledTrack> Deserialize( ICompiledTrack? parent, JsonSerializerOptions? options )
	{
		if ( (Type?)Type is null ) return [];

		var track = Kind switch
		{
			TrackKind.Reference => DeserializeReferenceTrack( parent, options ),
			TrackKind.Action => new CompiledActionTrack( Name, Type, parent!, ImmutableArray<CompiledActionBlock>.Empty ),
			TrackKind.Property => DeserializeHelper.Get( Type ).DeserializePropertyTrack( this, parent!, options ),
			TrackKind.ReferenceProperty => DeserializeHelper.GetReference( Type ).DeserializePropertyTrack( this, parent!, options ),
			_ => throw new NotImplementedException()
		};

		return Children is { IsDefaultOrEmpty: false } children
			? [track, .. children.SelectMany( x => x.Deserialize( track, options ) )]
			: [track];
	}

	private static TrackKind GetKind( ICompiledTrack track )
	{
		return track switch
		{
			IReferenceTrack => TrackKind.Reference,
			IActionTrack => TrackKind.Action,
			IPropertyTrack when BindingReference.GetUnderlyingType( track.TargetType ) is not null => TrackKind.ReferenceProperty,
			IPropertyTrack => TrackKind.Property,
			_ => throw new NotImplementedException()
		};
	}

	private static Type GetTypeForSerialization( ICompiledTrack track )
	{
		if ( track is IPropertyTrack )
		{
			return BindingReference.GetUnderlyingType( track.TargetType ) ?? track.TargetType;
		}

		return track.TargetType;
	}

	private ICompiledReferenceTrack DeserializeReferenceTrack( ICompiledTrack? parent, JsonSerializerOptions? options )
	{
		var trackType = typeof( CompiledReferenceTrack<> )
			.MakeGenericType( Type );

		return (ICompiledReferenceTrack)Activator.CreateInstance( trackType,
			Id ?? Guid.NewGuid(),
			Name,
			(CompiledReferenceTrack<GameObject>?)parent,
			Metadata )!;
	}
}

file abstract class DeserializeHelper
{
	[SkipHotload]
	private static Dictionary<Type, DeserializeHelper> Cache { get; } = new();

	[SkipHotload]
	private static Dictionary<Type, DeserializeHelper> ReferenceCache { get; } = new();

	public static DeserializeHelper Get( Type type )
	{
		if ( Cache.TryGetValue( type, out var cached ) ) return cached;

		var helperType = typeof( DeserializeHelper<> )
			.MakeGenericType( type );

		return Cache[type] = (DeserializeHelper)Activator.CreateInstance( helperType )!;
	}

	public static DeserializeHelper GetReference( Type type )
	{
		if ( ReferenceCache.TryGetValue( type, out var cached ) ) return cached;

		var referenceType = typeof( BindingReference<> ).MakeGenericType( type );

		return ReferenceCache[type] = Get( referenceType );
	}

	public abstract ICompiledTrack DeserializePropertyTrack( TrackModel model, ICompiledTrack parent, JsonSerializerOptions? options );
}

file sealed class DeserializeHelper<T> : DeserializeHelper
{
	public override ICompiledTrack DeserializePropertyTrack( TrackModel model, ICompiledTrack parent, JsonSerializerOptions? options )
	{
		return new CompiledPropertyTrack<T>( model.Name, parent,
			model.Blocks?.Deserialize<ImmutableArray<ICompiledPropertyBlock<T>>>( options )
			?? ImmutableArray<ICompiledPropertyBlock<T>>.Empty );
	}
}

[JsonConverter( typeof( CompiledPropertyBlockConverterFactory ) )]
partial interface ICompiledPropertyBlock<T>;

file sealed class CompiledPropertyBlockConverterFactory : JsonConverterFactory
{
	public override bool CanConvert( Type typeToConvert ) =>
		typeToConvert.IsConstructedGenericType && typeToConvert.GetGenericTypeDefinition() == typeof( ICompiledPropertyBlock<> );

	public override JsonConverter CreateConverter( Type typeToConvert, JsonSerializerOptions options )
	{
		var valueType = typeToConvert.GetGenericArguments()[0];

		var converterType = typeof( CompiledPropertyBlockConverter<> )
			.MakeGenericType( valueType );

		return (JsonConverter)Activator.CreateInstance( converterType )!;
	}
}

file sealed class CompiledPropertyBlockConverter<T> : JsonConverter<ICompiledPropertyBlock<T>>
{
	public override ICompiledPropertyBlock<T>? Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		var obj = JsonSerializer.Deserialize<JsonObject>( ref reader, options )!;
		var hasSamples = obj[nameof( CompiledSampleBlock<>.Samples )] is not null;

		return hasSamples
			? obj.Deserialize<CompiledSampleBlock<T>>( options )!
			: obj.Deserialize<CompiledConstantBlock<T>>( options )!;
	}

	public override void Write( Utf8JsonWriter writer, ICompiledPropertyBlock<T> value, JsonSerializerOptions options )
	{
		JsonSerializer.Serialize( writer, value, value.GetType(), options );
	}
}

[JsonConverter( typeof( CompiledSampleBlockConverterFactory ) )]
partial record CompiledSampleBlock<T>;

file sealed class CompiledSampleBlockConverterFactory : JsonConverterFactory
{
	public override bool CanConvert( Type typeToConvert ) =>
		typeToConvert.IsConstructedGenericType && typeToConvert.GetGenericTypeDefinition() == typeof( CompiledSampleBlock<> );

	public override JsonConverter CreateConverter( Type typeToConvert, JsonSerializerOptions options )
	{
		var valueType = typeToConvert.GetGenericArguments()[0];

		// TODO: don't hard-code this?

		if ( valueType == typeof( Transform ) )
		{
			return new CompressedTransformSampleBlockConverter();
		}

		if ( valueType == typeof( Rotation ) )
		{
			return new CompressedRotationSampleBlockConverter();
		}

		try
		{
			if ( SandboxedUnsafe.IsAcceptablePod( valueType ) )
			{
				var converterType = typeof( CompressedSampleBlockConverter<> )
					.MakeGenericType( valueType );

				return (JsonConverter)Activator.CreateInstance( converterType )!;
			}
		}
		catch
		{
			//
		}

		{
			var converterType = typeof( DefaultSampleBlockConverter<> )
				.MakeGenericType( valueType );

			return (JsonConverter)Activator.CreateInstance( converterType )!;
		}
	}
}

file class CompressedSampleBlockConverter<T> : JsonConverter<CompiledSampleBlock<T>>
	where T : unmanaged
{
	private sealed record Model( MovieTimeRange TimeRange,
		[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )] MovieTime Offset,
		int SampleRate, JsonNode Samples );

	public override void Write( Utf8JsonWriter writer, CompiledSampleBlock<T> value, JsonSerializerOptions options )
	{
		var stream = ByteStream.Create( 16 * value.Samples.Length + 4 );

		try
		{
			OnWriteSamples( ref stream, value.Samples.AsSpan() );

			using var compressed = stream.Compress();

			var base64 = Convert.ToBase64String( compressed.ToArray() );
			var model = new Model( value.TimeRange, value.Offset, value.SampleRate, base64 );

			JsonSerializer.Serialize( writer, model, options );
		}
		finally
		{
			stream.Dispose();
		}
	}

	protected virtual void OnWriteSamples( ref ByteStream stream, ReadOnlySpan<T> samples )
	{
		stream.WriteArray( samples );
	}

	public override CompiledSampleBlock<T> Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		var model = JsonSerializer.Deserialize<Model>( ref reader, options )!;

		ImmutableArray<T> samples;

		if ( model.Samples is JsonArray sampleArray )
		{
			samples = sampleArray.Deserialize<ImmutableArray<T>>( options );
		}
		else if ( model.Samples.GetValue<string>() is { } base64 )
		{
			using var compressed = ByteStream.CreateReader( Convert.FromBase64String( base64 ) );

			var stream = compressed.Decompress();

			try
			{
				samples = OnReadSamples( ref stream );
			}
			finally
			{
				stream.Dispose();
			}
		}
		else
		{
			throw new Exception( "Expected array or compressed sample string." );
		}

		return new CompiledSampleBlock<T>( model.TimeRange, model.Offset, model.SampleRate, samples );
	}

	protected virtual ImmutableArray<T> OnReadSamples( ref ByteStream stream )
	{
		return [.. stream.ReadArraySpan<T>( 0x10_0000 )];
	}
}

file sealed class CompressedTransformSampleBlockConverter : CompressedSampleBlockConverter<Transform>
{
	protected override void OnWriteSamples( ref ByteStream stream, ReadOnlySpan<Transform> samples )
	{
		stream.WriteCompressed( samples );
	}

	protected override ImmutableArray<Transform> OnReadSamples( ref ByteStream stream )
	{
		return stream.ReadCompressedTransforms();
	}
}

file sealed class CompressedRotationSampleBlockConverter : CompressedSampleBlockConverter<Rotation>
{
	protected override void OnWriteSamples( ref ByteStream stream, ReadOnlySpan<Rotation> samples )
	{
		stream.WriteCompressed( samples );
	}

	protected override ImmutableArray<Rotation> OnReadSamples( ref ByteStream stream )
	{
		return stream.ReadCompressedRotations();
	}
}

file sealed class DefaultSampleBlockConverter<T> : JsonConverter<CompiledSampleBlock<T>>
{
	private sealed record Model( MovieTimeRange TimeRange,
		[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )] MovieTime Offset,
		int SampleRate,
		ImmutableArray<T> Samples );

	public override void Write( Utf8JsonWriter writer, CompiledSampleBlock<T> value, JsonSerializerOptions options )
	{
		var model = new Model( value.TimeRange, value.Offset, value.SampleRate, value.Samples );

		JsonSerializer.Serialize( writer, model, options );
	}

	public override CompiledSampleBlock<T> Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		var model = JsonSerializer.Deserialize<Model>( ref reader, options )!;

		return new CompiledSampleBlock<T>( model.TimeRange, model.Offset, model.SampleRate, model.Samples );
	}
}
