using System;
using LightType = Sandbox.SceneMapLoader.LightType;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	internal static MapLoader.ObjectEntry CreateEntityEntry( Dictionary<string, string> keys, Transform transform )
	{
		// Hammer writes the combined probe's default as "8.0f". The compiled entity stores "8".
		if ( keys.TryGetValue( "cubemap_feathering", out var feathering ) && feathering?.EndsWith( 'f' ) == true &&
			float.TryParse( feathering.AsSpan( 0, feathering.Length - 1 ), System.Globalization.CultureInfo.InvariantCulture, out _ ) )
			keys["cubemap_feathering"] = feathering[..^1];

		var values = new Dictionary<uint, string>( keys.Count );
		foreach ( var (key, value) in keys )
			values.Add( StringToken.FindOrCreate( key ), value );

		return new( values )
		{
			Transform = transform,
			Position = transform.Position,
			Rotation = transform.Rotation,
			Angles = transform.Rotation.Angles(),
			Scales = transform.Scale
		};
	}

	// Parent names are local to each map or instance definition. Resolve them before
	// cloning so repeated doors, for example, each keep their own handles.
	static void ResolveEntityParents( SourceNode root, HashSet<SourceNode> templates )
	{
		var named = new Dictionary<string, SourceNode>( StringComparer.OrdinalIgnoreCase );
		var parents = new Dictionary<SourceNode, SourceNode>();
		var children = new List<SourceNode>();
		void Visit( SourceNode node )
		{
			if ( node.Kind == NodeKind.Entity )
			{
				if ( !string.IsNullOrEmpty( node.Entity.TargetName ) ) named.TryAdd( node.Entity.TargetName, node );
				if ( !string.IsNullOrWhiteSpace( node.Entity.ParentName ) && node != root ) children.Add( node );
			}
			foreach ( var child in node.Children )
			{
				if ( templates.Contains( child ) ) continue;
				parents.Add( child, node );
				Visit( child );
			}
		}
		Visit( root );

		var changed = new Dictionary<SourceNode, List<SourceNode>>();
		List<SourceNode> ChildrenOf( SourceNode node )
		{
			if ( !changed.TryGetValue( node, out var list ) ) changed[node] = list = node.Children.ToList();
			return list;
		}
		foreach ( var child in children )
		{
			if ( !named.TryGetValue( child.Entity.ParentName, out var target ) || target == parents[child] ) continue;
			var ancestor = target;
			while ( ancestor is not null && ancestor != child ) ancestor = parents.GetValueOrDefault( ancestor );
			if ( ancestor == child ) continue;
			ChildrenOf( parents[child] ).Remove( child );
			ChildrenOf( target ).Add( child );
			parents[child] = target;
		}
		foreach ( var (node, list) in changed ) node.Children = list.ToArray();
	}

	// Hammer world nodes and baked prop draw calls use GammaToLinearFullRange for RGB.
	internal static Color HammerRenderTint( Color tint ) => tint == Color.White ? tint :
		new Color( MathF.Pow( tint.r, 2.2f ), MathF.Pow( tint.g, 2.2f ), MathF.Pow( tint.b, 2.2f ), tint.a );

	internal static Color ReadPropTint( string color, string opacity )
	{
		// Hammer stores float channels in the 0â€“255 range. A separate renderamt overrides alpha.
		Span<float> channels = stackalloc float[] { 255, 255, 255, 255 };
		var text = color.AsSpan();
		int i = 0;
		foreach ( var range in text.SplitAny( " \t\r\n" ) )
		{
			if ( range.Start.Value == range.End.Value ) continue;
			if ( !float.TryParse( text[range], System.Globalization.CultureInfo.InvariantCulture, out var value ) ) break;
			channels[i++] = value;
			if ( i == channels.Length ) break;
		}
		if ( float.TryParse( opacity, System.Globalization.CultureInfo.InvariantCulture, out var alpha ) ) channels[3] = alpha;
		return new Color( channels[0] / 255f, channels[1] / 255f, channels[2] / 255f, channels[3] / 255f );
	}

	static bool ConvertEntity( MapLoader.ObjectEntry data, GameObject target, List<string> warnings, SourceNode source, bool applyingOverrides = false )
	{
		target.Name = string.IsNullOrWhiteSpace( data.TargetName ) ? data.TypeName : data.TargetName;
		target.Tags.Add( data.Tags );
		var enabled = true;
		switch ( data.TypeName )
		{
			case "ent_door": ConvertDoor( target, data, source, warnings ); break;
			case "ent_nav_blocker":
				enabled = data.GetValue( "enabled", true );
				break;
			case "info_cull_triangles":
				warnings.Add( "Triangle culling applies to directly placed static props in the same VMAP. World meshes and shared instance geometry retain their source triangles." );
				break;
			case "func_nav_markup":
			case "light_importance_volume":
			case "light_ortho":
				// Hammer's bake volumes are editor guides. Ortho lights were removed from the map renderer.
				warnings.Add( data.TypeName == "light_ortho"
					? $"Ortho light '{target.Name}' has no renderer in s&box."
					: $"Skipped Hammer bake settings on '{target.Name}'." );
				break;
			case "func_shatterglass":
				int constraint = data.GetValue<int>( "constraint" );
				if ( constraint is 1 or 2 )
				{
					var glassBody = target.Components.GetOrCreate<Rigidbody>();
					glassBody.StartAsleep = constraint == 2;
					glassBody.MassOverride = source.GlassMass;
				}
				else if ( applyingOverrides )
				{
					// A fixed placement can inherit a rigidbody from its movable prefab.
					target.Components.Get<Rigidbody>( FindMode.EverythingInSelf )?.Destroy();
				}
				warnings.Add( $"Glass '{target.Name}' has its intact pane and physics. Shattering on impact requires a game component." );
				break;
			case "func_physbox":
				var body = target.Components.GetOrCreate<Rigidbody>();
				int startAs = data.GetValue<int>( "start_as" );
				body.MotionEnabled = (startAs & 1) == 0;
				body.StartAsleep = (startAs & 2) != 0;
				break;
			case "func_brush":
				enabled = !data.GetValue<bool>( "startdisabled" );
				break;
			case "info_player_start":
				target.Components.GetOrCreate<SpawnPoint>();
				break;
			case "prop_static":
			case "prop_physics":
			case "prop_dynamic":
			case "prop_animated":
				ConvertProp( target, data, source, warnings );
				break;
			case "light_omni":
			case "light_rect":
			case "light_capsule":
			case "light_spot":
			case "light_environment":
			case "light_directional":
				ConvertLight( target, data, warnings );
				break;
			case "func_water": break; // The water surface takes its dimensions from the converted brush.
			case "post_processing_entity":
			case "post_processing_volume":
				enabled = data.GetValue( "enabled", true ) && !data.GetValue<bool>( "startdisabled" );
				break;
			case "point_worldtext": ConvertWorldText( target, data ); break;
			case "env_sky": SkyBox2D.InitializeFromLegacy( target, data ); break;
			case "env_cubemap":
			case "env_cubemap_box":
			case "env_light_probe_volume":
			case "env_combined_light_probe_volume":
				ConvertProbes( target, data, warnings );
				break;
			case "env_volumetric_fog_controller": VolumetricFogController.InitializeFromLegacy( target, data ); break;
			case "env_volumetric_fog_volume": VolumetricFogVolume.InitializeFromLegacy( target, data ); break;
#pragma warning disable CS0618 // Legacy Hammer fog entities.
			case "env_gradient_fog": ApplyGradientFog( target.Components.GetOrCreate<GradientFog>(), data ); break;
			case "env_cubemap_fog": ApplyCubemapFog( target.Components.GetOrCreate<CubemapFog>(), data ); break;
#pragma warning restore CS0618
			case "snd_event_point":
				ApplySoundEvent( target.Components.GetOrCreate<SoundPointComponent>(), data );
				if ( !string.IsNullOrWhiteSpace( data.GetString( "sourceEntityName" ) ) )
					warnings.Add( $"Point sound '{target.Name}' uses a separate playback origin. Attach the sound to '{data.GetString( "sourceEntityName" )}'." );
				break;
			case "snd_event_alignedbox":
				var bounds = new BBox( data.GetValue<Vector3>( "mins", -32 ), data.GetValue<Vector3>( "maxs", 32 ) );
				// The component is centered on its owner. Keep the entity's authored origin for its children and overrides.
				var origin = applyingOverrides ? target.Children.FirstOrDefault( child => child.Name == "Sound Origin" && child.Components.Get<SoundBoxComponent>().IsValid() ) : null;
				origin ??= new GameObject( target, true, "Sound Origin" );
				origin.LocalPosition = bounds.Center;
				var box = origin.Components.GetOrCreate<SoundBoxComponent>();
				box.Scale = bounds.Size;
				ApplySoundEvent( box, data );
				break;
			case "snd_soundscape": ApplySoundscape( target.Components.GetOrCreate<SoundscapeTrigger>(), data ); break;
			case "snd_soundscape_box": ApplySoundscapeBox( target.Components.GetOrCreate<SoundscapeTrigger>(), data ); break;
			case "sky_camera":
			case "skybox_reference":
				break;
			case "info_world_layer":
			case "commentary_dummy":
			case "phys_lengthconstraint":
			case "phys_hinge":
			case "info_target": break;
			default:
				warnings.Add( $"Entity '{target.Name}' ({data.TypeName}): imported its hierarchy and geometry. Its gameplay behavior requires a scene component." );
				break;
		}
		return enabled;
	}

	static void ApplySoundEvent( BaseSoundComponent sound, MapLoader.ObjectEntry data )
	{
		sound.SoundEvent = data.GetResource<SoundEvent>( "soundName" );
		sound.PlayOnStart = data.GetValue<bool>( "startOnSpawn" );
		sound.StopOnNew = data.GetValue<bool>( "stopOnNew" );
		sound.SoundOverride = data.GetValue<bool>( "overrideParams" );
		sound.Volume = data.GetValue( "soundVolume", 1f );
		sound.Pitch = data.GetValue( "soundPitch", 1f );
	}

	static void ApplySoundscapeBox( SoundscapeTrigger soundscape, MapLoader.ObjectEntry kv )
	{
		soundscape.Soundscape = GameResource.Load<Soundscape>( kv.GetString( "Soundscape" ) );
		soundscape.BoxSize = kv.GetValue<Vector3>( "Extents" ) * 0.5f;
		soundscape.Type = SoundscapeTrigger.TriggerType.Box;
		soundscape.Enabled = kv.GetValue<bool>( "Enabled" );
	}

	static void ApplySoundscape( SoundscapeTrigger soundscape, MapLoader.ObjectEntry kv )
	{
		soundscape.Soundscape = GameResource.Load<Soundscape>( kv.GetString( "Soundscape" ) );
		soundscape.Radius = kv.GetValue<float>( "radius" );
		soundscape.Type = SoundscapeTrigger.TriggerType.Sphere;
		soundscape.Enabled = kv.GetValue<bool>( "Enabled" );
	}

#pragma warning disable CS0618 // Legacy Hammer fog entities.
	static void ApplyGradientFog( GradientFog fog, MapLoader.ObjectEntry kv )
	{
		fog.Enabled = kv.GetValue<bool>( "fogenabled" );
		fog.Color = kv.GetValue<Color>( "fogcolor" ).WithAlpha( kv.GetValue<float>( "fogmaxopacity" ) );
		fog.StartDistance = kv.GetValue<float>( "FogStart" );
		fog.EndDistance = kv.GetValue<float>( "FogEnd" );
		fog.Height = kv.GetValue<float>( "fogendheight" );
		fog.FalloffExponent = kv.GetValue<float>( "fogfalloffexponent" );
		fog.VerticalFalloffExponent = kv.GetValue<float>( "fogverticalexponent" );
	}

	static void ApplyCubemapFog( CubemapFog fog, MapLoader.ObjectEntry kv )
	{
		fog.Sky = kv.GetResource<Material>( "cubemapfogmaterial" );
		fog.Blur = kv.GetValue<float>( "cubemapfoglodbiase" );
		fog.StartDistance = kv.GetValue<float>( "cubemapfogstartdistance" );
		fog.EndDistance = kv.GetValue<float>( "cubemapfogenddistance" );
		fog.FalloffExponent = kv.GetValue<float>( "cubemapfogfalloffexponent" );
		fog.HeightExponent = kv.GetValue<float>( "cubemapfogheightexponent" );
		fog.HeightStart = kv.GetValue<float>( "cubemapfogheightstart" );
		fog.HeightWidth = kv.GetValue<float>( "cubemapfogheightwidth" );
	}

#pragma warning restore CS0618

	static void ConvertWater( GameObject target, MapLoader.ObjectEntry data, List<string> warnings )
	{
		var brushes = target.Components.GetAll<MeshComponent>( FindMode.EverythingInSelfAndDescendants ).ToArray();
		if ( brushes.Length == 0 ) warnings.Add( $"Water '{target.Name}' has no brush bounds." );
		foreach ( var brush in brushes )
		{
			brush.Collision = MeshComponent.CollisionType.None;
			brush.HideInGame = true;
			var water = brush.Components.GetOrCreate<WaterBody>();
			water.ReflectionEnvironment = WaterBody.EnvironmentMode.SceneProbes;
			water.Underwater = data.GetValue( "enablefog", true );
			if ( data.GetString( "watermaterial" ) is { Length: > 0 } path )
			{
				water.Material = Material.Load( path );
				if ( water.ActiveMaterial != water.Material || water.Material is null || water.Material.IsError )
					warnings.Add( $"Water material '{path}' requires conversion to the scene water shader." );
			}
		}
	}

	static void ConvertWorldText( GameObject target, MapLoader.ObjectEntry data )
	{
		// The renderer has a depth offset, while Hammer keeps the entity pivot at its authored origin.
		var owner = target.Children.FirstOrDefault( c => c.Name == "Text" && c.Components.Get<TextRenderer>( FindMode.EverythingInSelf ) is not null )
			?? new GameObject( target, true, "Text" );
		owner.WorldPosition = target.WorldPosition + target.WorldRotation.Up * data.GetValue<float>( "depth_render_offset" );
		owner.WorldRotation = target.WorldRotation;
		owner.WorldScale = Vector3.One;
		owner.Tags.Add( target.Tags );
		owner.Tags.Add( "world_text" );
		var text = owner.Components.GetOrCreate<TextRenderer>();
		text.Text = data.GetString( "message" );
		text.FontFamily = data.GetString( "font_name" );
		text.FontSize = data.GetValue<float>( "font_size" ).Clamp( 1, 256 );
		text.Scale = data.GetValue<float>( "world_units_per_pixel" ) * 0.75f;
		text.Color = data.GetValue<Color>( "color" );
		text.HorizontalAlignment = data.GetValue<int>( "justify_horizontal" ) switch
		{
			1 => TextRenderer.HAlignment.Center,
			2 => TextRenderer.HAlignment.Right,
			_ => TextRenderer.HAlignment.Left
		};
		text.VerticalAlignment = data.GetValue<int>( "justify_vertical" ) switch
		{
			1 => TextRenderer.VAlignment.Center,
			2 => TextRenderer.VAlignment.Top,
			_ => TextRenderer.VAlignment.Bottom
		};
	}

	internal static void ConvertProbes( GameObject target, MapLoader.ObjectEntry entry, List<string> warnings )
	{
		var bounds = new BBox( entry.GetValue( "box_mins", new Vector3( -72 ) ), entry.GetValue( "box_maxs", new Vector3( 72 ) ) );
		var priority = entry.GetValue<int>( "indoor_outdoor_level" );
		if ( entry.TypeName != "env_light_probe_volume" )
		{
			var probe = target.Components.GetOrCreate<EnvmapProbe>();
			probe.Bounds = bounds;
			probe.Priority = priority;
			probe.Feathering = entry.GetValue( "cubemap_feathering", 0.25f ) * 8;
			probe.Projection = entry.TypeName == "env_cubemap" ? SceneCubemap.ProjectionMode.Sphere : SceneCubemap.ProjectionMode.Box;
			probe.UpdateStrategy = EnvmapProbe.CubemapDynamicUpdate.OnEnabled;
			probe.ZNear = entry.GetValue( "bakenearz", 2f );
			probe.ZFar = entry.GetValue( "bakefarz", 4096f );
			var custom = entry.GetValue<bool>( "customcubemaptexture" );
			probe.Texture = custom ? entry.GetResource<Texture>( "cubemaptexture" ) : null;
			probe.Mode = custom ? EnvmapProbe.EnvmapProbeMode.CustomTexture : EnvmapProbe.EnvmapProbeMode.Realtime;
			if ( custom && probe.Texture is not { IsError: false } )
				warnings.Add( $"Could not load custom cubemap '{entry.GetString( "cubemaptexture" )}'." );
		}
		if ( entry.TypeName is "env_light_probe_volume" or "env_combined_light_probe_volume" )
		{
			var volume = target.Components.GetOrCreate<LightProbeVolume>();
			volume.Bounds = bounds;
			volume.Priority = priority;
			volume.Handshake = entry.GetValue<int>( "handshake" );
			volume.IndicesTexture = entry.GetResource<Texture>( "lightprobetexture_dli" );
			volume.ScalarsTexture = entry.GetResource<Texture>( "lightprobetexture_dls" );
			volume.Texture = entry.GetResource<Texture>( "lightprobetexture" );
			if ( volume.Texture is not { IsError: false } )
				warnings.Add( "Light probe volumes without an ambient texture preserve their bounds but provide no baked lighting." );
		}
	}

	internal static void ConvertLight( GameObject target, MapLoader.ObjectEntry entry, List<string> warnings )
	{
		var type = entry.TypeName switch
		{
			"light_omni" => LightType.Omni,
			"light_spot" => LightType.Spot,
			"light_rect" => LightType.Rect,
			"light_capsule" => LightType.Capsule,
			_ => LightType.Directional
		};
		var data = SceneMapLoader.LightData.Parse( entry, type );
		Light light;
		if ( type is LightType.Omni or LightType.Capsule )
		{
			var point = target.Components.GetOrCreate<PointLight>();
			point.Radius = data.Range;
			point.ConstantAttenuation = data.Attenuation0;
			point.LinearAttenuation = data.Attenuation1;
			point.Attenuation = data.Attenuation2 * 10000f;
			light = point;
		}
		else if ( type is LightType.Spot or LightType.Rect )
		{
			var spot = target.Components.GetOrCreate<SpotLight>();
			spot.Radius = data.Range;
			spot.ConeInner = type == LightType.Rect ? 90 : MathF.Min( data.InnerConeAngle, 80f );
			spot.ConeOuter = type == LightType.Rect ? 90 : MathF.Min( data.OuterConeAngle, 80f );
			spot.Cookie = data.LightCookie;
			spot.Attenuation = data.Attenuation2 * 10000f;
			light = spot;
		}
		else
		{
			var sun = target.Components.GetOrCreate<DirectionalLight>();
			var skyColor = entry.GetValue( "SkyColor", Color.White );
			sun.SkyColor = (skyColor * entry.GetValue( "SkyIntensity", 1f )).WithAlpha( skyColor.a );
			light = sun;
		}
		if ( type is LightType.Rect or LightType.Capsule )
			warnings.Add( $"Imported {entry.TypeName} '{target.Name}' as a {light.GetType().Name}. Its emitter shape has no scene component." );
		else
			light.ShadowHardness = 0.5f;
		light.Enabled = data.Enabled;
		light.MixedShadows = data.DirectLight == 3;
		light.BakeLightIndex = data.BakedLightIndexing && data.DirectLight is 1 or 3 && data.BakeLightIndex is >= 0 and < 255
			? data.BakeLightIndex : -1;
		// Hammer multiplies the indexed light's RGB by this scale when packing it for rendering.
		light.Intensity = light.BakeLightIndex >= 0 ? data.BakeLightIndexScale : 1f;
		light.LightColor = data.FinalColor;
		light.Shadows = type is not (LightType.Rect or LightType.Capsule) && data.CastShadows;
		light.FogMode = (Light.FogInfluence)data.FogLighting;
		light.Contribution = Light.LightContribution.Transmissive |
			(data.RenderDiffuse ? Light.LightContribution.Diffuse : 0) |
			(data.RenderSpecular ? Light.LightContribution.Specular : 0);
		light.FogStrength = data.FogContributionStrength;
	}
}
