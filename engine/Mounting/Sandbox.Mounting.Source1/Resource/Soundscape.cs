using System.IO;
using Sandbox.Mounting;

namespace Sandbox.Mounting.Source1;

internal sealed class SourceSoundscapeLoader( SourceSoundscapes.Definition definition, string root ) : ResourceLoader<BaseMount>
{
	protected override object Load()
	{
		foreach ( var diagnostic in definition.Diagnostics )
			Log.Warning( diagnostic );
		var soundscape = new Soundscape();
		for ( var i = 0; i < definition.Entries.Count; i++ )
		{
			var entry = definition.Entries[i];
			if ( entry.Random )
			{
				var sound = GameResource.Load<SoundEvent>( $"{Path}.{i}.sound" ) ?? throw new InvalidDataException( $"{Path}: random sound {i} could not be loaded." );
				soundscape.StingSounds.Add( new() { SoundFile = sound, RepeatTime = new( entry.Time.Min, entry.Time.Max ), Distance = entry.Ambient ? 0 : 36 } );
			}
			else
			{
				var path = entry.Sound.Waves.Last();
				var resource = Host.GetContentPath( path, root ) ?? throw new FileNotFoundException( $"{Path}: missing looping wave '{path}'." );
				var sound = SoundFile.Load( $"{resource}.vsnd" );
				if ( sound is null || !sound.IsValid )
					throw new InvalidDataException( $"{Path}: looping wave '{path}' could not be loaded." );
				soundscape.LoopedSounds.Add( new() { SoundFile = sound, Volume = new( entry.Sound.Volume.Min, entry.Sound.Volume.Max ) } );
			}
		}

		return soundscape;
	}
}

internal sealed class SourceSoundscapeEventLoader( SourceSoundscapes.Entry entry, string root ) : ResourceLoader<BaseMount>
{
	protected override object Load() => SourceSoundEvents.Create( Host, entry.Sound, root, entry.Ambient );
}
