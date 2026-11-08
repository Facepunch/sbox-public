using Sandbox.Rendering;

namespace Sandbox;

public sealed partial class WaterBody
{
	/// <summary>
	/// Water fog replacing the scene atmosphere in above-water refraction captures, from the material's
	/// through-surface fog: its colour, the depths below the mean surface where it starts and is full, the depth
	/// curve, and with distance fog on, the distance removing 63.2 percent of background light. The native height gradient follows world
	/// Z, even for rotated water. Null inherits the scene atmosphere when both contributions are off.
	/// </summary>
	internal FogSetup? RefractionFog
	{
		get
		{
			var material = ActiveMaterial;
			var depth = material.GetVector4( "WaterFogDepthRange" );
			float end = float.IsFinite( depth.y ) ? MathF.Max( depth.y, 0 ) : 0;
			float start = float.IsFinite( depth.x ) ? Math.Clamp( depth.x, 0, end ) : 0;
			float attenuation = WaterFloat( material, "WaterFogDistance" ) != 0
				? WaterFloat( material, "WaterFogAttenuationDistance" ) : 0;
			attenuation = float.IsFinite( attenuation ) ? Math.Clamp( attenuation, 0, 4096 ) : 0;
			if ( end <= 0 && attenuation <= 0 )
				return null;

			var color = WaterFogColor( material );
			return new FogSetup
			{
				Mode = FogMode.Exponential,
				Color = color,
				DistanceEnabled = attenuation > 0,
				AttenuationDistance = attenuation,
				SunLightingEnabled = false,
				HeightEnabled = end > 0,
				BaseHeight = SurfaceOrigin.z - end,
				HeightFalloff = end - start,
				HeightExponent = Math.Clamp( WaterFloat( material, "WaterFogDepthCurve", 0.5f ), 0.1f, 4 ),
				HeightColor = color
			};
		}
	}

	/// <summary>
	/// The material's water fog colour, which underwater fog starts from too. Black when it isn't finite.
	/// </summary>
	static Color WaterFogColor( Material material )
	{
		var color = material.GetColor( "WaterFogColor" ).WithAlpha( 1 );
		return float.IsFinite( color.r + color.g + color.b ) ? color : Color.Black;
	}
}
