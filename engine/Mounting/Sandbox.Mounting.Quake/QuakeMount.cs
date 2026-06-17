/// <summary>
/// A mounting implementation for Quake
/// </summary>
public partial class QuakeMount : BaseGameMount
{
	public override string Ident => "quake";
	public override string Title => "Quake";

	const long AppId = 2310;

	public override long? SteamAppId => AppId;

	readonly CaseInsensitiveDictionary<List<PakLib.Pack>> _paks = [];
	readonly CaseInsensitiveDictionary<byte[]> _palettes = [];
	string _root;

	protected override void Initialize( InitializeContext context )
	{
		if ( !context.IsAppInstalled( AppId ) )
			return;

		_root = context.GetAppDirectory( AppId );
		if ( _root is null )
			return;

		foreach ( var pakPath in System.IO.Directory.EnumerateFiles( _root, "*.pak", SearchOption.AllDirectories ) )
		{
			var pakDir = Path.GetDirectoryName( pakPath );
			if ( pakDir is null )
				continue;

			var pakFolder = Path.GetRelativePath( _root, pakDir ).Replace( '\\', '/' );

			if ( !_paks.TryGetValue( pakFolder, out var pakList ) )
				_paks[pakFolder] = pakList = [];

			pakList.Add( new PakLib.Pack( pakPath ) );
		}

		foreach ( var list in _paks.Values )
			list.Sort( ( a, b ) => string.Compare( a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase ) );

		IsInstalled = true;
	}

	List<PakLib.Pack> Paks( string pakDir ) => _paks.TryGetValue( pakDir, out var list ) ? list : [];

	public Stream GetFileStream( string pakDir, string filename )
	{
		var data = GetFileBytes( pakDir, filename );
		return data != null ? new MemoryStream( data ) : Stream.Null;
	}

	public byte[] GetFileBytes( string pakDir, string filename, int maxLength = -1 )
	{
		foreach ( var pak in Paks( pakDir ) )
		{
			var data = pak.GetFileBytes( filename, maxLength );
			if ( data != null )
				return data;
		}

		if ( LoosePath( pakDir, filename ) is not string path )
			return null;

		var bytes = File.ReadAllBytes( path );
		return maxLength >= 0 && maxLength < bytes.Length ? bytes[..maxLength] : bytes;
	}

	public string GetFullFilePath( string pakDir, string filename )
	{
		foreach ( var pak in Paks( pakDir ) )
		{
			var path = pak.GetFilePath( filename );
			if ( !string.IsNullOrWhiteSpace( path ) )
				return path;
		}

		return LoosePath( pakDir, filename );
	}

	public bool FileExists( string pakDir, string filename )
	{
		foreach ( var pak in Paks( pakDir ) )
		{
			if ( pak.FileExists( filename ) )
				return true;
		}

		return LoosePath( pakDir, filename ) is not null;
	}

	string LoosePath( string pakDir, string filename )
	{
		if ( string.IsNullOrEmpty( filename ) )
			return null;

		var path = Path.Combine( _root, pakDir, filename );
		return File.Exists( path ) ? path : null;
	}

	public byte[] GetPalette( string pakDir )
	{
		if ( _palettes.TryGetValue( pakDir, out var palette ) && palette != null )
			return palette;

		if ( _palettes.TryGetValue( "Id1", out var basePalette ) && basePalette != null )
			return basePalette;

		return null;
	}

	protected override Task Mount( MountContext context )
	{
		var registered = new HashSet<string>( System.StringComparer.OrdinalIgnoreCase );

		foreach ( var (pakDir, pakList) in _paks )
		{
			foreach ( var pak in pakList )
			{
				if ( !_palettes.ContainsKey( pakDir ) )
				{
					var palette = pak.GetFileBytes( "gfx/palette.lmp" );
					if ( palette is not null ) _palettes[pakDir] = palette;
				}

				foreach ( var file in pak.Files )
					Register( context, registered, pakDir, file.FullPath );
			}
		}

		foreach ( var gameDir in System.IO.Directory.EnumerateDirectories( _root ) )
		{
			var pakDir = Path.GetRelativePath( _root, gameDir ).Replace( '\\', '/' );

			foreach ( var file in System.IO.Directory.EnumerateFiles( gameDir, "*", SearchOption.AllDirectories ) )
				Register( context, registered, pakDir, Path.GetRelativePath( gameDir, file ).Replace( '\\', '/' ) );
		}

		IsMounted = true;
		return Task.CompletedTask;
	}

	static void Register( MountContext context, HashSet<string> registered, string pakDir, string path )
	{
		var ext = Path.GetExtension( path )?.ToLowerInvariant();
		if ( string.IsNullOrWhiteSpace( ext ) ) return;

		if ( ext == ".lmp" )
		{
			var fileName = Path.GetFileName( path );
			if ( fileName.Equals( "palette.lmp", System.StringComparison.OrdinalIgnoreCase ) ) return;
			if ( fileName.Equals( "colormap.lmp", System.StringComparison.OrdinalIgnoreCase ) ) return;
		}

		var fullpath = Path.Combine( pakDir, path ).Replace( '\\', '/' );
		if ( !registered.Add( fullpath ) ) return;

		switch ( ext )
		{
			case ".mdl": context.Add( ResourceType.Model, fullpath, new QuakeModel( pakDir, path ) ); break;
			case ".md5mesh": context.Add( ResourceType.Model, fullpath, new QuakeModelMD5( pakDir, path ) ); break;
			case ".lmp": context.Add( ResourceType.Texture, fullpath, new QuakeTexture( pakDir, path ) ); break;
			case ".wav": context.Add( ResourceType.Sound, fullpath, new QuakeSound( pakDir, path ) ); break;
			case ".bsp": context.Add( ResourceType.Scene, fullpath, new QuakeMap( pakDir, path ) ); break;
		}
	}
}
