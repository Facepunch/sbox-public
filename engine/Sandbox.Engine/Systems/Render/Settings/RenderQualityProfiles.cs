using NativeEngine;
using System.IO;

namespace Sandbox.Engine.Settings;

/// <summary>
/// Render quality profiles adjust rendering features to a profile level
/// </summary>
class RenderQualityProfiles
{
	Dictionary<string, Dictionary<string, Dictionary<string, string>>> Profiles { get; }

	public RenderQualityProfiles()
	{
		Profiles = EngineFileSystem.CoreContent.ReadJsonOrDefault<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>( Path.Combine( "cfg", "quality_profiles.json" ), new() );
	}

	public void SetDefaults( RenderSettings settings )
	{
		// Set all our convars based on our set profiles as we load
		SetGroupConVars( "TextureQuality", settings.TextureQuality.ToString() );
		SetGroupConVars( "PostProcessQuality", settings.PostProcessQuality.ToString() );
		SetGroupConVars( "VolumetricFogQuality", settings.VolumetricFogQuality.ToString() );
		SetGroupConVars( "ShadowQuality", settings.ShadowQuality.ToString() );
	}

	/// <summary>
	/// Set all the convars for a group based on the level
	/// </summary>
	public void SetGroupConVars( string group, string level )
	{
		if ( !Profiles.ContainsKey( group ) )
			return;

		if ( !Profiles[group].ContainsKey( level ) )
			return;

		foreach ( var convar in Profiles[group][level] )
		{
			ConVarSystem.SetValue( convar.Key, convar.Value, true );
		}
	}
}
