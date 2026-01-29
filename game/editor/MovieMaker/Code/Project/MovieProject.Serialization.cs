using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;

namespace Editor.MovieMaker;

#nullable enable

[JsonConverter( typeof(MovieProjectConverter) )]
partial class MovieProject : IJsonPopulator
{
	internal sealed record Model(
		int SampleRate, MovieTime? Duration, VideoExportConfig? ExportConfig,
		ImmutableDictionary<Guid, ProjectTrackModel> Tracks,
		ImmutableHashSet<MovieResource>? References );

	public JsonNode Serialize() => JsonSerializer.SerializeToNode( Snapshot(), EditorJsonOptions )!;

	internal Model Snapshot()
	{
		using var scope = MovieSerializationContext.Push();

		return new Model(
			SampleRate, Duration, ExportConfig,
			Tracks.ToImmutableDictionary( x => x.Id, x => x.Serialize( EditorJsonOptions ) ),
			Tracks.OfType<ProjectSequenceTrack>().SelectMany( x => x.References ).ToImmutableHashSet() );
	}

	internal void Restore( Model model )
	{
		using var scope = MovieSerializationContext.Push();

		var config = ProjectSettings.Get<MovieMakerConfig>( Session.ConfigFileName );

		SampleRate = model.SampleRate;
		ExportConfig = model.ExportConfig;

		_rootTrackList.Clear();
		_trackDict.Clear();
		_trackList.Clear();
		_tracksChanged = true;

		var addedTracks = new Dictionary<Guid, IProjectTrack?>();

		foreach ( var (id, trackModel) in model.Tracks )
		{
			try
			{
				switch ( trackModel )
				{
					case ProjectReferenceTrackModel refModel:
						addedTracks[id] = IProjectReferenceTrack.Create( this, id, refModel.Name, refModel.TargetType );
						break;

					case ProjectPropertyTrackModel propertyModel:
						addedTracks[id] =
							IProjectPropertyTrack.Create( this, id, propertyModel.Name, propertyModel.TargetType );
						break;

					case ProjectSequenceTrackModel sequenceModel:
						addedTracks[id] = new ProjectSequenceTrack( this, id, sequenceModel.Name );
						break;

					default:
						throw new NotImplementedException();
				}
			}
			catch ( Exception ex )
			{
				Log.Warning( ex, $"Exception when deserializing track \"{trackModel.Name}\"" );

				addedTracks[id] = null;
			}
		}

		foreach ( var (id, trackModel) in model.Tracks )
		{
			if ( addedTracks[id] is not { } addedTrack ) continue;

			if ( trackModel.ParentId is { } parentId )
			{
				if ( addedTracks[parentId] is not { } parentTrack ) continue;

				AddTrackInternal( addedTrack, parentTrack );
			}
			else
			{
				AddTrackInternal( addedTrack, null );
			}
		}

		foreach ( var (id, trackModel) in model.Tracks )
		{
			var addedTrack = addedTracks[id];

			addedTrack?.Deserialize( trackModel, EditorJsonOptions );
		}
	}

	public void Deserialize( JsonNode node )
	{
		UpgradeLegacySources( node );

		Restore( node.Deserialize<Model>( EditorJsonOptions )! );
	}

	private record LegacySourceClipModel( MovieClip Clip, JsonObject? Metadata );

	private record LegacySourceRefModel( Guid Source, int TrackIndex, int BlockIndex,
		[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )] MovieTransform Transform = default,
		[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )] MovieTime SmoothingSize = default );

	/// <summary>
	/// We used to store recorded clips in a separate section, but now the
	/// recordings are just stored as normal track data.
	/// </summary>
	private static void UpgradeLegacySources( JsonNode node )
	{
		var options = EditorJsonOptions;

		var sourceModels = node["Sources"]?.Deserialize<ImmutableDictionary<Guid, LegacySourceClipModel>>( options );

		if ( sourceModels is not { Count: > 0 } ) return;

		if ( node[nameof( Model.Tracks )] is not JsonObject { Count: > 0 } tracksNode ) return;

		foreach ( var (_, trackNode) in tracksNode )
		{
			if ( trackNode?[nameof( ProjectPropertyTrackModel.Blocks )] is not JsonArray { Count: > 0 } blockNodes ) continue;

			foreach ( var blockNode in blockNodes )
			{
				if ( blockNode?[nameof( PropertyBlock<>.Signal )] is JsonObject signalNode )
				{
					blockNode[nameof( PropertyBlock<>.Signal )] = UpgradeLegacySources( signalNode, sourceModels, options );
				}
			}
		}
	}

	private static IEnumerable<string> NestedSignalPropertyNames { get; } = ["Signal", "First", "Second"];

	private static JsonObject UpgradeLegacySources( JsonObject signalNode, IReadOnlyDictionary<Guid, LegacySourceClipModel> sourceModels, JsonSerializerOptions options )
	{
		if ( signalNode["$type"]?.GetValue<string>() == "Source" )
		{
			try
			{
				var sourceRefModel = signalNode.Deserialize<LegacySourceRefModel>( options )!;
				var sourceClipModel = sourceModels[sourceRefModel.Source];
				var sourceTrack = (ICompiledPropertyTrack)sourceClipModel.Clip.Tracks[sourceRefModel.TrackIndex];
				var sourceBlock = sourceTrack.Blocks[sourceRefModel.BlockIndex];

				switch ( sourceBlock )
				{
					case ICompiledConstantBlock constBlock:
						return new JsonObject
						{
							{ "$id", signalNode["$id"]?.GetValue<int>() },
							{ "$type", "Constant" },
							{ "Value", constBlock.Serialized }
						};

					case ICompiledSampleBlock:
						{
							var sourceNode = (JsonObject)JsonSerializer.SerializeToNode( sourceBlock, sourceBlock.GetType(), options )!;

							sourceNode.Insert( 1, "$type", "Samples" );

							// Smoothing is now a separate operator

							if ( sourceRefModel.SmoothingSize.IsPositive )
							{
								return new JsonObject
								{
									{ "$id", signalNode["$id"]?.GetValue<int>() },
									{ "$type", "Smooth" },
									{ "Signal", sourceNode },
									{ "Size", JsonSerializer.SerializeToNode( sourceRefModel.SmoothingSize, options ) }
								};
							}

							sourceNode.Insert( 0, "$id", signalNode["$id"]?.GetValue<int>() );

							return sourceNode;
						}

					default:
						throw new NotImplementedException();
				}
			}
			catch ( Exception ex )
			{
				Log.Error( ex, "Unable to upgrade legacy source block." );
				return signalNode;
			}
		}

		foreach ( var propertyName in NestedSignalPropertyNames )
		{
			if ( signalNode[propertyName] is not JsonObject nestedSignalNode ) continue;

			signalNode[propertyName] = UpgradeLegacySources( nestedSignalNode, sourceModels, options );
		}

		return signalNode;
	}
}

file sealed class MovieProjectConverter : JsonConverter<MovieProject>
{
	public override void Write( Utf8JsonWriter writer, MovieProject value, JsonSerializerOptions options )
	{
		JsonSerializer.Serialize( value.Serialize(), options );
	}

	public override MovieProject? Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		var project = new MovieProject();
		var node = JsonSerializer.Deserialize<JsonNode>( ref reader, options )!;

		project.Deserialize( node );

		return project;
	}
}

file sealed class MovieSerializationContext : IDisposable
{
	public static MovieSerializationContext Push()
	{
		return Current = new MovieSerializationContext( Current );
	}

	[field: ThreadStatic]
	public static MovieSerializationContext? Current { get; private set; }

	private readonly Dictionary<IPropertySignal, int> _signalsToId = new();
	private readonly Dictionary<int, IPropertySignal> _signalsFromId = new();

	private readonly MovieSerializationContext? _parent;

	private MovieSerializationContext( MovieSerializationContext? parent )
	{
		_parent = parent;
	}

	public void ResetSignals()
	{
		_signalsToId.Clear();
		_signalsFromId.Clear();
	}

	public bool TryRegisterSignal( IPropertySignal signal, out int id )
	{
		if ( _signalsToId.TryGetValue( signal, out id ) )
		{
			return false;
		}

		_signalsToId[signal] = id = _signalsToId.Count + 1;

		return true;
	}

	public void RegisterSignal( int id, IPropertySignal signal ) => _signalsFromId[id] = signal;
	public IPropertySignal GetSignal( int id ) => _signalsFromId[id];

	public void Dispose()
	{
		if ( Current != this ) throw new InvalidOperationException();

		Current = _parent;
	}
}

[JsonPolymorphic]
[JsonDerivedType( typeof( ProjectReferenceTrackModel ), "Reference" )]
[JsonDerivedType( typeof( ProjectPropertyTrackModel ), "Property" )]
[JsonDerivedType( typeof( ProjectSequenceTrackModel ), "Sequence" )]
public abstract record ProjectTrackModel( string Name, Type TargetType,
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] Guid? ParentId );

file sealed record ProjectReferenceTrackModel( string Name, Type TargetType, Guid? ParentId, Guid? ReferenceId )
	: ProjectTrackModel( Name, TargetType, ParentId );

file sealed record ProjectPropertyTrackModel( string Name, Type TargetType, Guid? ParentId,
	[property: JsonPropertyOrder( 100 ), JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] JsonArray? Blocks )
	: ProjectTrackModel( Name, TargetType, ParentId );

file sealed record ProjectSequenceTrackModel( string Name, Guid? ParentId, ImmutableArray<ProjectSequenceBlockModel> Blocks )
	: ProjectTrackModel( Name, typeof( MovieResource ), ParentId );

file sealed record ProjectSequenceBlockModel(
	MovieTimeRange TimeRange,
	MovieTransform Transform,
	MovieResource Resource );

partial interface IProjectTrack
{
	ProjectTrackModel Serialize( JsonSerializerOptions options );
	void Deserialize( ProjectTrackModel model, JsonSerializerOptions options );
}

partial class ProjectTrack<T>
{
	public abstract ProjectTrackModel Serialize( JsonSerializerOptions options );
	public abstract void Deserialize( ProjectTrackModel model, JsonSerializerOptions options );
}

partial class ProjectReferenceTrack<T>
{
	public override ProjectTrackModel Serialize( JsonSerializerOptions options )
	{
		return new ProjectReferenceTrackModel( Name, TargetType, Parent?.Id, ReferenceId );
	}

	public override void Deserialize( ProjectTrackModel model, JsonSerializerOptions options )
	{
		if ( model is not ProjectReferenceTrackModel refModel ) return;

		ReferenceId = refModel.ReferenceId;
	}
}

partial class ProjectPropertyTrack<T>
{
	public override ProjectTrackModel Serialize( JsonSerializerOptions options )
	{
		MovieSerializationContext.Current?.ResetSignals();

		return new ProjectPropertyTrackModel( Name, TargetType, Parent?.Id,
			Blocks.Count != 0
				? JsonSerializer.SerializeToNode( Blocks, EditorJsonOptions )!.AsArray()
				: null );
	}

	public override void Deserialize( ProjectTrackModel model, JsonSerializerOptions options )
	{
		if ( model is not ProjectPropertyTrackModel propertyModel ) return;

		MovieSerializationContext.Current?.ResetSignals();

		_blocks.Clear();
		_blocksChanged = true;

		if ( propertyModel.Blocks?.Deserialize<ImmutableArray<PropertyBlock<T>>>( options ) is not { } blocks )
		{
			return;
		}

		_blocks.AddRange( blocks );
	}
}

partial class ProjectSequenceTrack
{
	public override ProjectTrackModel Serialize( JsonSerializerOptions options )
	{
		return new ProjectSequenceTrackModel( Name, Parent?.Id, [..Blocks.Select( x => new ProjectSequenceBlockModel( x.TimeRange, x.Transform, x.Resource ) )] );
	}

	public override void Deserialize( ProjectTrackModel model, JsonSerializerOptions options )
	{
		if ( model is not ProjectSequenceTrackModel sequenceModel ) return;

		_tracksInvalid = true;

		_blocks.Clear();

		if ( sequenceModel.Blocks is { IsDefaultOrEmpty: false } blocks )
		{
			_blocks.AddRange( blocks.Select( x => new ProjectSequenceBlock( x.TimeRange, x.Transform, x.Resource ) ) );
		}
	}
}

public sealed class JsonDiscriminatorAttribute( string value ) : Attribute
{
	public string Value { get; } = value;
}

[JsonConverter( typeof(PropertySignalConverterFactory) )]
partial record PropertySignal<T>;

file class PropertySignalConverterFactory : JsonConverterFactory
{
	public override bool CanConvert( Type typeToConvert ) =>
		typeToConvert.IsConstructedGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(PropertySignal<>);

	public override JsonConverter CreateConverter( Type typeToConvert, JsonSerializerOptions options )
	{
		var valueType = typeToConvert.GetGenericArguments()[0];
		var converterType = typeof(PropertySignalConverter<>).MakeGenericType( valueType );

		return (JsonConverter)Activator.CreateInstance( converterType )!;
	}
}

file class PropertySignalConverter<T> : JsonConverter<PropertySignal<T>>
{
	[SkipHotload]
	private static ImmutableDictionary<string, Type>? _discriminatorLookup;

	private static string GetDiscriminator( Type type )
	{
		return type.GetCustomAttribute<JsonDiscriminatorAttribute>()?.Value ?? type.Name;
	}

	private static Type GetType( string discriminator )
	{
		_discriminatorLookup ??= EditorTypeLibrary.GetTypesWithAttribute<JsonDiscriminatorAttribute>()
			.Where( x => !x.Type.IsAbstract && x.Type.IsGenericType )
			.Select( x => (Name: x.Attribute.Value, Type: x.Type.TargetType.MakeGenericType( typeof(T) )) )
			.ToImmutableDictionary( x => x.Name, x => x.Type );

		return _discriminatorLookup[discriminator];
	}

	public override void Write( Utf8JsonWriter writer, PropertySignal<T> value, JsonSerializerOptions options )
	{
		var context = MovieSerializationContext.Current!;

		if ( !context.TryRegisterSignal( value, out var id ) )
		{
			writer.WriteNumberValue( id );
			return;
		}

		var type = value.GetType();
		var node = JsonSerializer.SerializeToNode( value, type, options )!.AsObject();

		node.Insert( 0, "$id", id );
		node.Insert( 1, "$type", GetDiscriminator( type ) );

		JsonSerializer.Serialize( writer, node, options );
	}

	public override PropertySignal<T>? Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		if ( reader.TokenType == JsonTokenType.Number )
		{
			var refId = JsonSerializer.Deserialize<int>( ref reader, options );

			return (PropertySignal<T>)MovieSerializationContext.Current!.GetSignal( refId );
		}

		var node = JsonSerializer.Deserialize<JsonObject>( ref reader, options )!;
		var discriminator = node["$type"]!.GetValue<string>();
		var type = GetType( discriminator );

		var signal = (PropertySignal<T>)node.Deserialize( type, options )!;

		if ( node["$id"]?.GetValue<int>() is { } id )
		{
			// Signals can be referenced multiple times if they have an ID,
			// to avoid serializing them over and over again

			MovieSerializationContext.Current!.RegisterSignal( id, signal );
		}

		return signal;
	}
}
