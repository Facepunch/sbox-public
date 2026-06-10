using Sandbox.Mounting;
using System;
using System.Threading.Tasks;

/// <summary>
/// A mounting implementation for Max Payne 2: The Fall of Max Payne
/// </summary>
public partial class MaxPayne2Mount : BaseGameMount
{
	public override string Ident => "maxpayne2";
	public override string Title => "Max Payne 2";

	const long appId = 12150;

	readonly List<RasLib.Archive> archives = [];

	protected override void Initialize( InitializeContext context )
	{
		if ( !context.IsAppInstalled( appId ) )
			return;

		var dir = context.GetAppDirectory( appId );
		if ( string.IsNullOrEmpty( dir ) )
			return;

		foreach ( var rasPath in System.IO.Directory.EnumerateFiles( dir, "*.ras", System.IO.SearchOption.AllDirectories ) )
		{
			try
			{
				var archive = new RasLib.Archive( rasPath );
				if ( archive.IsValid )
				{
					archives.Add( archive );
					continue;
				}

				archive.Dispose();
			}
			catch ( Exception e )
			{
				Log.Warning( $"Failed to load RAS {rasPath}: {e.Message}" );
			}
		}

		archives.Sort( ( a, b ) => string.Compare( a.Name, b.Name, StringComparison.OrdinalIgnoreCase ) );

		IsInstalled = archives.Count > 0;
	}

	protected override Task Mount( MountContext context )
	{
		var added = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var archive in archives )
		{
			foreach ( var file in archive.Files )
			{
				var ext = System.IO.Path.GetExtension( file.FileName )?.ToLowerInvariant();
				if ( string.IsNullOrWhiteSpace( ext ) )
					continue;

				var path = file.FullPath;
				if ( !added.Add( path ) )
					continue;

				switch ( ext )
				{
					case ".ldb":
						context.Add( ResourceType.Scene, path, new MaxPayne2Map( path ) );
						break;

					case ".kf2":
						// gn pages are placement quads for the comic player; their art is in Textures
						if ( IsMeshlessKf2( path ) || path.Contains( "/graphicnovelpages/", StringComparison.OrdinalIgnoreCase ) ) continue;
						if ( !HasMeshChunk( path ) ) continue;
						context.Add( ResourceType.Model, path, new MaxPayne2Model( path ) );
						break;

					case ".dds":
					case ".tga":
					case ".pcx":
					case ".jpg":
						context.Add( ResourceType.Texture, path, new MaxPayne2Texture( path ) );
						break;

					case ".wav":
						context.Add( ResourceType.Sound, path, new MaxPayne2Sound( path ) );
						break;
				}
			}
		}

		IsMounted = true;
		return Task.CompletedTask;
	}

	// skeletons hold keyframe animations, camerapaths hold cameras - neither has mesh data
	static bool IsMeshlessKf2( string path )
	{
		return path.Contains( "/skeletons/", StringComparison.OrdinalIgnoreCase )
			|| path.Contains( "/camerapaths/", StringComparison.OrdinalIgnoreCase );
	}

	// meshless kf2s hide everywhere (skins texture-set variants, projectile anims) - sniff the
	// top-level chunks; only the nested NODE chunk lies about its size, outer chunks are reliable
	bool HasMeshChunk( string path )
	{
		var data = GetFileBytes( path );
		if ( data is null ) return false;

		var pos = 0;
		while ( pos + 13 <= data.Length )
		{
			if ( data[pos] != 0x0C ) return false;
			if ( BitConverter.ToUInt32( data, pos + 1 ) == 0x00010005 ) return true;
			var size = BitConverter.ToUInt32( data, pos + 9 );
			if ( size < 13 ) return false;
			pos += (int)size;
		}

		return false;
	}

	public byte[] GetFileBytes( string filename )
	{
		foreach ( var archive in archives )
		{
			var data = archive.GetFileBytes( filename );
			if ( data != null )
				return data;
		}

		return null;
	}

	public IEnumerable<string> FindFiles( string folderPrefix, string extension )
	{
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var archive in archives )
		{
			foreach ( var file in archive.Files )
			{
				var path = file.FullPath;
				if ( !path.StartsWith( folderPrefix, StringComparison.OrdinalIgnoreCase ) )
					continue;
				if ( !path.EndsWith( extension, StringComparison.OrdinalIgnoreCase ) )
					continue;
				if ( seen.Add( path ) )
					yield return path;
			}
		}
	}

	public bool FileExists( string filename )
	{
		foreach ( var archive in archives )
		{
			if ( archive.FileExists( filename ) )
				return true;
		}

		return false;
	}

	protected override void Shutdown()
	{
		foreach ( var archive in archives )
		{
			archive.Dispose();
		}

		archives.Clear();
	}
}
