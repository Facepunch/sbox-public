/// <summary>
/// A mounting implementation for Quake
/// </summary>
public partial class QuakeMount : BaseGameMount
{
	public override string Ident => "quake";
	public override string Title => "Quake";

	const long appId = 2310;

	readonly Dictionary<string, List<PakLib.Pack>> paks = [];
	readonly Dictionary<string, byte[]> palettes = [];

	protected override void Initialize( InitializeContext context )
	{
		if ( !context.IsAppInstalled( appId ) )
			return;

		var dir = context.GetAppDirectory( appId );

		foreach ( var pakPath in System.IO.Directory.EnumerateFiles( dir, "*.pak", SearchOption.AllDirectories ) )
		{
			var pakDir = Path.GetDirectoryName( pakPath );
			if ( pakDir is null )
				continue;

			var pakFolder = Path.GetRelativePath( dir, pakDir ).Replace( '\\', '/' );
			var pak = new PakLib.Pack( pakPath );

			if ( !paks.TryGetValue( pakFolder, out var pakList ) )
			{
				pakList = [];
				paks[pakFolder] = pakList;
			}

			pakList.Add( pak );
		}

		foreach ( var list in paks.Values )
		{
			list.Sort( ( a, b ) => string.Compare( a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase ) );
		}

		IsInstalled = paks.Count > 0;
	}

	public Stream GetFileStream( string pakFolder, string filename )
	{
		var data = GetFileBytes( pakFolder, filename );
		return data != null ? new MemoryStream( data ) : Stream.Null;
	}

	public byte[] GetFileBytes( string pakDir, string filename, int maxLength = -1 )
	{
		if ( !paks.TryGetValue( pakDir, out var pakList ) )
			return null;

		foreach ( var pak in pakList )
		{
			var data = pak.GetFileBytes( filename, maxLength );
			if ( data != null )
				return data;
		}

		return null;
	}

	public string GetFullFilePath( string pakDir, string filename )
	{
		if ( !paks.TryGetValue( pakDir, out var pakList ) )
			return null;

		foreach ( var pak in pakList )
		{
			var path = pak.GetFilePath( filename );
			if ( string.IsNullOrWhiteSpace( path ) )
				continue;

			return path;
		}

		return null;
	}

	public bool FileExists( string pakDir, string filename )
	{
		if ( !paks.TryGetValue( pakDir, out var pakList ) )
			return false;

		foreach ( var pak in pakList )
		{
			if ( pak.FileExists( filename ) )
				return true;
		}

		return false;
	}

	public byte[] GetPalette( string pakDir )
	{
		if ( palettes.TryGetValue( pakDir, out var palette ) && palette != null )
			return palette;

		if ( palettes.TryGetValue( "Id1", out var basePalette ) && basePalette != null )
			return basePalette;

		return null;
	}

	protected override Task Mount( MountContext context )
	{
		foreach ( var (pakDir, pakList) in paks )
		{
			foreach ( var pak in pakList )
			{
				if ( !palettes.ContainsKey( pakDir ) )
				{
					var palette = pak.GetFileBytes( "gfx/palette.lmp" );
					if ( palette is not null ) palettes[pakDir] = palette;
				}

				foreach ( var file in pak.Files )
				{
					var ext = Path.GetExtension( file.FileName )?.ToLowerInvariant();
					if ( string.IsNullOrWhiteSpace( ext ) ) continue;

					if ( ext == ".lmp" )
					{
						if ( file.FileName.Equals( "palette.lmp", System.StringComparison.OrdinalIgnoreCase ) ) continue;
						if ( file.FileName.Equals( "colormap.lmp", System.StringComparison.OrdinalIgnoreCase ) ) continue;
					}

					var path = file.FullPath;
					var fullpath = Path.Combine( pakDir, path ).Replace( '\\', '/' );

					switch ( ext )
					{
						case ".mdl": context.Add( ResourceType.Model, fullpath, new QuakeModel( pakDir, path ) ); break;
						case ".md5mesh": context.Add( ResourceType.Model, fullpath, new QuakeModelMD5( pakDir, path ) ); break;
						case ".lmp": context.Add( ResourceType.Texture, fullpath, new QuakeTexture( pakDir, path ) ); break;
						case ".wav": context.Add( ResourceType.Sound, fullpath, new QuakeSound( pakDir, path ) ); break;
					}
				}
			}
		}

		IsMounted = true;
		return Task.CompletedTask;
	}
}
