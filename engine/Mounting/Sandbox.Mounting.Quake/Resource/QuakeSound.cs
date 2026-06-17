using Sandbox;

class QuakeSound( string pakDir, string fileName ) : ResourceLoader<QuakeMount>
{
	public string PakDir { get; set; } = pakDir;
	public string FileName { get; set; } = fileName;

	protected override object Load()
	{
		var data = Host.GetFileBytes( PakDir, FileName );
		var loop = FileName.Contains( "ambience/", System.StringComparison.OrdinalIgnoreCase );
		return SoundFile.FromWav( Path, data, new SoundFile.LoadOptions { Loop = loop } );
	}
}
