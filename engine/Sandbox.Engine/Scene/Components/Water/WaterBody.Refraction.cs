using Sandbox.Rendering;

namespace Sandbox;

public sealed partial class WaterBody
{
	/// <summary>
	/// Divisor of the refraction capture's dimensions relative to the viewing camera, set by the
	/// water quality level. 1 is full resolution.
	/// </summary>
	[ConVar( "r.water.refraction.downsample", Min = 1, Max = 8, Help = "Water refraction resolution divisor (1: full, 2: half, 4: quarter)." )]
	internal static int RefractionDownsampleFactor { get; set; } = 1;

	void AddRefractionCapture( CommandList commands, CameraComponent camera, Vector3 normal, bool unclipped )
	{
		bool below = ViewSurfaceDepth( camera.WorldPosition ) > 0;
		var fog = below ? default( FogSetup ) : RefractionFog;
		var target = commands.GetRenderTarget( "Water refraction", CaptureSizeFactor( RefractionDownsampleFactor ), ImageFormat.RGBA16161616F, ImageFormat.None );
		var view = commands.DrawRefraction( camera, new Plane( SurfaceOrigin + normal * (CaptureDisplacement + 0.125f), -normal ), target, new RefractionSetup
		{
			RenderBehind = true,
			FallbackColor = Color.Transparent,
			ViewSetup = new ViewSetup
			{
				ProjectionMatrix = unclipped ? camera.ProjectionMatrix : null,
				EnablePostprocessing = false,
				EnableUI = false,
				EnableDebugOverlays = false,
				ViewHash = HashCode.Combine( Id, 0 ),
				ClearColor = below || fog is null ? null : fog.Value.Color,
				Fog = fog,
				ExcludeTags = _captureExclusions
			}
		} );
		commands.Attributes.Set( "RefractionTexture", target.ColorTexture );
		commands.Attributes.Set( "RefractionViewport", view.Viewport );
		commands.Attributes.Set( "HasRefractionTexture", view.IsValid );
		commands.Attributes.Set( "RefractionHasSceneFog", fog is null );
	}
}
