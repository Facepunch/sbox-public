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
	/// Files a game server sent us. A crc can be forged, so these are kept apart from package
	/// downloads, which only ever look up their own. Cache filenames never start with a dot.
	/// </summary>
	const string HostFolder = ".network";

	/// <summary>
	/// We have downloaded an asset file. Store it for reuse in the future
	/// </summary>
	public static string StoreFile( string relativePath, ulong crc, byte[] data, bool fromHost = false )
	{
		var absPath = GetAbsolutePath( relativePath, crc, fromHost );

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
	internal static string GetAbsolutePath( string path, ulong crc, bool fromHost = false )
	{
		var cachePath = CreateCacheFilename( path, crc );
		return fromHost ? System.IO.Path.Combine( cacheFolder, HostFolder, cachePath ) : System.IO.Path.Combine( cacheFolder, cachePath );
	}


	static readonly System.Buffers.SearchValues<char> LegalExtensionChars = System.Buffers.SearchValues.Create( "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_" );

	static string[] _neverDownloadExtensions = new[] { ".dll", ".exe", ".bat", ".cmd", ".msi", ".scr", ".com", ".cpl", ".vbs", ".ps1", ".reg" };

	/// <summary>
	/// Is a file an legal download or not (filter out executables etc)
	/// </summary>
	internal static bool IsLegalDownload( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return false;
		if ( path.Length < 3 ) return false;
		if ( path.Contains( ".." ) ) return false;

		// Windows drops trailing spaces and dots and writes "::$DATA" streams to the file itself, so "x.dll " is x.dll
		if ( path.Contains( ':' ) ) return false;

		var ext = System.IO.Path.GetExtension( path );
		if ( string.IsNullOrWhiteSpace( ext ) )
			return false;

		if ( ext.AsSpan( 1 ).ContainsAnyExcept( LegalExtensionChars ) )
			return false;

		if ( _neverDownloadExtensions.Contains( ext, StringComparer.OrdinalIgnoreCase ) )
			return false;

		return true;
	}

	/// <summary>
	/// <see cref="ResolveCached"/>'s answer for a file core content already has, so nothing needs mounting.
	/// </summary>
	internal static readonly string CoreContent = "core";

	/// <summary>
	/// Where a cached copy of this file is: the absolute path in the download cache,
	/// <see cref="CoreContent"/>, or null when it has to be downloaded. Only reads, safe off the main thread.
	/// </summary>
	internal static string ResolveCached( string path, ulong crc, bool includeHostFiles = false )
	{
		if ( IsFileDownloaded( path, crc, out var wasCoreContent ) )
			return wasCoreContent ? CoreContent : GetAbsolutePath( path, crc );

		if ( !includeHostFiles )
			return null;

		var hostFile = GetAbsolutePath( path.NormalizeFilename( true ).Trim( '/', '\\' ), crc, fromHost: true );
		return System.IO.File.Exists( hostFile ) ? hostFile : null;
	}

	internal static bool TryMount( RedirectFileSystem fs, string path, ulong crc, bool includeHostFiles = false )
	{
		var resolved = ResolveCached( path, crc, includeHostFiles );
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
