namespace Sandbox;

public sealed partial class WaterBody
{
	/// <summary>
	/// Animated screen distortion while submerged, independent of surface refraction, clarity and fog.
	/// Zero leaves the underwater view undistorted.
	/// </summary>
	[Property, Feature( nameof( Underwater ) ), Group( "Screen Effects" ), Order( -69 ), Title( "Screen Distortion" ), Range( 0, 2 )]
	public float UnderwaterDistortion
	{
		get;
		set => field = float.IsFinite( value ) ? Math.Clamp( value, 0, 2 ) : 1;
	} = 1;

	/// <summary>
	/// Screen distortion animation speed, independent of surface waves. Zero uses a stationary pattern.
	/// Changes preserve the current phase, including when stopping and resuming.
	/// </summary>
	[Property, Feature( nameof( Underwater ) ), Group( "Screen Effects" ), Order( -68 ), Title( "Distortion Speed" ), Range( 0, 4 )]
	public float UnderwaterDistortionSpeed
	{
		get;
		set
		{
			value = float.IsFinite( value ) ? Math.Clamp( value, 0, 4 ) : 1;
			UnderwaterDistortionTimeOffset += Time.NowDouble * ((double)field - value);
			field = value;
		}
	} = 1;

	/// <summary>
	/// Distortion-clock offset, preserving phase across speed edits, cloning and serialization.
	/// </summary>
	[Property, Hide]
	public double UnderwaterDistortionTimeOffset
	{
		get;
		set => field = double.IsFinite( value ) ? value : 0;
	}
}
