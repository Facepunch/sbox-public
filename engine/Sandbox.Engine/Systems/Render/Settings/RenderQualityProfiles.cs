using Sandbox.Diagnostics;
using System.IO;

namespace Sandbox.Engine.Settings;

/// <summary>
/// Render quality profiles adjust rendering features to a profile level
/// </summary>
class RenderQualityProfiles
{
	static readonly Logger log = new( "RenderQuality" );

	Dictionary<string, Dictionary<string, Dictionary<string, string>>> Profiles { get; }

	/// <summary>Names already warned about, so a bad profile doesn't spam on every change.</summary>
	readonly HashSet<string> warned = new();

	public RenderQualityProfiles()
	{
		Profiles = EngineFileSystem.CoreContent.ReadJsonOrDefault<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>( Path.Combine( "cfg", "quality_profiles.json" ), new() );
	}

	/// <summary>
	/// Push every group's convars for the levels currently stored. Has to run after the convars
	/// exist: ConVarSystem.SetValue drops writes to names it doesn't know, so calling this before
	/// registration silently leaves the managed groups at their code defaults.
	/// </summary>
	public void ApplyAll( RenderSettings settings )
	{
		SetGroupConVars( "TextureQuality", settings.TextureQuality.ToString() );
		SetGroupConVars( "VolumetricFogQuality", settings.VolumetricFogQuality.ToString() );
		SetGroupConVars( "ShadowQuality", settings.ShadowQuality.ToString() );

		// Post processing is per effect. The preset over them is derived, so there's no group for it.
		SetGroupConVars( "AmbientOcclusionQuality", settings.AmbientOcclusionQuality.ToString() );
		SetGroupConVars( "DepthOfFieldQuality", settings.DepthOfFieldQuality.ToString() );
		SetGroupConVars( "ScreenSpaceReflectionQuality", settings.ScreenSpaceReflectionQuality.ToString() );
		settings.ApplyMotionBlur();
		settings.ApplyBloom();
	}

	/// <summary>
	/// Set all the convars for a group based on the level
	/// </summary>
	public void SetGroupConVars( string group, string level )
	{
		if ( !Profiles.TryGetValue( group, out var levels ) )
			return;

		if ( !levels.TryGetValue( level, out var convars ) )
		{
			Warn( $"{group}/{level}", $"quality profile has no level '{level}' for '{group}'" );
			return;
		}

		foreach ( var convar in convars )
		{
			// Nothing downstream reports a missing convar, so check here. A renamed or misspelled
			// name would otherwise sit in the profile doing nothing at all.
			if ( ConVarSystem.Find( convar.Key ) is null )
			{
				Warn( convar.Key, $"quality profile {group}/{level} sets unknown convar '{convar.Key}'" );
				continue;
			}

			ConVarSystem.SetValue( convar.Key, convar.Value, true );
		}
	}

	void Warn( string key, string message )
	{
		if ( !warned.Add( key ) )
			return;

		log.Warning( message );
	}
}
