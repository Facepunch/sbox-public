using Sandbox;
using ValvePak;

internal class HL2Sound : ResourceLoader<HL2Mount>
{
	private readonly VpkArchive _package;
	private readonly VpkEntry _entry;
	private readonly string _filePath;

	public HL2Sound( VpkArchive package, VpkEntry entry )
	{
		_package = package;
		_entry = entry;
	}

	public HL2Sound( string filePath )
	{
		_filePath = filePath;
	}

	protected override object Load()
	{
		byte[] data;
		if ( _package != null )
		{
			_package.ReadEntry( _entry, out data );
		}
		else
		{
			data = File.ReadAllBytes( _filePath );
		}
		return LoadSound( data, Path );
	}

	internal static SoundFile LoadSound( byte[] data, string path )
	{
		return data == null || data.Length < 4
			? null
			: data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'
			? SoundFile.FromWav( path, data, false )
			: data[0] == 'I' && data[1] == 'D' && data[2] == '3' || data[0] == 0xFF && (data[1] & 0xE0) == 0xE0
			? SoundFile.FromMp3( path, data, false )
			: null;
	}
}
