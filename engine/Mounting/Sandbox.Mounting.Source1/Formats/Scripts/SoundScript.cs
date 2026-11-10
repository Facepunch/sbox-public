using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed record SourceSoundDefinition( string Name, string SourcePath, IReadOnlyList<string> Waves,
	(float Min, float Max) Volume, (float Min, float Max) Pitch, float SoundLevel, bool CompatibilityAttenuation, bool Dry )
{
	internal float Gain( float distance )
	{
		if ( SoundLevel == 0 ) return 1;
		if ( CompatibilityAttenuation )
			return Math.Clamp( 1 - distance * (SoundLevel > 50 ? 20 / (SoundLevel - 50) : 4) / 1000, 0, 1 );
		var relative = distance * MathF.Pow( 10, (60 - SoundLevel + 4 * distance / 1200) / 20 ) / 36;
		var gain = relative > 0.1f ? 1 / relative : 10;
		if ( gain > 0.5f )
		{
			var power = SoundLevel > 90 ? 2.5f + (SoundLevel - 90) * (0.8f - 2.5f) / 50 : 2.5f;
			gain = 1 - 0.5f * MathF.Pow( 0.5f / gain, power );
		}
		if ( gain < 0.01f )
			gain = Math.Max( 0.001f, 0.01f * (2 - relative * 0.01f) );
		return Math.Clamp( gain, 0, 1 );
	}

	internal float AudibleDistance()
	{
		if ( SoundLevel == 0 ) return 0;
		float max = 128;
		while ( Gain( max ) > 0.001f ) max *= 2;
		float min = 0;
		for ( int i = 0; i < 24; i++ )
		{
			var middle = (min + max) * 0.5f;
			if ( Gain( middle ) > 0.001f ) min = middle;
			else max = middle;
		}
		return max;
	}

	internal (float Time, float Value)[] SampleFalloff( float distance )
	{
		var frames = new (float Time, float Value)[257];
		for ( int i = 0; i < frames.Length; i++ )
		{
			var time = MathF.Pow( i / (float)(frames.Length - 1), 3 );
			frames[i] = (time, Gain( time * distance ));
		}
		return frames;
	}

	internal static SourceSoundDefinition Parse( VmtNode root, string path )
	{
		var parameters = new VmtParameters( root.Values, $"{path}: {root.Name}" );
		var waves = new List<string>();
		bool dry = false;
		foreach ( var entry in root.Children )
		{
			if ( !entry.IsBlock && entry.Name.Equals( "wave", StringComparison.OrdinalIgnoreCase ) )
				AddWave( entry.Value );
			else if ( entry.IsBlock && entry.Name.Equals( "rndwave", StringComparison.OrdinalIgnoreCase ) )
			{
				foreach ( var wave in entry.Children )
				{
					if ( wave.IsBlock || !wave.Name.Equals( "wave", StringComparison.OrdinalIgnoreCase ) )
						throw new InvalidDataException( $"{path}: sound '{root.Name}' has an invalid random wave." );
					AddWave( wave.Value );
				}
			}
		}
		if ( waves.Count == 0 )
			throw new InvalidDataException( $"{path}: sound '{root.Name}' has no waves." );

		var volume = Range( parameters.String( "volume", "1" ), path );
		var pitch = Range( parameters.String( "pitch", "100" ), path );
		if ( volume.Min < 0 || pitch.Min <= 0 )
			throw new InvalidDataException( $"{path}: sound '{root.Name}' has invalid volume or pitch." );

		float level = 75;
		bool compatibility = false;
		foreach ( var entry in root.Children )
		{
			if ( entry.IsBlock ) continue;
			if ( entry.Name.Equals( "soundlevel", StringComparison.OrdinalIgnoreCase ) )
			{
				var range = Range( entry.Value, path );
				if ( range.Min != range.Max )
					throw new NotSupportedException( $"{path}: sound '{root.Name}' uses randomized sound levels." );
				level = range.Min;
				compatibility = false;
			}
			else if ( entry.Name.Equals( "attenuation", StringComparison.OrdinalIgnoreCase )
				|| entry.Name.Equals( "CompatibilityAttenuation", StringComparison.OrdinalIgnoreCase ) )
			{
				var range = Range( entry.Value, path );
				if ( range.Min != range.Max || range.Min < 0 )
					throw new NotSupportedException( $"{path}: sound '{root.Name}' uses unsupported attenuation '{entry.Value}'." );
				level = range.Min == 0 ? 0 : 50 + 20 / range.Min;
				compatibility = entry.Name.Equals( "CompatibilityAttenuation", StringComparison.OrdinalIgnoreCase );
			}
		}
		if ( level < 0 || level > 255 || !float.IsFinite( level ) )
			throw new InvalidDataException( $"{path}: sound '{root.Name}' has invalid sound level." );
		level = MathF.Truncate( level );

		return new( root.Name, path, waves.AsReadOnly(), volume, (pitch.Min / 100, pitch.Max / 100), level, compatibility, dry );

		void AddWave( string wave )
		{
			int prefix = 0;
			bool waveDry = false;
			while ( prefix < wave.Length && "*?!#><^@)}(".Contains( wave[prefix] ) )
			{
				if ( "?!><^(".Contains( wave[prefix] ) )
					throw new NotSupportedException( $"{path}: sound '{root.Name}' uses unsupported wave encoding '{wave}'." );
				waveDry |= wave[prefix++] == '#';
			}
			if ( waves.Count > 0 && dry != waveDry )
				throw new NotSupportedException( $"{path}: sound '{root.Name}' mixes dry and reverberated random waves." );
			dry = waveDry;
			wave = ScriptManifest.ContentPath( wave[prefix..] );
			if ( wave.Contains( '$' ) || (!wave.EndsWith( ".wav" ) && !wave.EndsWith( ".mp3" )) )
				throw new NotSupportedException( $"{path}: sound '{root.Name}' uses unsupported wave '{wave}'." );
			waves.Add( wave.StartsWith( "sound/" ) ? wave : $"sound/{wave}" );
		}
	}

	internal static (float Min, float Max) Range( string text, string path )
	{
		var parts = text.Split( ',' );
		if ( parts.Length is < 1 or > 2 )
			throw new InvalidDataException( $"{path}: invalid sound range '{text}'." );
		var min = Value( parts[0].Trim() );
		var max = parts.Length == 1 ? min : Value( parts[1].Trim() );
		if ( min > max )
			throw new InvalidDataException( $"{path}: reversed sound range '{text}'." );
		return (min, max);

		float Value( string value )
		{
			var constant = value.ToUpperInvariant() switch
			{
				"VOL_NORM" or "ATTN_STATIC" => 1.0f,
				"PITCH_NORM" => 100,
				"PITCH_LOW" => 95,
				"PITCH_HIGH" => 120,
				"SNDLVL_NONE" or "ATTN_NONE" => 0,
				"SNDLVL_IDLE" or "SNDLVL_TALKING" => 60,
				"SNDLVL_NORM" => 75,
				"SNDLVL_STATIC" => 66,
				"SNDLVL_GUNFIRE" => 140,
				"ATTN_NORM" => 0.8f,
				"ATTN_IDLE" => 2.0f,
				"ATTN_RICOCHET" => 1.5f,
				_ => float.NaN
			};
			if ( float.IsFinite( constant ) ) return constant;
			if ( value.StartsWith( "SNDLVL_", StringComparison.OrdinalIgnoreCase ) && value.EndsWith( "dB", StringComparison.OrdinalIgnoreCase ) )
				value = value[7..^2];
			if ( !float.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result ) || !float.IsFinite( result ) )
				throw new InvalidDataException( $"{path}: invalid sound value '{value}'." );
			return result;
		}
	}
}
