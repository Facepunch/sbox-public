using Sandbox;

class QuakeSound( string pakDir, string fileName ) : ResourceLoader<QuakeMount>
{
	public string PakDir { get; set; } = pakDir;
	public string FileName { get; set; } = fileName;

	protected override object Load()
	{
		return SoundFile.FromWav( Path, Host.GetFileBytes( PakDir, FileName ) );
	}
}
