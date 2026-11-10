using System;
using System.Globalization;
using System.IO;
using Sandbox.Volumes;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	internal static PostProcessVolume CreatePostProcessVolume( GameObject world, string name, SceneVolume shape, Vector3 origin = default, Rotation? rotation = null, int priority = 0 )
	{
		var instance = new GameObject( false, name )
		{
			Parent = world,
			IsStatic = true,
			LocalPosition = origin,
			LocalRotation = rotation ?? Rotation.Identity
		};
		var volume = instance.AddComponent<PostProcessVolume>();
		volume.SceneVolume = shape;
		volume.Priority = priority;
		volume.BlendDistance = 0;
		return volume;
	}

	internal static void ConfigureTonemapping( PostProcessVolume volume, SourcePostProcessing.TonemapSettings settings, bool hdr, float rate )
	{
		var tonemapping = volume.GameObject.AddComponent<Tonemapping>();
		tonemapping.Mode = Tonemapping.TonemappingMode.Linear;
		tonemapping.AutoExposureEnabled = hdr;
		tonemapping.MinimumExposure = settings.MinimumExposure;
		tonemapping.MaximumExposure = Math.Max( settings.MinimumExposure, settings.MaximumExposure );
		tonemapping.ExposureCompensation = 0;
		tonemapping.Rate = rate;
		var bloom = volume.GameObject.AddComponent<Bloom>();
		bloom.Mode = SceneCamera.BloomAccessor.BloomMode.Additive;
		bloom.Strength = hdr ? settings.BloomScale : 0;
		bloom.Threshold = 0;
	}

	private sealed partial class EntityBuilder
	{
		private SourcePostProcessing postProcessing;

		private void BuildPostProcessing()
		{
			postProcessing = SourcePostProcessing.Read( bsp.Entities );
			warnings.UnionWith( postProcessing.Warnings );
			var volume = CreatePostProcessVolume( World, "source post processing", new SceneVolume { Type = SceneVolume.VolumeTypes.Infinite } );
			ConfigureTonemapping( volume, postProcessing.Global, bsp.Hdr, postProcessing.Rate );
			var globalCorrections = postProcessing.Corrections.Values.Where( correction => correction.IsGlobal && correction.Enabled && correction.Weight > 0 ).ToArray();
			if ( globalCorrections.Length > 0 )
			{
				try
				{
					AddColorGrading( volume, $"mount://{owner.Host.Ident}/{owner.FullPath}_grading.vtex", 1 );
				}
				catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
				{
					warnings.Add( $"Global color correction was not imported: {e.Message}" );
				}
			}
			volume.GameObject.Enabled = true;

			foreach ( var entity in bsp.Entities.Where( entity => string.Equals( entity.Get( "classname" ), "trigger_tonemap", StringComparison.OrdinalIgnoreCase ) ) )
			{
				if ( entity.Integer( "StartDisabled" ) != 0 )
					continue;
				var controller = postProcessing.Controllers.Keys.FirstOrDefault( controller => string.Equals( controller.Get( "targetname" ), entity.Get( "TonemapName" ), StringComparison.OrdinalIgnoreCase ) );
				if ( controller is null )
				{
					warnings.Add( $"Tonemap trigger '{entity.Get( "targetname" )}' has no imported controller '{entity.Get( "TonemapName" )}'; omitted." );
					continue;
				}
				var local = CreateBrushPostProcessVolume( entity );
				ConfigureTonemapping( local, postProcessing.Controllers[controller], bsp.Hdr, postProcessing.Rate );
				local.GameObject.Enabled = true;
			}

			foreach ( var correction in postProcessing.Corrections.Values.Where( correction => !correction.IsGlobal && correction.Enabled && correction.Weight > 0 ) )
			{
				PostProcessVolume local = null;
				try
				{
					var entity = correction.Entity;
					if ( string.Equals( entity.Get( "classname" ), "color_correction_volume", StringComparison.OrdinalIgnoreCase ) )
						local = CreateBrushPostProcessVolume( entity );
					else
					{
						var inner = entity.Number( "minfalloff" );
						var outer = entity.Number( "maxfalloff", 1000 );
						if ( inner < 0 || outer <= inner )
							throw new NotSupportedException( "Radial color correction requires 0 <= minfalloff < maxfalloff." );
						local = CreatePostProcessVolume( World, entity.Get( "targetname", "color correction" ),
							new SceneVolume { Type = SceneVolume.VolumeTypes.Sphere, Sphere = new Sphere( Vector3.Zero, outer ) },
							entity.Vector( "origin" ), priority: 1 );
						local.BlendDistance = outer - inner;
					}
					var path = owner.Host.GetContentPath( entity.Get( "filename", "" ).Replace( '\\', '/' ), owner.ContentRoot )
						?? throw new FileNotFoundException( $"Color-correction lookup '{entity.Get( "filename" )}' was not found in mounted content." );
					AddColorGrading( local, $"{path}.vtex", correction.Weight );
					local.GameObject.Enabled = true;
				}
				catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
				{
					local?.GameObject.Destroy();
					warnings.Add( $"Color correction '{correction.Entity.Get( "targetname" )}' was not imported: {e.Message}" );
				}
			}
			if ( postProcessing.Corrections.Count > 0 )
				warnings.Add( "Color correction uses native 16-sample LUTs resampled from Source's 32-sample sRGB tables; overlapping local LUTs use native volume selection rather than Source's additive lookup blending." );
		}

		private PostProcessVolume CreateBrushPostProcessVolume( MapEntity entity )
		{
			var path = entity.Get( "model", "" );
			if ( !path.StartsWith( '*' ) || !int.TryParse( path.AsSpan( 1 ), NumberStyles.None, CultureInfo.InvariantCulture, out var index ) || index <= 0 || index >= bsp.Models.Length )
				throw new InvalidDataException( $"{owner.FullPath}: post-processing volume has invalid brush model '{path}'." );
			var bounds = bsp.Models[index];
			warnings.Add( "Brush post-processing volumes use native box bounds and camera position, not exact Source trigger solids/player-origin tests or temporal entry/exit fades." );
			return CreatePostProcessVolume( World, entity.Get( "targetname", entity.Get( "classname" ) ),
				new SceneVolume { Type = SceneVolume.VolumeTypes.Box, Box = new BBox( bounds.Mins, bounds.Maxs ) },
				entity.Vector( "origin" ), Rotation.From( entity.Angles ), priority: 1 );
		}

		private static void AddColorGrading( PostProcessVolume volume, string path, float weight )
		{
			var texture = Texture.Load( path, false );
			if ( texture is null || !texture.IsValid || texture.IsError )
				throw new InvalidDataException( $"Color-correction texture '{path}' could not be loaded." );
			var grading = volume.GameObject.AddComponent<ColorGrading>();
			grading.GradingMethod = ColorGrading.GradingType.LUT;
			grading.LookupTexture = texture;
			grading.BlendFactor = weight;
		}
	}
}
