using Sandbox.Internal;
using System.Text.RegularExpressions;

namespace Sandbox;

/// <summary>
/// Downloadeded assets go in the ".source2/assets" folder, where they are symlinked on demand
/// for use by the engine.
/// </summary>
static class AssetDownloadCache
{
	[ConVar( "debug_network_files", ConVarFlags.Protected )]
	internal static bool DebugNetworkFiles { get; set; } = false;

	static string cacheFolder;

	public static void Initialize( string folder )
	{
		System.IO.Directory.CreateDirectory( folder );
		cacheFolder = folder;
	}

	internal static RedirectFileSystem CreateRedirectFileSystem()
	{
		if ( string.IsNullOrEmpty( cacheFolder ) )
			return null;

		return RedirectFileSystem.Create( cacheFolder );
	}

	/// <summary>
	/// We have downloaded an asset file. Store it for reuse in the future
	/// </summary>
	public static string StoreFile( string relativePath, ulong crc, byte[] data )
	{
		var cachePath = CreateCacheFilename( relativePath, crc );
		var absPath = System.IO.Path.Combine( cacheFolder, cachePath );

		// todo - check crc of data

		// make sure the directory tree exists
		string directory = System.IO.Path.GetDirectoryName( absPath );
		System.IO.Directory.CreateDirectory( directory );

		// todo - write with retry?
		System.IO.File.WriteAllBytes( absPath, data );

		return absPath;
	}

	/// <summary>
	/// Try to mount the downloaded file with this path and crc. If it doesn't exist, return false
	/// </summary>
	public static bool IsFileDownloaded( string relativePath, ulong crc, out bool isCoreContent )
	{
		isCoreContent = false;
		relativePath = relativePath.NormalizeFilename( true ).Trim( '/', '\\' );

		// Don't download core content
		if ( EngineFileSystem.CoreContent.FileExists( relativePath ) )
		{
			if ( EngineFileSystem.CoreContent.GetCrc( relativePath ) == crc )
			{
				isCoreContent = true;
				return true;
			}
		}

		var cachePath = CreateCacheFilename( relativePath, crc );
		var absPath = System.IO.Path.Combine( cacheFolder, cachePath );

		if ( !System.IO.File.Exists( absPath ) )
		{
			return false;
		}

		return true;
	}

	/// <summary>
	/// Makes sure the directory exists, and returns the relative path to this file
	/// </summary>
	public static string CreateCacheFilename( string filePath, ulong crc )
	{
		var ext = System.IO.Path.GetExtension( filePath );
		var targetFile = filePath[..^ext.Length];

		targetFile = Regex.Replace( targetFile, "[^a-zA-Z0-9/]", "_" );
		targetFile = targetFile.TrimStart( '/', '\\' );
		targetFile = $"{targetFile}.{crc:x}{ext}";

		return targetFile;
	}

	/// <summary>
	/// Generate the absolute path for this, whether it exists or not
	/// </summary>
	internal static string GetAbsolutePath( string path, ulong crc )
	{
		return System.IO.Path.Combine( cacheFolder, CreateCacheFilename( path, crc ) );
	}


	static string[] _neverDownloadExtensions = new[] { ".dll", ".exe", ".bat", ".cmd", ".msi", ".scr", ".com", ".cpl", ".vbs", ".ps1", ".reg" };

	/// <summary>
	/// Is a file an legal download or not (filter out executables etc)
	/// </summary>
	internal static bool IsLegalDownload( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return false;
		if ( path.Length < 3 ) return false;
		if ( path.Contains( ".." ) ) return false;

		var ext = System.IO.Path.GetExtension( path );
		if ( string.IsNullOrWhiteSpace( ext ) )
			return false;

		if ( _neverDownloadExtensions.Contains( ext, StringComparer.OrdinalIgnoreCase ) )
			return false;

		return true;
	}

	/// <summary>
	/// Given a file with a crc, create a unique cache filename for it
	/// </summary>
	internal static string CreateGameCacheFilename( string path, string crc )
	{
		return $"{path.ToLowerInvariant().Md5()}.{crc}.cache";
	}

	/// <summary>
	/// Is this file in the game cache, core content or the download cache, without mounting it
	/// </summary>
	internal static bool IsCached( string path, ulong crc )
	{
		var gc = "/gamecache/" + CreateGameCacheFilename( path, crc.ToString( "x" ) );
		return EngineFileSystem.Root.FileExists( gc ) || IsFileDownloaded( path, crc, out _ );
	}

	/// <summary>
	/// <see cref="ResolveCached"/>'s answer for a file core content already has, so nothing needs mounting.
	/// </summary>
	internal static readonly string CoreContent = "core";

	/// <summary>
	/// Where a cached copy of this file is: the absolute path in the game or download cache,
	/// <see cref="CoreContent"/>, or null when it has to be downloaded. Only reads, safe off the main thread.
	/// </summary>
	internal static string ResolveCached( string path, ulong crc )
	{
		var gc = "/gamecache/" + CreateGameCacheFilename( path, crc.ToString( "x" ) );
		if ( EngineFileSystem.Root.FileExists( gc ) )
			return EngineFileSystem.Root.GetFullPath( gc );

		if ( !IsFileDownloaded( path, crc, out var wasCoreContent ) )
			return null;

		return wasCoreContent ? CoreContent : GetAbsolutePath( path, crc );
	}

	internal static bool TryMount( RedirectFileSystem fs, string path, ulong crc )
	{
		var resolved = ResolveCached( path, crc );
		if ( resolved is null ) return false;

		Mount( fs, path, resolved );
		return true;
	}

	/// <summary>
	/// Mount a file at what <see cref="ResolveCached"/> found for it.
	/// </summary>
	internal static void Mount( RedirectFileSystem fs, string path, string resolved )
	{
		if ( ReferenceEquals( resolved, CoreContent ) ) return;

		fs.AddAbsFile( path.NormalizeFilename( true ), resolved );
	}
}
