using System.IO;
using System.IO.Compression;

namespace Sandbox;

/// <summary>
/// Read-only access to the files packed into game/core/bake.cache by the editor.
/// </summary>
internal static class BakeCache
{
	internal static string FilePath => EngineFileSystem.Root.GetFullPath( "/core/bake.cache" );

	static readonly object sync = new();
	static ZipArchive archive;
	static bool opened;

	static void Open()
	{
		opened = true;
		var path = FilePath;
		if ( !File.Exists( path ) ) return;

		// The editor can replace the pack while a client is reading this version.
		var file = File.Open( path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete );
		try
		{
			archive = new ZipArchive( file, ZipArchiveMode.Read );
		}
		catch
		{
			file.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Read a file by its exact key. The archive index is shared across lookups.
	/// </summary>
	internal static bool TryGet( string key, out byte[] data )
	{
		lock ( sync )
		{
			data = null;
			try
			{
				if ( !opened ) Open();

				if ( archive?.GetEntry( key ) is not { } entry || entry.Length > Array.MaxLength ) return false;

				using var stream = entry.Open();
				var bytes = new byte[checked((int)entry.Length)];
				stream.ReadExactly( bytes );
				data = bytes;
				return true;
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or UnauthorizedAccessException )
			{
				Log.Warning( $"Couldn't read baked file '{key}': {e.Message}" );
				return false;
			}
		}
	}

	/// <summary>
	/// Release the archive on shutdown or when the editor publishes a new bake.
	/// </summary>
	internal static void Close()
	{
		lock ( sync )
		{
			archive?.Dispose();
			archive = null;
			opened = false;
		}
	}
}
