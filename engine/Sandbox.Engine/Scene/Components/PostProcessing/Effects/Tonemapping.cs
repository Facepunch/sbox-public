using Sandbox.Rendering;
using System.Text.Json.Nodes;

namespace Sandbox;

/// <summary>
/// Applies a tonemapping effect to the camera.
/// </summary>
[Title( "Tone Mapping" )]
[Category( "Post Processing" )]
[Icon( "exposure" )]
public class Tonemapping : BasePostProcess<Tonemapping>
{
	/// <summary>
	/// Options to select a tonemapping algorithm to use for color grading.
	/// </summary>
	public enum TonemappingMode
	{
		/// <summary>
		/// John Hable's filmic tonemapping algorithm.
		/// Matches the default curve Source 2 uses based on Uncharted 2.
		/// </summary>
		HableFilmic = 1,
		/// <summary>
		/// The most realistic tonemapper at handling bright light, desaturating light as it becomes brighter.
		/// This is slightly more expensive than other options.
		/// </summary>
		ACES,
		/// <summary>
		/// Reinhard's tonemapper, which is a simple and fast tonemapper.
		/// </summary>
		ReinhardJodie,
		/// <summary>
		/// Linear tonemapper.
		/// </summary>
		Linear,
		/// <summary>
		/// Default AgX implementation.
		/// </summary>
		AgX
	}

	public enum ExposureColorSpaceEnum
	{
		RGB,
		Luminance
	}

	/// <summary>
	/// Which tonemapping algorithm to use for color grading.
	/// </summary>
	[Property, MakeDirty] public TonemappingMode Mode { get; set; } = TonemappingMode.HableFilmic;

	[ShowIf( nameof( Mode ), TonemappingMode.HableFilmic )]
	[Property, MakeDirty] public ExposureColorSpaceEnum ExposureMethod { get; set; } = ExposureColorSpaceEnum.RGB;

	public override void Render()
	{
		var shader = Material.FromShader( "shaders/tonemapping/tonemapping.shader" );

		Attributes.SetComboEnum( "D_TONEMAPPING", Mode );
		Attributes.SetComboEnum( "D_EXPOSUREMETHOD", ExposureMethod );

		var blit = BlitMode.WithBackbuffer( shader, Stage.Tonemapping, 1 ); // The actual tonemap should be after the autoexposure
		Blit( blit, "Tonemapping" );
	}

	public override int ComponentVersion => 3;
	// if there were components that set these, we have to keep these
	[Obsolete( "Use the separate Auto Exposure component" )]
	public bool AutoExposureEnabled { get; set; }

	[Obsolete( "Use the separate Auto Exposure component" )]
	public float MinimumExposure { get; set; }

	[Obsolete( "Use the separate Auto Exposure component" )]
	public float MaximumExposure { get; set; }

	[Obsolete( "Use the separate Auto Exposure component" )]
	public float ExposureCompensation { get; set; }

	[Obsolete( "Use the separate Auto Exposure component" )]
	public float Rate { get; set; }

	/// <summary>
	/// Remove Exposure Bias
	/// this doesn't make much sense since it's tied to only HableFilmic and does the same thing as ExposureCompensation
	/// </summary>
	[Expose, JsonUpgrader( typeof( Tonemapping ), 3 )]
	static void Upgrader_v3( JsonObject obj )
	{
		if ( obj.TryGetPropertyValue( "Mode", out var mode ) )
		{
			if ( mode.ToString() != "HableFilmic" )
				return;
		}

		if ( obj.TryGetPropertyValue( "ExposureBias", out var exposureBias ) && obj.TryGetPropertyValue( "ExposureCompensation", out var exposureCompensation ) )
		{
			obj["ExposureCompensation"] = ((float)exposureBias * 0.5f) + ((float)exposureCompensation);
		}
	}

	/// <summary>
	/// Remap the old "Legacy" mode to HableFilmic 
	/// </summary>
	[Expose, JsonUpgrader( typeof( Tonemapping ), 2 )]
	static void Upgrader_v2( JsonObject obj )
	{
		// Remap the old "Legacy" mode to HableFilmic
		if ( obj.TryGetPropertyValue( "Mode", out var mode ) )
		{
			if ( mode.ToString() == "Legacy" )
				obj["Mode"] = (int)TonemappingMode.HableFilmic;
		}
	}

}
