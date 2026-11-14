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
	private readonly SceneCamera Camera;
	private ITonemapSystem System;
	private SceneTonemapParameters_t Parameters = new SceneTonemapParameters_t();

	internal TonemapSystem( SceneCamera camera )
	{
		Camera = camera;
		System = Camera.ToneMapping?.GetNative() ?? default;

		if ( System.IsValid )
		{
			SceneTonemapParameters_t defaultParams = new SceneTonemapParameters_t();
			System.SetTonemapParameters( ref defaultParams );
		}
	}

	internal void SetTonemapParameters( SceneTonemapParameters_t parameters )
	{
		Parameters = parameters;
		Update();
	}

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
			Update();
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
			Update();
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
			Update();
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
			Update();
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
			Update();
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
			Update();
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
			Update();
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
			Update();
		}
	}

	private void Update()
	{
		if ( System.IsValid )
		{
			System.SetTonemapParameters( ref Parameters );
		}
	}

	// The camera's auto exposure on a Borealis.Gpu device, made when its frames are drawn there
	Borealis.Backend.AutoExposure gpuExposure;
	BorealisGpuRenderBackend gpuExposureDevice;

	/// <summary>
	/// The camera's auto exposure on <paramref name="backend"/>'s device: native's tonemap system ported, with the settings
	/// native's has (what the tonemapping component sets through <c>CameraComponent.AutoExposure</c>). A frame there calls
	/// its <see cref="Borealis.Backend.AutoExposure.FrameUpdate"/> where native's frame update runs, and counts its bucket over
	/// the HDR colour before tonemapping (<see cref="BorealisGpuFrame.CountExposure"/>). Null while exposure is off.
	/// </summary>
	internal Borealis.Backend.AutoExposure GpuExposure( BorealisGpuRenderBackend backend )
	{
		if ( !Enabled ) return null;

		if ( gpuExposure is null || gpuExposureDevice != backend )
		{
			gpuExposure?.Dispose();
			gpuExposure = backend.CreateExposure();
			gpuExposureDevice = backend;
		}

		gpuExposure.SetSettings( BorealisGpuRenderBackend.Settings( Parameters ) );
		return gpuExposure;
	}

	/// <summary>Free the exposure made on a Borealis.Gpu device, once its device is done with it.</summary>
	internal void DisposeGpuExposure()
	{
		gpuExposure?.Dispose();
		gpuExposure = null;
		gpuExposureDevice = null;
	}
}
