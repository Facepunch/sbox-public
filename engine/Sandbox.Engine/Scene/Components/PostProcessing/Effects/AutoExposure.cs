using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Applies the Auto Exposure to the camera.
/// </summary>
[Title( "Auto Exposure" )]
[Category( "Post Processing" )]
[Icon( "exposure" )]
public class AutoExposure : BasePostProcess<AutoExposure>
{
	//
	// All of this auto exposure stuff is now its own component
	// Should be before tonemapping, not part of it
	//

	[Property, Range( 0.0f, 3.0f )]
	public float MinimumExposure { get; set; } = 1.0f;

	[Property, Range( 0.0f, 5.0f )]
	public float MaximumExposure { get; set; } = 3.0f;

	[Property, Range( -5.0f, 5.0f )]
	public float ExposureCompensation { get; set; } = 0.0f;

	[Property, Range( 1.0f, 10.0f )]
	public float Rate { get; set; } = 1.0f;


	public override void Render()
	{
		if ( !Camera.IsValid() ) return;

		Camera.AutoExposure.Enabled = true;
		Camera.AutoExposure.Compensation = GetWeighted( x => x.ExposureCompensation, 0 );
		Camera.AutoExposure.MinimumExposure = GetWeighted( x => x.MinimumExposure, 1 );
		Camera.AutoExposure.MaximumExposure = GetWeighted( x => x.MaximumExposure, 3 );
		Camera.AutoExposure.Rate = GetWeighted( x => x.Rate, 1 );
	}

}
