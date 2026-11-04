using System;
using System.Collections.Generic;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed class SourceSoundscapes
{
	internal sealed record Entry( SourceSoundDefinition Sound, bool Random, (float Min, float Max) Time, bool Ambient );
	internal sealed record Definition( IReadOnlyList<Entry> Entries, IReadOnlyList<string> Diagnostics );
	private readonly Dictionary<string, (VmtNode Node, string Path)> definitions = new( StringComparer.OrdinalIgnoreCase );
	internal IEnumerable<string> Names => definitions.Keys;

	internal SourceSoundscapes()
	{
	}

	internal void Add( string text, string path )
	{
		foreach ( var node in VmtFile.ParseKeyValues( text, path, diagnostic => throw new NotSupportedException( $"{path}:{diagnostic.Line}: {diagnostic.Message}" ) ) )
		{
			if ( !node.IsBlock )
				throw new InvalidDataException( $"{path}: soundscape '{node.Name}' must be a block." );
			definitions[node.Name] = (node, path);
		}
	}

	internal Definition Read( string name )
	{
		var entries = new List<Entry>();
		var diagnostics = new HashSet<string>( StringComparer.Ordinal );
		Expand( name, 1, 0, 0, -1, -1, false );
		return new( entries.AsReadOnly(), diagnostics.ToArray() );
		void Expand( string current, float masterVolume, int depth, int startingPosition, int positionOverride, int ambientOverride, bool forceOrigin )
		{
			if ( depth > 8 )
				throw new InvalidDataException( $"Soundscape '{name}' exceeds Source's eight nested soundscapes." );
			if ( !definitions.TryGetValue( current, out var definition ) )
				throw new FileNotFoundException( $"Soundscape '{name}' references missing soundscape '{current}'." );
			var path = $"{definition.Path}: {current}";
			foreach ( var command in definition.Node.Children )
			{
				var type = command.Name.ToLowerInvariant();
				if ( type is "dsp" or "dsp_player" or "dsp_volume" or "soundmixer" or "fadetime" )
				{
					if ( depth == 0 )
						diagnostics.Add( $"{path}: {command.Name} is not represented by native soundscapes." );
					continue;
				}

				if ( !command.IsBlock )
					throw new InvalidDataException( $"{path}: unsupported soundscape command '{command.Name}'." );
				var parameters = new VmtParameters( command.Values, path );
				if ( type == "playsoundscape" )
				{
					var volume = SourceSoundDefinition.Range( parameters.String( "volume", "1" ), path );
					if ( volume.Min < 0 )
						throw new InvalidDataException( $"{path}: nested soundscape volume cannot be negative." );
					if ( volume.Min != volume.Max )
						diagnostics.Add( $"{path}: nested volume ranges are sampled at import, not each soundscape activation." );
					var multiplier = volume.Min == volume.Max ? volume.Min : Random.Shared.Float( volume.Min, volume.Max );
					if ( multiplier < 0 || !float.IsFinite( masterVolume * multiplier ) )
						throw new InvalidDataException( $"{path}: invalid nested soundscape volume." );
					var nextPosition = positionOverride;
					var nextAmbient = ambientOverride;
					if ( positionOverride < 0 && parameters.Contains( "positionoverride" ) )
					{
						nextPosition = checked(startingPosition + parameters.Integer( "positionoverride" ));
						nextAmbient = nextPosition;
					}

					if ( ambientOverride < 0 && parameters.Contains( "ambientpositionoverride" ) )
						nextAmbient = checked(startingPosition + parameters.Integer( "ambientpositionoverride" ));
					Expand( parameters.String( "name" ) ?? throw new InvalidDataException( $"{path}: playsoundscape requires a name." ), masterVolume * multiplier, depth + 1, checked(startingPosition + parameters.Integer( "position" )), nextPosition, nextAmbient, forceOrigin || parameters.Contains( "ambientoriginoverride" ) );
					continue;
				}

				if ( type is not ("playlooping" or "playrandom") )
					throw new NotSupportedException( $"{path}: unsupported soundscape command '{command.Name}'." );
				var random = type == "playrandom";
				var position = parameters.String( "position" );
				var randomPosition = string.Equals( position, "random", StringComparison.OrdinalIgnoreCase );
				var index = position is null || randomPosition ? -1 : checked(startingPosition + parameters.Integer( "position" ));
				if ( index < 0 )
					index = ambientOverride;
				else if ( positionOverride >= 0 )
				{
					index = positionOverride;
					if ( random )
						randomPosition = false;
				}

				var textOrigin = parameters.Contains( "origin" );
				if ( forceOrigin && index < 0 )
				{
					textOrigin = true;
					if ( random )
						randomPosition = false;
				}

				if ( (!randomPosition && (index >= 0 || textOrigin)) || (!random && randomPosition) )
				{
					diagnostics.Add( $"{path}: positioned soundscape sounds are omitted; native entries cannot retain authored world positions." );
					continue;
				}

				var volumeRange = SourceSoundDefinition.Range( parameters.String( "volume", "0" ), path );
				if ( volumeRange.Min < 0 || !float.IsFinite( volumeRange.Max * masterVolume ) )
					throw new InvalidDataException( $"{path}: invalid soundscape volume." );
				if ( volumeRange.Max * masterVolume == 0 )
					continue;
				var sound = SourceSoundDefinition.Parse( command, path );
				sound = sound with
				{
					Volume = (volumeRange.Min * masterVolume, volumeRange.Max * masterVolume)
				};
				if ( random && !parameters.Contains( "soundlevel" ) && !parameters.Contains( "attenuation" ) )
					sound = sound with
					{
						SoundLevel = 0
					};
				var time = random ? SourceSoundDefinition.Range( parameters.String( "time", "0" ), path ) : default;
				if ( time.Min < 0 )
					throw new InvalidDataException( $"{path}: random sound time cannot be negative." );
				if ( !random && sound.Pitch != (1f, 1f) )
					diagnostics.Add( $"{path}: native soundscape loops do not support authored pitch; using normal pitch." );
				if ( parameters.Contains( "radius" ) || parameters.Contains( "suppress_on_restore" ) )
					diagnostics.Add( $"{path}: loop radii and Source save/restore suppression are not represented." );
				entries.Add( new( sound, random, time, !randomPosition ) );
			}
		}
	}
}
