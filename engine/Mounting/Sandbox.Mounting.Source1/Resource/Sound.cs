using Sandbox.Mounting;
using System;

namespace Sandbox.Mounting.Source1;

internal sealed class WavSoundLoader( string fullPath ) : ResourceLoader<BaseMount>
{
	protected override object Load() => SoundFile.FromWav( Path, Host.GetFileBytes( fullPath ) );
}

internal sealed class Mp3SoundLoader( string fullPath ) : ResourceLoader<BaseMount>
{
	protected override object Load() => SoundFile.FromMp3( Path, Host.GetFileBytes( fullPath ) );
}

internal static class SourceSoundEvents
{
	internal static SoundEvent Create( BaseMount host, SourceSoundDefinition definition, string contentRoot = "", bool ambient = false )
	{
		var sounds = new List<SoundFile>();
		foreach ( var wave in definition.Waves )
		{
			var sound = host.LoadContentSound( wave, contentRoot );
			if ( sound is null || !sound.IsValid )
				throw new InvalidOperationException( $"{definition.SourcePath}: wave '{wave}' could not be loaded." );
			sounds.Add( sound );
		}
		var distance = definition.AudibleDistance();
		var frames = definition.SampleFalloff( distance )
			.Select( frame => new Curve.Frame( frame.Time, frame.Value ) { Mode = Curve.HandleMode.Linear } ).ToArray();
		return new SoundEvent
		{
			UI = ambient,
			Sounds = sounds,
			Volume = new RangedFloat( definition.Volume.Min, definition.Volume.Max ),
			Pitch = new RangedFloat( definition.Pitch.Min, definition.Pitch.Max ),
			SelectionMode = SoundEvent.SoundSelectionMode.Random,
			ReverbEnabled = !definition.Dry,
			AirAbsorption = false,
			DistanceAttenuation = !ambient && definition.SoundLevel != 0,
			Distance = distance,
			Falloff = new Curve( frames )
		};
	}
}
