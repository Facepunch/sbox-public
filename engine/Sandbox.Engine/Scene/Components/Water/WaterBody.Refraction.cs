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

	/// <summary>
	/// Whether water draws the scene under it into its surface. Off, a body in view no longer renders the scene a second time.
	/// </summary>
	[ConVar( "r.water.refraction", Help = "Draw the scene under water into its surface (0: off, saves a second render of the scene)." )]
	internal static bool Refraction { get; set; } = true;

	/// <summary>
	/// Record the refraction capture: a view through the surface rendered where the surface draws, into a target the
	/// renderer takes then, bound to the surface's draws with the view's viewport.
	/// </summary>
	void AddRefractionCapture( CommandList commands, in CaptureKey key )
	{
		var fog = key.Fog;
		var camera = key.Camera;
		var target = commands.GetRenderTarget( "Water refraction", key.Downsample, ImageFormat.RGBA16161616F, ImageFormat.None );
		var view = commands.DrawRefraction( camera, key.Plane, target, new RefractionSetup
		{
			RenderBehind = true,
			FallbackColor = Color.Transparent,
			ViewSetup = new ViewSetup
			{
				ProjectionMatrix = key.Unclipped ? key.Projection : null,
				EnablePostprocessing = false,
				EnableUI = false,
				EnableDebugOverlays = false,
				ViewHash = HashCode.Combine( Id, 0 ),
				ClearColor = key.Below || fog is null ? null : fog.Value.Color,
				Fog = fog,
				ExcludeTags = _captureExclusions
			}
		} );
		commands.Attributes.Set( "RefractionTexture", target.ColorTexture );
		commands.Attributes.Set( "RefractionViewport", view.Viewport );
		commands.Attributes.Set( "HasRefractionTexture", view.IsValid );
		commands.Attributes.Set( "RefractionHasSceneFog", fog is null );
	}

	/// <summary>
	/// Whether two refraction fogs are the same: what <see cref="RefractionFog"/> sets, exactly.
	/// </summary>
	static bool SameFog( FogSetup? a, FogSetup? b )
	{
		if ( a is not { } x || b is not { } y )
			return a.HasValue == b.HasValue;

		return x.Mode == y.Mode && SameColor( x.Color, y.Color ) && x.DistanceEnabled == y.DistanceEnabled
			&& x.AttenuationDistance == y.AttenuationDistance && x.SunLightingEnabled == y.SunLightingEnabled
			&& x.HeightEnabled == y.HeightEnabled && x.BaseHeight == y.BaseHeight && x.HeightFalloff == y.HeightFalloff
			&& x.HeightExponent == y.HeightExponent && SameColor( x.HeightColor, y.HeightColor );
	}

	static bool SameColor( Color a, Color b ) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
}
