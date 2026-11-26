namespace Sandbox;

public partial class SceneCamera
{
	/// <summary>
	/// Access tonemapping properties of camera
	/// </summary>
	internal TonemapSystem Tonemap { get; set; }
}

internal class TonemapSystem
{
	SceneTonemapParameters_t Parameters = new();

	/// <summary>
	/// Enable or disable exposure.
	/// </summary>
	public bool Enabled { get; set; } = false;

	/// <summary>
	/// The rate of change for exposure.
	/// </summary>
	public float Rate
	{
		get => Parameters.m_flRate;
		set
		{
			Parameters.m_flRate = value;
		}
	}

	/// <summary>
	/// Set the speed at which exposure fades downwards (diminishes) in response to light changes.
	/// </summary>
	public float Fade
	{
		get => Parameters.m_flAccelerateExposureDown;
		set
		{
			Parameters.m_flAccelerateExposureDown = value;
		}
	}

	/// <summary>
	/// Minimum auto exposure scale
	/// </summary>
	public float MinExposure
	{
		get => Parameters.m_flAutoExposureMin;
		set
		{
			Parameters.m_flAutoExposureMin = value;
		}
	}

	/// <summary>
	/// Maximum auto exposure scale
	/// </summary>
	public float MaxExposure
	{
		get => Parameters.m_flAutoExposureMax;
		set
		{
			Parameters.m_flAutoExposureMax = value;
		}
	}

	/// <summary>
	/// Number of stops to adjust auto-exposure by
	/// </summary>
	public float ExposureCompensation
	{
		get => MathF.Log( Parameters.m_flExposureCompensationScalar, 2.0f );
		set
		{
			Parameters.m_flExposureCompensationScalar = MathF.Pow( 2.0f, value );
		}
	}

	/// <summary>
	/// Set a custom brightness target to go along with 'Target Bright Pixel Percentage'. (-1 for default engine behavior)
	/// </summary>
	public float PercentTarget
	{
		get => Parameters.m_flTonemapPercentTarget;
		set
		{
			Parameters.m_flTonemapPercentTarget = value;
		}
	}

	/// <summary>
	/// Set a target for percentage of pixels above a certain brightness. (-1 for default engine behavior)
	/// </summary>
	public float PercentBrightPixels
	{
		get => Parameters.m_flTonemapPercentBrightPixels;
		set
		{
			Parameters.m_flTonemapPercentBrightPixels = value;
		}
	}

	/// <summary>
	/// Set the minimum average luminance. This ensures that certain regions aren't too dim after tonemapping
	/// </summary>
	public float MinAverageLuminance
	{
		get => Parameters.m_flTonemapMinAvgLum;
		set
		{
			Parameters.m_flTonemapMinAvgLum = value;
		}
	}

	internal ref readonly SceneTonemapParameters_t Settings => ref Parameters;
}
