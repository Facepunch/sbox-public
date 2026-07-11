
using NativeEngine;
using Sandbox.Rendering;

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

	internal enum LightType
	{
		Directional,
		Spot,
		Omni,
		Ortho,
		Rect,
		Capsule,
	}

	/// <summary>
	/// Parsed keyvalues for a Hammer light entity. Shared by the raw <see cref="SceneLight"/> path
	/// here and the GameObject/component path used when a map loads into a scene.
	/// </summary>
	internal readonly struct LightData
	{
		public LightType Type { get; init; }
		public bool Enabled { get; init; }
		public Color Color { get; init; }
		public float Brightness { get; init; }
		public float Range { get; init; }
		public float FallOff { get; init; }
		public float InnerConeAngle { get; init; }
		public float OuterConeAngle { get; init; }
		public float Attenuation0 { get; init; }
		public float Attenuation1 { get; init; }
		public float Attenuation2 { get; init; }
		public bool CastShadows { get; init; }
		public Texture LightCookie { get; init; }
		public int BakeLightIndex { get; init; }
		public float BakeLightIndexScale { get; init; }
		public bool BakedLightIndexing { get; init; }
		public int DirectLight { get; init; }
		public int FogLighting { get; init; }
		public float FogContributionStrength { get; init; }
		public bool RenderDiffuse { get; init; }
		public bool RenderSpecular { get; init; }
		public float LightSourceDim0 { get; init; }
		public float LightSourceDim1 { get; init; }

		/// <summary>
		/// The light color after brightness and attenuation scaling, matching the raw scene light.
		/// </summary>
		public Color FinalColor
		{
			get
			{
				var color = Color * Brightness;

				// Point/spot style lights scale their color by the attenuation magnitude.
				if ( Type is LightType.Spot or LightType.Omni or LightType.Rect or LightType.Capsule )
				{
					float scaleFactor = Attenuation2 * 10000 + Attenuation1 * 100 + Attenuation0;
					if ( scaleFactor > 0 )
						color *= scaleFactor;
				}

				return color;
			}
		}

		public static LightData Parse( ObjectEntry kv, LightType type ) => new()
		{
			Type = type,
			Enabled = kv.GetValue<bool>( "enabled" ),
			Color = kv.GetValue<Color>( "color" ),
			Brightness = kv.GetValue( "brightness", 1.0f ),
			Range = kv.GetValue( "range", 1024.0f ),
			FallOff = kv.GetValue<float>( "falloff" ),
			InnerConeAngle = kv.GetValue( "innerconeangle", 45.0f ),
			OuterConeAngle = kv.GetValue( "outerconeangle", 60.0f ),
			Attenuation0 = kv.GetValue( "attenuation0", 0.0f ),
			Attenuation1 = kv.GetValue( "attenuation1", 0.0f ),
			Attenuation2 = kv.GetValue( "attenuation2", 1.0f ),
			CastShadows = kv.GetValue<int>( "castshadows" ) == 1,
			LightCookie = kv.GetResource<Texture>( "lightcookie" ),
			BakeLightIndex = kv.GetValue( "bakelightindex", -1 ),
			BakeLightIndexScale = kv.GetValue( "bakelightindexscale", 1.0f ),
			BakedLightIndexing = kv.GetValue( "baked_light_indexing", true ),
			DirectLight = kv.GetValue( "directlight", 2 ),
			FogLighting = kv.GetValue<int>( "fog_lighting", 2 ),
			FogContributionStrength = kv.GetValue<float>( "fogcontributionstrength", 1.0f ),
			RenderDiffuse = kv.GetValue( "renderdiffuse", true ),
			RenderSpecular = kv.GetValue( "renderspecular", true ),
			LightSourceDim0 = kv.GetValue<float>( "lightsourcedim0" ),
			LightSourceDim1 = kv.GetValue<float>( "lightsourcedim1" ),
		};

		/// <summary>
		/// The advanced settings that aren't exposed as component properties, for the component path.
		/// </summary>
		public readonly Light.LegacyLightData ToLegacyData() => new()
		{
			DirectLight = DirectLight,
			BakeLightIndex = BakeLightIndex,
			BakeLightIndexScale = BakeLightIndexScale,
			BakedLightIndexing = BakedLightIndexing,
			FogContributionStrength = FogContributionStrength,
			RenderDiffuse = RenderDiffuse,
			RenderSpecular = RenderSpecular,
			FogLighting = FogLighting,
		};
	}

	private void CreateLight( ObjectEntry kv, LightType lightType )
	{
		var data = LightData.Parse( kv, lightType );
		if ( !data.Enabled )
			return;

		SceneLight sceneLight = null;

		if ( lightType == LightType.Directional )
		{
			sceneLight = new SceneDirectionalLight( World, kv.Rotation, data.FinalColor )
			{
				ShadowsEnabled = data.CastShadows,
				ShadowCascadeCount = 4,
				ShadowCascadeSplitRatio = 0.91f
			};

			sceneLight.Tags.Add( "light_directional" );
		}
		else if ( lightType == LightType.Spot )
		{
			sceneLight = new SceneSpotLight( World, kv.Position, data.FinalColor )
			{
				Rotation = kv.Rotation,
				ShadowsEnabled = data.CastShadows,
				ConeInner = data.InnerConeAngle,
				ConeOuter = data.OuterConeAngle,
				Radius = data.Range,
				FallOff = data.FallOff,
				ConstantAttenuation = data.Attenuation0,
				LinearAttenuation = data.Attenuation1,
				QuadraticAttenuation = data.Attenuation2 * 10000.0f,
				LightCookie = data.LightCookie,
			};

			sceneLight.Tags.Add( "light_spot" );
		}
		else if ( lightType == LightType.Omni )
		{
			sceneLight = new ScenePointLight( World, kv.Position, data.Range, data.FinalColor )
			{
				Rotation = kv.Rotation,
				ShadowsEnabled = data.CastShadows,
				Radius = data.Range,
				ConstantAttenuation = data.Attenuation0,
				LinearAttenuation = data.Attenuation1,
				QuadraticAttenuation = data.Attenuation2 * 10000.0f,
				LightCookie = data.LightCookie,
			};

			sceneLight.Tags.Add( "light_omni" );
		}
		else if ( lightType == LightType.Rect )
		{
			sceneLight = new SceneSpotLight( World, kv.Position, data.FinalColor )
			{
				Rotation = kv.Rotation,
				ShadowsEnabled = false, // Not yet
				Radius = data.Range,
				ConstantAttenuation = data.Attenuation0,
				LinearAttenuation = data.Attenuation1,
				QuadraticAttenuation = data.Attenuation2 * 10000.0f,
				ConeInner = 90,
				ConeOuter = 90,
				LightCookie = data.LightCookie,
				Shape = SceneLight.LightShape.Rectangle,
			};

			sceneLight.Tags.Add( "light_rect" );
		}
		else if ( lightType == LightType.Capsule )
		{
			sceneLight = new ScenePointLight( World, kv.Position, data.Range, data.FinalColor )
			{
				Rotation = kv.Rotation,
				ShadowsEnabled = false, // Not yet
				Radius = data.Range,
				ConstantAttenuation = data.Attenuation0,
				LinearAttenuation = data.Attenuation1,
				QuadraticAttenuation = data.Attenuation2 * 10000.0f,
				LightCookie = data.LightCookie,
				Shape = SceneLight.LightShape.Capsule,
			};

			sceneLight.Tags.Add( "light_capsule" );
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

		// Apply the advanced native settings shared with the component path.
		data.ToLegacyData().ApplyTo( light );

		if ( lightType == LightType.Rect )
		{
			light.SetLightShape( LightSourceShape_t.Rectangle );
			light.SetLightSourceDim0( data.LightSourceDim0 );
			light.SetLightSourceDim1( data.LightSourceDim1 );
		}
		else if ( lightType == LightType.Capsule )
		{
			light.SetLightShape( LightSourceShape_t.Capsule );
			light.SetLightSourceDim0( data.LightSourceDim0 );
			light.SetLightSourceDim1( data.LightSourceDim1 );
		}

		SceneObjects.Add( sceneLight );
	}

	public class TextSceneObject : SceneCustomObject
	{
		private readonly CommandList _commandList = new( "MapText" );

		public string Text { get; set; }
		public string FontName { get; set; } = "Roboto";
		public float FontSize { get; set; } = 100.0f;
		public float FontWeight { get; set; } = 800.0f;
		public TextFlag TextFlags { get; set; } = TextFlag.DontClip;

		public TextSceneObject( SceneWorld sceneWorld ) : base( sceneWorld )
		{
			RenderLayer = SceneRenderLayer.Default;
		}

		internal void BuildCommandList()
		{
			_commandList.Reset();
			_commandList.Attributes.SetCombo( "D_WORLDPANEL", 1 );
			var scope = new TextRendering.Scope( Text, ColorTint, FontSize, FontName, (int)FontWeight );
			_commandList.DrawText( scope, new Rect( 0 ), TextFlags );
		}

		public override void RenderSceneObject()
		{
			_commandList.ExecuteOnRenderThread();
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

		textObject.BuildCommandList();
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
