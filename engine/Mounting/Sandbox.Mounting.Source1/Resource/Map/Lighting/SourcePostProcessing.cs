using System;
using System.Globalization;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed class SourcePostProcessing
{
	internal sealed class TonemapSettings
	{
		public float MinimumExposure { get; set; } = 0.5f;
		public float MaximumExposure { get; set; } = 2;
		public float BloomScale { get; set; } = 1;
	}

	internal sealed class ColorCorrectionSettings
	{
		internal ColorCorrectionSettings( MapEntity entity )
		{
			Entity = entity;
			Enabled = entity.Integer( "StartDisabled" ) == 0 && !IsClientSelected;
			Weight = entity.Get( "classname" ).Equals( "color_correction_volume", StringComparison.OrdinalIgnoreCase )
				? Math.Max( 0, entity.Number( "maxweight", 1 ) ) : 1;
		}

		public MapEntity Entity { get; }
		public bool Enabled { get; set; }
		public float Weight { get; set; }
		public bool IsClientSelected => Entity.Get( "classname" ).Equals( "color_correction", StringComparison.OrdinalIgnoreCase )
			&& (Entity.Integer( "spawnflags" ) & 2) != 0;
		public bool IsGlobal => Entity.Get( "classname" ).Equals( "color_correction", StringComparison.OrdinalIgnoreCase )
			&& (Entity.Number( "minfalloff" ) == -1 || Entity.Number( "maxfalloff", 1000 ) == -1
				|| Entity.Number( "minfalloff" ) == Entity.Number( "maxfalloff", 1000 ));
	}

	public Dictionary<MapEntity, TonemapSettings> Controllers { get; } = [];
	public Dictionary<MapEntity, ColorCorrectionSettings> Corrections { get; } = [];
	public HashSet<(MapEntity Entity, string Output, string Value)> HandledOutputs { get; } = [];
	public HashSet<string> Warnings { get; } = new( StringComparer.Ordinal );
	public MapEntity Master { get; private set; }
	public float Rate { get; private set; } = 1;
	public TonemapSettings Global => Master is null ? new() : Controllers[Master];

	internal static SourcePostProcessing Read( IReadOnlyList<MapEntity> entities )
	{
		var result = new SourcePostProcessing();
		foreach ( var entity in entities )
		{
			switch ( entity.Get( "classname", "" ).ToLowerInvariant() )
			{
				case "env_tonemap_controller":
					result.Controllers.Add( entity, new() );
					if ( result.Master is null || (entity.Integer( "spawnflags" ) & 1) != 0 )
						result.Master = entity;
					break;
				case "color_correction":
				case "color_correction_volume":
					result.Corrections.Add( entity, new( entity ) );
					break;
			}
		}

		var inputs = new List<(MapEntity Source, string Output, string Value, BspEntityConnection Connection)>();
		foreach ( var source in entities.Where( entity => string.Equals( entity.Get( "classname" ), "logic_auto", StringComparison.OrdinalIgnoreCase ) ) )
		{
			if ( !string.IsNullOrEmpty( source.Get( "globalstate" ) ) )
				continue;
			foreach ( var output in new[] { "OnNewGame", "OnMapSpawn", "OnMultiNewMap" } )
				foreach ( var (key, value) in source.KeyValues.Where( pair => pair.Key.Equals( output, StringComparison.OrdinalIgnoreCase ) ) )
					inputs.Add( (source, key, value, BspEntityConnection.Read( key, value )) );
		}

		foreach ( var (source, output, value, connection) in inputs.OrderBy( input => input.Connection.Delay ) )
		{
			var targets = BspEntityConnection.ResolveTargets( source, connection.Target, entities ).ToArray();
			var handled = 0;
			foreach ( var target in targets )
			{
				if ( !result.Apply( target, connection.Input, connection.Parameter ) )
					continue;
				handled++;
				if ( connection.Delay > 0 )
					result.Warnings.Add( "Post-processing startup delays are baked into the initial look; timed transitions are not simulated." );
			}
			if ( handled > 0 && handled == targets.Length )
				result.HandledOutputs.Add( (source, output, value) );
		}
		return result;
	}

	private bool Apply( MapEntity target, string input, string parameter )
	{
		if ( Controllers.TryGetValue( target, out var controller ) )
		{
			switch ( input.ToLowerInvariant() )
			{
				case "setautoexposuremin":
					controller.MinimumExposure = Number( target, input, parameter );
					if ( controller.MinimumExposure <= 0 ) controller.MinimumExposure = 0.5f;
					return true;
				case "setautoexposuremax":
					controller.MaximumExposure = Number( target, input, parameter );
					if ( controller.MaximumExposure <= 0 ) controller.MaximumExposure = 2;
					return true;
				case "usedefaultautoexposure":
					controller.MinimumExposure = 0.5f;
					controller.MaximumExposure = 2;
					return true;
				case "setbloomscale":
					controller.BloomScale = Math.Max( 0, Number( target, input, parameter ) );
					return true;
				case "usedefaultbloomscale":
					controller.BloomScale = 1;
					return true;
				case "settonemaprate":
					Rate = Math.Max( 0, Number( target, input, parameter ) );
					return true;
			}
		}
		if ( Corrections.TryGetValue( target, out var correction ) && (input.Equals( "Enable", StringComparison.OrdinalIgnoreCase )
			|| input.Equals( "Disable", StringComparison.OrdinalIgnoreCase )) )
		{
			if ( correction.IsClientSelected )
				return false;
			correction.Enabled = input.Equals( "Enable", StringComparison.OrdinalIgnoreCase );
			correction.Weight = correction.Enabled ? Math.Max( 0, target.Number( "maxweight", 1 ) ) : 0;
			if ( target.Number( "fadeInDuration" ) > 0 || target.Number( "fadeOutDuration" ) > 0 || target.Number( "fadeDuration" ) > 0 )
				Warnings.Add( "Color-correction startup fades are baked into the initial look; temporal fades are not simulated." );
			return true;
		}
		return false;
	}

	private static float Number( MapEntity entity, string input, string value )
	{
		if ( float.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number ) && float.IsFinite( number ) )
			return number;
		throw new InvalidDataException( $"Post-processing input '{input}' on '{entity.Get( "targetname", "<unnamed>" )}' requires a finite number, not '{value}'." );
	}
}
