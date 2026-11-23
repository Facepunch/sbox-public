using System.IO;
using System.Text.Json;

namespace Editor;

/// <summary>
/// Geometry compile settings saved with the source scene.
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

	/// <summary>
	/// Load the scene's saved settings, using compiler defaults for a scene without overrides.
	/// </summary>
	public static SceneCompilerSettings Load( Asset asset )
	{
		var metadata = asset?.AssetType?.ResourceType == typeof( SceneFile ) && File.Exists( asset.GetSourceFile( true ) )
			? asset.MetaData.GetElement( MetadataProperty )
			: null;
		var settings = metadata is null ? new SceneCompilerSettings() : metadata.Value.Deserialize<SceneCompilerSettings>()
			?? throw new InvalidDataException( "Invalid scene compile settings." );
		settings.Validate();
		return settings;
	}

	internal void Validate()
	{
		if ( !float.IsFinite( AggregateCost ) || AggregateCost <= 0
			|| !float.IsFinite( MaxChunkSize ) || MaxChunkSize <= 0 )
			throw new InvalidDataException( "Scene compile settings must be finite positive numbers." );
	}
}
