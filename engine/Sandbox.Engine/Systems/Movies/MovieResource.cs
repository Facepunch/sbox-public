using System.Collections.Immutable;
using Sandbox.MovieMaker.Compiled;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox.MovieMaker;

#nullable enable

/// <summary>
/// A container for a <see cref="MovieClip"/>, including optional <see cref="EditorData"/>.
/// </summary>
[JsonConverter( typeof( MovieResourceConverter ) )]
public interface IMovieResource
{
	/// <summary>
	/// Compiled movie clip.
	/// </summary>
	MovieClip? Compiled { get; set; }

	/// <summary>
	/// Editor-only data used to generate <see cref="Compiled"/>.
	/// </summary>
	JsonNode? EditorData { get; set; }

	/// <summary>
	/// Mark this resource as modified, with changes coming from the given <paramref name="project"/>.
	/// </summary>
	void StateHasChanged( IMovieProject project );
}

/// <summary>
/// An editor-only movie project that can be compiled into a <see cref="MovieClip"/>.
/// </summary>
public interface IMovieProject : IJsonPopulator
{
	/// <summary>
	/// Compile this project into a playable <see cref="MovieClip"/>.
	/// </summary>
	MovieClip? Compile();
}

/// <summary>
/// A movie clip created with the MoviePlayer component.
/// </summary>
[AssetType( Name = "Movie Clip", Extension = "movie", Flags = AssetTypeFlags.NoEmbedding )]
public sealed class MovieResource : GameResource, IMovieResource
{
	private MovieClip? _compiled;
	private JsonNode? _editorData;
	private IMovieProject? _project;

	/// <inheritdoc />
	[Hide]
	public MovieClip? Compiled
	{
		get => _compiled ??= _project?.Compile();
		set => _compiled = value;
	}

	/// <inheritdoc />
	[Hide]
	public JsonNode? EditorData
	{
		get => _editorData ??= _project?.Serialize();
		set => _editorData = value;
	}

	protected override void OnJsonSerialize( JsonObject node )
	{
		// Here we're writing the .movie, not the .movie_c

		// We only want EditorData to be written here,
		// MovieCompiler will handle writing Compiled to the .movie_c

		// If there is no EditorData, just leave Compiled

		if ( node[nameof( EditorData )] is not null )
		{
			node.Remove( nameof( Compiled ) );
		}
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "video_file", width, height );
	}

	/// <inheritdoc />
	public void StateHasChanged( IMovieProject project )
	{
		ArgumentNullException.ThrowIfNull( project );

		_compiled = null;
		_editorData = null;
		_project = project;

		StateHasChanged();
	}
}

/// <summary>
/// An <see cref="IMovieClip"/> embedded in a property.
/// </summary>
public sealed class EmbeddedMovieResource : IMovieResource
{
	private MovieClip? _compiled;
	private JsonNode? _editorData;
	private IMovieProject? _project;

	/// <inheritdoc />
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public MovieClip? Compiled
	{
		get => _compiled ??= _project?.Compile();
		set
		{
			_compiled = value;
			ReferencedPackages = FindReferencedPackages( value );
		}
	}

	/// <inheritdoc />
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public JsonNode? EditorData
	{
		get => _editorData ??= _project?.Serialize();
		set => _editorData = value;
	}

	[JsonInclude]
	[JsonPropertyName( "__references" )]
	internal ImmutableArray<string> ReferencedPackages { get; set; } = [];

	/// <inheritdoc />
	public void StateHasChanged( IMovieProject project )
	{
		ArgumentNullException.ThrowIfNull( project );

		_compiled = null;
		_editorData = null;
		_project = project;
	}

	private static ImmutableArray<string> FindReferencedPackages( MovieClip? clip )
	{
		if ( clip is null || clip == MovieClip.Empty ) return [];

		var packages = new HashSet<string>();

		// Include current game's ident so we can import movies recorded in-game
		// into empty editor projects

		if ( Package.TryParseIdent( Game.Ident, out var parsed ) )
		{
			// Parse to strip out #local etc

			packages.Add( $"{parsed.org}.{parsed.package}" );
		}

		foreach ( var package in clip.ResolvePrimaryPackages() )
		{
			packages.Add( $"{package.FullIdent}#{package.Revision.VersionId}" );
		}

		return [.. packages];
	}
}

file sealed class MovieResourceConverter : JsonConverter<IMovieResource>
{
	public override void Write( Utf8JsonWriter writer, IMovieResource value, JsonSerializerOptions options )
	{
		switch ( value )
		{
			case MovieResource resource:
				writer.WriteStringValue( resource.ResourcePath );
				return;

			case EmbeddedMovieResource embedded:
				JsonSerializer.Serialize( writer, embedded, options );
				return;

			default:
				throw new NotImplementedException();
		}
	}

	public override IMovieResource Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		switch ( reader.TokenType )
		{
			case JsonTokenType.String:
				return JsonSerializer.Deserialize<MovieResource>( ref reader, options )!;

			case JsonTokenType.StartObject:
				return JsonSerializer.Deserialize<EmbeddedMovieResource>( ref reader, options )!;

			default:
				throw new Exception( "Expected resource path or embedded resource object." );
		}
	}
}
