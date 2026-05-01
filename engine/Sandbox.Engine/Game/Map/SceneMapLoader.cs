
using NativeEngine;

namespace Sandbox;

public class SceneMapLoader : MapLoader
{
	public SceneMapLoader( SceneWorld world, PhysicsWorld physics, Vector3 origin = default ) : base( world, physics, origin )
	{
	}

	protected override void CreateObject( ObjectEntry data )
	{
		switch ( data.TypeName )
		{
			case "env_light_probe_volume":
				CreateLightProbeVolume( data );
				break;
			case "env_combined_light_probe_volume":
				CreateCombinedLightProbeVolume( data );
				break;
			case "light_environment":
			case "light_directional":
				CreateLight( data, LightType.Directional );
				break;
			case "light_rect":
				CreateLight( data, LightType.Rect );
				break;
			case "light_capsule":
				CreateLight( data, LightType.Capsule );
				break;
			case "light_spot":
				CreateLight( data, LightType.Spot );
				break;
			case "light_omni":
				CreateLight( data, LightType.Omni );
				break;
			case "light_ortho":
				CreateLight( data, LightType.Ortho );
				break;
			case "point_worldtext":
				CreatePointWorldText( data );
				break;
			default:
				CreateModel( data );
				break;
		}
	}

	protected virtual void CreateLightProbeVolume( ObjectEntry kv )
	{
		var texture = kv.GetResource<Texture>( "lightprobetexture" );
		var indicesTexture = kv.GetResource<Texture>( "lightprobetexture_dli" );
		var scalarsTexture = kv.GetResource<Texture>( "lightprobetexture_dls" );
		var boundsMin = kv.GetValue( "box_mins", new Vector3( -72.0f, -72.0f, -72.0f ) );
		var boundsMax = kv.GetValue( "box_maxs", new Vector3( 72.0f, 72.0f, 72.0f ) );
		var handshake = kv.GetValue<int>( "handshake" );
		var indoorOutdoorLevel = kv.GetValue<int>( "indoor_outdoor_level" );

		var so = new SceneLightProbe(
			World,
			texture,
			indicesTexture,
			scalarsTexture,
			new BBox( boundsMin, boundsMax ),
			kv.Transform,
			handshake,
			indoorOutdoorLevel );

		// Copy tags from Hammer to this SceneObject.
		so.Tags.SetFrom( kv.Tags );

		SceneObjects.Add( so );
	}

	protected virtual void CreateCombinedLightProbeVolume( ObjectEntry kv )
	{
		CreateLightProbeVolume( kv );
	}

	private enum LightType
	{
		Directional,
		Spot,
		Omni,
		Ortho,
		Rect,
		Capsule,
	}

	private void CreateLight( ObjectEntry kv, LightType lightType )
	{
		if ( !kv.GetValue<bool>( "enabled" ) )
			return;

		var color = kv.GetValue<Color>( "color" );
		var brightness = kv.GetValue( "brightness", 1.0f );
		var bounceScale = kv.GetValue( "bouncescale", 1.0f );
		var range = kv.GetValue( "range", 1024.0f );
		var fallOff = kv.GetValue<float>( "falloff" );
		var innerConeAngle = kv.GetValue( "innerconeangle", 45.0f );
		var outerConeAngle = kv.GetValue( "outerconeangle", 60.0f );
		var attenuation0 = kv.GetValue( "attenuation0", 0.0f );
		var attenuation1 = kv.GetValue( "attenuation1", 0.0f );
		var attenuation2 = kv.GetValue( "attenuation2", 1.0f );
		var castShadows = kv.GetValue<int>( "castshadows" ) == 1;
		var shadowCascadeCount = kv.GetValue<int>( "numcascades", 1 );
		var shadowCascadeDistanceScale = kv.GetValue<float>( "shadowcascadedistancescale" );
		var lightCookie = kv.GetResource<Texture>( "lightcookie" );
		var bakeLightIndex = kv.GetValue( "bakelightindex", -1 );
		var bakeLightIndexScale = kv.GetValue( "bakelightindexscale", 1.0f );
		var bakedLightIndexing = kv.GetValue( "baked_light_indexing", true );
		var directLight = kv.GetValue( "directlight", 2 );
		var fogLighting = kv.GetValue<int>( "fog_lighting", 2 );
		var fogContributionStrength = kv.GetValue<float>( "fogcontributionstrength", 1.0f );
		var renderDiffuse = kv.GetValue( "renderdiffuse", true );
		var renderSpecular = kv.GetValue( "renderspecular", true );
		var shadowTextureWidth = kv.GetValue<int>( "shadowtexturewidth" );
		var shadowTextureHeight = kv.GetValue<int>( "shadowtextureheight" );
		var lightSourceDim0 = kv.GetValue<float>( "lightsourcedim0" );
		var lightSourceDim1 = kv.GetValue<float>( "lightsourcedim1" );

		SceneLight sceneLight = null;

		if ( lightType == LightType.Directional )
		{
			sceneLight = new SceneDirectionalLight( World, kv.Rotation, color * brightness )
			{
				ShadowsEnabled = castShadows,
				ShadowCascadeCount = 4,
				ShadowCascadeSplitRatio = 0.91f
			};

			sceneLight.Tags.Add( "light_directional" );
		}
		else if ( lightType == LightType.Spot )
		{
			sceneLight = new SceneSpotLight( World, kv.Position, color * brightness )
			{
				Rotation = kv.Rotation,
				ShadowsEnabled = castShadows,
				ConeInner = innerConeAngle,
				ConeOuter = outerConeAngle,
				Radius = range,
				FallOff = fallOff,
				ConstantAttenuation = attenuation0,
				LinearAttenuation = attenuation1,
				QuadraticAttenuation = attenuation2 * 10000.0f,
				LightCookie = lightCookie,
			};

			sceneLight.Tags.Add( "light_spot" );

			float scaleFactor = attenuation2 * 10000 + attenuation1 * 100 + attenuation0;

			if ( scaleFactor > 0 )
			{
				sceneLight.LightColor *= scaleFactor;
			}
		}
		else if ( lightType == LightType.Omni )
		{
			sceneLight = new ScenePointLight( World, kv.Position, range, color * brightness )
			{
				Rotation = kv.Rotation,
				ShadowsEnabled = castShadows,
				Radius = range,
				ConstantAttenuation = attenuation0,
				LinearAttenuation = attenuation1,
				QuadraticAttenuation = attenuation2 * 10000.0f,
				LightCookie = lightCookie,
			};

			sceneLight.Tags.Add( "light_omni" );

			float scaleFactor = attenuation2 * 10000 + attenuation1 * 100 + attenuation0;

			if ( scaleFactor > 0 )
			{
				sceneLight.LightColor *= scaleFactor;
			}
		}
		else if ( lightType == LightType.Rect )
		{
			sceneLight = new SceneSpotLight( World, kv.Position, color * brightness )
			{
				Rotation = kv.Rotation,
				ShadowsEnabled = false, // Not yet
				Radius = range,
				ConstantAttenuation = attenuation0,
				LinearAttenuation = attenuation1,
				QuadraticAttenuation = attenuation2 * 10000.0f,
				ConeInner = 90,
				ConeOuter = 90,
				LightCookie = lightCookie,
				Shape = SceneLight.LightShape.Rectangle,
			};

			sceneLight.Tags.Add( "light_rect" );

			float scaleFactor = attenuation2 * 10000 + attenuation1 * 100 + attenuation0;

			if ( scaleFactor > 0 )
			{
				sceneLight.LightColor *= scaleFactor;
			}
		}
		else if ( lightType == LightType.Capsule )
		{
			sceneLight = new ScenePointLight( World, kv.Position, range, color * brightness )
			{
				Rotation = kv.Rotation,
				ShadowsEnabled = false, // Not yet
				Radius = range,
				ConstantAttenuation = attenuation0,
				LinearAttenuation = attenuation1,
				QuadraticAttenuation = attenuation2 * 10000.0f,
				LightCookie = lightCookie,
				Shape = SceneLight.LightShape.Capsule,
			};

			sceneLight.Tags.Add( "light_capsule" );

			float scaleFactor = attenuation2 * 10000 + attenuation1 * 100 + attenuation0;

			if ( scaleFactor > 0 )
			{
				sceneLight.LightColor *= scaleFactor;
			}
		}
		else if ( lightType == LightType.Ortho )
		{
			Log.Warning( "Ortho lights have been removed." );
		}

		if ( !sceneLight.IsValid() )
			return;

		// Copy tags from Hammer to this SceneObject.
		sceneLight.Tags.Add( "light" );
		sceneLight.Tags.Add( kv.Tags );

		var light = sceneLight.lightNative;
		light.SetWorldDirection( kv.Rotation );


		switch ( directLight )
		{
			case 3: // HAMMER_DIRECT_LIGHT_STATIONARY
				light.SetLightFlags( light.GetLightFlags() | 16 ); // LIGHTTYPE_FLAGS_MIXED_SHADOWS
				light.SetLightFlags( light.GetLightFlags() | 32 ); // LIGHTTYPE_FLAGS_BAKED
				break;
			case 1: // HAMMER_DIRECT_LIGHT_BAKED
				light.SetLightFlags( light.GetLightFlags() | 32 ); // LIGHTTYPE_FLAGS_BAKED
				break;
		}

		light.GetAttributesPtrForModify().SetFloatValue( "MixedShadowsStrength", 1.0f );
		light.SetCascadeDistanceScale( shadowCascadeDistanceScale );
		light.SetBounceColor( light.GetColor() * bounceScale );
		light.SetBakeLightIndex( bakeLightIndex );
		light.SetBakeLightIndexScale( bakeLightIndexScale );
		light.SetUsesIndexedBakedLighting( bakedLightIndexing );
		light.SetFogContributionStength( fogContributionStrength );
		light.SetRenderDiffuse( renderDiffuse );
		light.SetRenderSpecular( renderSpecular );
		light.SetFogLightingMode( fogLighting );
		light.SetShadowTextureWidth( shadowTextureWidth );
		light.SetShadowTextureHeight( shadowTextureHeight );

		if ( lightType == LightType.Ortho )
		{
			var orthoLightWidth = kv.GetValue<float>( "ortholightwidth", 512 );
			var orthoLightHeight = kv.GetValue<float>( "ortholightheight", 512 );

			float aspect = orthoLightWidth / orthoLightHeight;
			var width = shadowTextureWidth;
			width = (width == 0) ? 2048 : width;
			var height = (int)(width * aspect);
			height = height.Clamp( 1, 8196 );

			light.SetLightSourceSize0( orthoLightWidth );
			light.SetLightSourceSize1( orthoLightHeight );
			light.SetShadowTextureWidth( width / 4 );
			light.SetShadowTextureHeight( height / 4 );
		}

		if ( lightType == LightType.Rect )
		{
			light.SetLightShape( LightSourceShape_t.Rectangle );
			light.SetLightSourceDim0( lightSourceDim0 );
			light.SetLightSourceDim1( lightSourceDim1 );
		}
		else if ( lightType == LightType.Capsule )
		{
			light.SetLightShape( LightSourceShape_t.Capsule );
			light.SetLightSourceDim0( lightSourceDim0 );
			light.SetLightSourceDim1( lightSourceDim1 );
		}

		SceneObjects.Add( sceneLight );
	}

	public class TextSceneObject : SceneCustomObject
	{
		public string Text { get; set; }
		public string FontName { get; set; } = "Roboto";
		public float FontSize { get; set; } = 100.0f;
		public float FontWeight { get; set; } = 800.0f;
		public TextFlag TextFlags { get; set; } = TextFlag.DontClip;

		public TextSceneObject( SceneWorld sceneWorld ) : base( sceneWorld )
		{
			RenderLayer = SceneRenderLayer.Default;
			managedNative.ExecuteOnMainThread = false;
		}

		public override void RenderSceneObject()
		{
			Graphics.Attributes.SetCombo( "D_WORLDPANEL", 1 );
			Graphics.DrawText( new Rect( 0 ), Text, ColorTint, FontName, FontSize, FontWeight, TextFlags );
		}
	}

	protected virtual void CreatePointWorldText( ObjectEntry kv )
	{
		var message = kv.GetString( "message" );
		var fontSize = kv.GetValue<float>( "font_size" );
		var fontName = kv.GetString( "font_name" );
		var worldUnitsPerPixel = kv.GetValue<float>( "world_units_per_pixel" );
		var depthRenderOffset = kv.GetValue<float>( "depth_render_offset" );
		var color = kv.GetValue<Color>( "color" );
		var justifyHorizontal = kv.GetValue<int>( "justify_horizontal" );
		var justifyVertical = kv.GetValue<int>( "justify_vertical" );

		var textObject = new TextSceneObject( World )
		{
			Transform = new Transform( kv.Position + kv.Rotation.Up * depthRenderOffset, kv.Rotation, worldUnitsPerPixel * 0.75f ),
			LocalBounds = BBox.FromPositionAndSize( 0, 1000 ),
			ColorTint = color,
			FontName = fontName,
			FontSize = fontSize.Clamp( 1, 256 ),
			Text = message
		};

		// Copy tags from Hammer to this SceneObject.
		textObject.Tags.SetFrom( kv.Tags );
		textObject.Tags.Add( "world_text" );

		if ( justifyHorizontal == 0 )
			textObject.TextFlags |= TextFlag.Left;
		else if ( justifyHorizontal == 1 )
			textObject.TextFlags |= TextFlag.CenterHorizontally;
		else if ( justifyHorizontal == 2 )
			textObject.TextFlags |= TextFlag.Right;

		if ( justifyVertical == 0 )
			textObject.TextFlags |= TextFlag.Bottom;
		else if ( justifyVertical == 1 )
			textObject.TextFlags |= TextFlag.CenterVertically;
		else if ( justifyVertical == 2 )
			textObject.TextFlags |= TextFlag.Top;

		SceneObjects.Add( textObject );
	}

	protected virtual void CreateModel( ObjectEntry kv )
	{
		var model = kv.GetResource<Model>( "model" );
		if ( model == null || model.native.IsNull || model.IsError ) return;
		if ( model.MeshCount == 0 ) return;
		if ( !model.native.HasSceneObjects() ) return;

		var renderColor = kv.GetValue<Color>( "rendercolor" );

		var sceneObject = new SceneObject( World, model, kv.Transform );
		if ( !sceneObject.IsValid() )
			return;

		sceneObject.ColorTint = renderColor;

		// Copy tags from Hammer to this SceneObject.
		sceneObject.Tags.SetFrom( kv.Tags );

		SceneObjects.Add( sceneObject );
	}
}
