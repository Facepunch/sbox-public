
class WavSoundLoader( string fullPath ) : ResourceLoader<GameMount>
{
	protected override object Load() => SoundFile.FromWav( Path, File.ReadAllBytes( fullPath ), false );
}
