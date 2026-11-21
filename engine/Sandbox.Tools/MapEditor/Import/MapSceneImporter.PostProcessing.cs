using System;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using Sandbox.Volumes;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	sealed partial class VmapReader
	{
		(JsonObject Effect, string Warning) ReadPostProcessing( string filename, string asset )
		{
			var local = LocalReferencePath( filename, asset );
			var key = local ?? asset;
			if ( postProcessing.TryGetValue( key, out var cached ) ) return cached;

			try
			{
				var text = local is null ? FileSystem.Content.ReadAllText( asset ) : File.ReadAllText( local );
				if ( text is null ) cached.Warning = $"Post-processing source '{asset}' was not found.";
				else if ( new Kv3Text( text ).Read() is JsonObject effect && effect["m_layers"] is JsonArray )
					cached.Effect = effect;
				else cached.Warning = $"Post-processing source '{asset}' has no layer array.";
			}
			catch ( Exception e ) when ( e is InvalidDataException or System.Text.Json.JsonException )
			{
				cached.Warning = $"Could not read '{asset}': {e.Message}";
			}
			return postProcessing[key] = cached;
		}
	}

	static void ConvertPostProcessing( GameObject target, MapLoader.ObjectEntry data, JsonObject effect, Conversion conversion )
	{
		var warnings = conversion.Warnings;
		string asset = data.GetString( "postprocessing" );
		JsonObject bloom = null;
		bool filmic = false;
		Texture grading = null;
		if ( effect?["m_layers"] is JsonArray layers )
		{
			grading = conversion.GetGradingTexture( layers );
			foreach ( var layer in layers.OfType<JsonObject>() )
			{
				if ( layer["m_bVisible"]?.ToString() == "false" || Number( layer, "m_nOpacityPercent", 100 ) == 0 ) continue;
				string type = (string)layer["_class"];
				if ( IsGradingLayer( layer ) ) continue;
				var parameters = layer["m_pLayerMask"] is null && Number( layer, "m_nOpacityPercent", 100 ) == 100 ? layer["m_params"] as JsonObject : null;
				if ( type == "CBloomLayer" && bloom is null && parameters is not null )
				{
					bloom = parameters;
					warnings.Add( $"'{asset}': imported bloom mode, strength and threshold. Hammer's per-mip filtering and skybox bloom use a different renderer." );
				}
				else if ( type == "CToneMappingLayer" && !filmic && parameters is not null )
				{
					filmic = true;
					warnings.Add( $"'{asset}': using the scene's built-in filmic curve. The original curve parameters remain in the referenced post-processing asset." );
				}
				else warnings.Add( $"'{asset}': post-processing layer '{(string)layer["m_name"]}' ({type}) requires conversion." );
			}
		}

		var brushes = target.Components.GetAll<MeshComponent>( FindMode.EverythingInSelfAndDescendants ).ToArray();
		bool global = data.TypeName == "post_processing_entity";
		foreach ( var brush in brushes )
		{
			brush.HideInGame = true;
			brush.Collision = MeshComponent.CollisionType.None;
			if ( !global ) AddVolume( brush.GameObject, new() { Box = brush.Mesh.CalculateBounds() } );
		}
		if ( global ) AddVolume( target, new() { Type = SceneVolume.VolumeTypes.Infinite } );
		else if ( brushes.Length == 0 ) warnings.Add( $"Post-processing volume '{target.Name}' has no brush bounds." );

		void AddVolume( GameObject obj, SceneVolume shape )
		{
			var volume = obj.Components.GetOrCreate<PostProcessVolume>();
			volume.SceneVolume = shape;
			volume.BlendDistance = 0;
			volume.Priority = global ? 0 : 1;
			if ( grading is not null )
			{
				var component = obj.Components.GetOrCreate<ColorGrading>();
				component.GradingMethod = ColorGrading.GradingType.LUT;
				component.LookupTexture = grading;
			}
			if ( bloom is not null )
			{
				var component = obj.Components.GetOrCreate<Bloom>();
				component.Mode = (string)bloom["m_blendMode"] switch
				{
					"BLOOM_BLEND_SCREEN" => SceneCamera.BloomAccessor.BloomMode.Screen,
					"BLOOM_BLEND_BLUR" => SceneCamera.BloomAccessor.BloomMode.Blur,
					_ => SceneCamera.BloomAccessor.BloomMode.Additive
				};
				component.Strength = Number( bloom, component.Mode switch
				{
					SceneCamera.BloomAccessor.BloomMode.Screen => "m_flScreenBloomStrength",
					SceneCamera.BloomAccessor.BloomMode.Blur => "m_flBlurBloomStrength",
					_ => "m_flBloomStrength"
				}, component.Mode == SceneCamera.BloomAccessor.BloomMode.Additive ? 2 : 1 );
				component.Threshold = Number( bloom, "m_flBloomThreshold", 0 );
			}
			bool exposure = data.GetValue<bool>( "enableexposure" );
			if ( !exposure && !filmic ) return;
			var tone = obj.Components.GetOrCreate<Tonemapping>();
			tone.AutoExposureEnabled = exposure;
			if ( !exposure ) return;
			float Exposure( string linear, string log )
			{
				float value = data.GetValue<float>( linear );
				return value > 0 ? value : MathF.Pow( 2, data.GetValue<float>( log ) );
			}
			tone.MinimumExposure = Exposure( "minexposure", "minlogexposure" );
			tone.MaximumExposure = Exposure( "maxexposure", "maxlogexposure" );
			tone.ExposureCompensation = data.GetValue<float>( "exposurecompensation" );
			tone.Rate = data.GetValue( "exposurespeedup", 1f );
			if ( data.GetValue( "exposurespeeddown", 1f ) != 1f )
				warnings.Add( $"Exposure on '{target.Name}' uses a separate downward adaptation rate." );
		}

	}

	static float Number( JsonObject obj, string key, float fallback ) =>
		float.TryParse( obj[key]?.ToString(), CultureInfo.InvariantCulture, out var value ) ? value : fallback;
}
