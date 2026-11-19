using Borealis.Features;

namespace Sandbox;

public partial class SceneLight
{
	/// <summary>
	/// A point or spot light's settings and packed description, as native's binner reads a <c>CSceneLightObject</c>.
	/// </summary>
	internal override void WriteRender( Borealis.RenderObject obj, Rendering.SceneObjectChange change, Rendering.ISceneWorldRender render )
	{
		base.WriteRender( obj, change, render );
		if ( obj is not Borealis.LightObject light ) return;

		if ( (change & Rendering.SceneObjectChange.Transform) != 0 )
			light.Transform = Transform;

		if ( (change & Rendering.SceneObjectChange.Settings) != 0 )
		{
			light.Color = LightColor;
			light.Radius = Radius;
			light.Attenuation = QuadraticAttenuation;
			light.CastShadows = ShadowsEnabled;
			light.ShadowHardness = ShadowHardness;

			if ( this is SceneSpotLight spot )
			{
				light.ConeInner = spot.ConeInner;
				light.ConeOuter = spot.ConeOuter;
			}

			light.Cookie = this is SceneSpotLight ? render.Texture( LightCookie ) : null;
		}

		// Its description packed (attenuation, falloff and baking from maps included: Light.LegacyLightData)
		if ( (change & (Rendering.SceneObjectChange.Settings | Rendering.SceneObjectChange.Transform)) != 0 && this.IsValid() )
		{
			Rendering.LightPacking.Pack( this, out var packed, out var flags, out var bakeIndex );

			// The binner sets the cookie's index on Borealis.Gpu from the texture
			if ( render.OnDevice && (packed.Flags & LightBinnerFeature.LightCookieEnabled) != 0 )
			{
				packed.Flags &= ~LightBinnerFeature.LightCookieEnabled;
				packed.LightCookieTextureIndex = 0xFFFFFFFF;
			}

			light.NativePacked = packed;
			light.Baked = (flags & Rendering.LightPacking.TypeFlagsBaked) != 0;
			light.MixedShadows = (flags & Rendering.LightPacking.TypeFlagsMixedShadows) != 0;
			light.BakeIndex = bakeIndex;
		}
	}
}
