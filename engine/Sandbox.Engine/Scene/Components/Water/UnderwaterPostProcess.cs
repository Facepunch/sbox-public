using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// The generated underwater volume's screen distortion, evaluated by the native per-camera effect pipeline.
/// </summary>
[Hide]
internal sealed class UnderwaterPostProcess : BasePostProcess<UnderwaterPostProcess>
{
	/// <summary>
	/// Strength supplied by the owning water body.
	/// </summary>
	internal float Strength { get; set; }

	/// <summary>
	/// Animation speed supplied by the owning water body.
	/// </summary>
	internal float Speed { get; set; } = 1;

	/// <summary>
	/// Phase correction supplied by the owning body when its distortion speed changes.
	/// </summary>
	internal double TimeOffset { get; set; }

	static readonly Material ScreenMaterial = Material.FromShader( "shaders/water/underwater.shader" );

	/// <summary>
	/// Distort the rendered world before blur, vignette and tonemapping, preserving later UI.
	/// </summary>
	public override void Render()
	{
		float strength = GetWeighted( x => x.Strength );
		var water = WaterBody.FindUnderwaterView( Scene, Camera.PostProcessPosition );
		float density = water.IsValid() ? water.UnderwaterParticleDensity : 0;
		if ( strength <= 0 && density <= 0 )
			return;

		Attributes.Set( "Strength", strength );
		Attributes.Set( "DistortionTime", (float)(Time.NowDouble * GetWeighted( x => x.Speed, 1f ) + GetWeighted( x => x.TimeOffset )) );
		Attributes.Set( "ParticleDensity", 0f );
		if ( density > 0 )
			water.SetUnderwaterParticleAttributes( Attributes );
		Blit( BlitMode.WithBackbuffer( ScreenMaterial, Stage.BeforePostProcess, 3000 ), "Underwater" );
	}
}
