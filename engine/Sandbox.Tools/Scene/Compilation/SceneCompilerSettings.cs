using System.IO;
using System.Text.Json;

namespace Editor;

/// <summary>
/// Immutable scene compile settings, stored in source metadata with defaults from editor cookies.
/// </summary>
internal sealed record SceneCompilerSettings
{
	internal const string MetadataProperty = "sceneCompileSettings";

	/// <summary>
	/// What an extra aggregate costs, in fragments, when deciding whether to split geometry into
	/// two of them. Higher makes fewer, bigger aggregates that cull less well.
	/// </summary>
	public float AggregateCost { get; init; } = 32.0f;

	/// <summary>
	/// Target extent before geometry is cut up. Whole triangles are never cut, splits must improve
	/// culling, and one source draw produces at most SceneAggregateObject.MaxFragments pieces.
	/// Unattainable targets retain larger fragments and issue a diagnostic.
	/// </summary>
	public float MaxChunkSize { get; init; } = 2048.0f;

	public static SceneCompilerSettings Load( Asset asset )
	{
		var metadata = asset?.AssetType?.ResourceType == typeof( SceneFile ) && File.Exists( asset.GetSourceFile( true ) )
			? asset.MetaData.GetElement( MetadataProperty )
			: null;
		var settings = metadata is null ? LoadDefaults() : metadata.Value.Deserialize<SceneCompilerSettings>()
			?? throw new InvalidDataException( "Invalid scene compile settings." );
		settings.Validate();
		return settings;
	}

	static SceneCompilerSettings LoadDefaults() => new()
	{
		AggregateCost = EditorCookie.Get( "scenecompiler.aggregatecost", 32.0f ),
		MaxChunkSize = EditorCookie.Get( "scenecompiler.maxchunksize", 2048.0f ),
	};

	internal void Validate()
	{
		if ( !float.IsFinite( AggregateCost ) || AggregateCost <= 0
			|| !float.IsFinite( MaxChunkSize ) || MaxChunkSize <= 0 )
			throw new InvalidDataException( "Scene compile settings must be finite positive numbers." );
	}

	/// <summary>
	/// Remember the last successful compile settings as the defaults for new scenes.
	/// </summary>
	public void SaveDefaults()
	{
		EditorCookie.Set( "scenecompiler.aggregatecost", AggregateCost );
		EditorCookie.Set( "scenecompiler.maxchunksize", MaxChunkSize );
	}
}
