using System;
using System.IO;
using System.IO.Compression;

namespace Editor;

/// <summary>
/// Stages keyed files on disk and merges them into the shipped archive in one atomic replacement.
/// </summary>
public sealed class BakeCacheWriter : IDisposable
{
	readonly string path = BakeCache.FilePath;
	readonly FileStream pending;
	readonly Dictionary<string, (long Offset, int Length)> updates = new( StringComparer.Ordinal );

	/// <summary>
	/// Open the shipped cache for a manual bake in a developer editor.
	/// </summary>
	public static BakeCacheWriter Open()
	{
		if ( Sandbox.Application.IsRetail || !Sandbox.Application.IsEditor )
			throw new InvalidOperationException( "Run the bake in a developer build of the editor." );

		return new BakeCacheWriter();
	}

	internal BakeCacheWriter()
	{
		// Also keeps two editors from publishing overlapping bakes.
		pending = new FileStream( path + ".pending", FileMode.Create, FileAccess.ReadWrite, FileShare.None,
			4096, FileOptions.DeleteOnClose );
	}

	/// <summary>
	/// Add or replace the bytes under a key. Only the latest value is published.
	/// </summary>
	public void Set( string key, byte[] bytes )
	{
		ArgumentException.ThrowIfNullOrEmpty( key );
		ArgumentNullException.ThrowIfNull( bytes );

		var offset = pending.Seek( 0, SeekOrigin.End );
		pending.Write( bytes );
		updates[key] = (offset, bytes.Length);
	}

	/// <summary>
	/// Copy untouched entries and staged values into a new pack. An interrupted bake leaves the old pack usable.
	/// </summary>
	public void Save()
	{
		if ( updates.Count == 0 ) return;

		var temp = path + ".tmp";
		try
		{
			using ( var original = File.Exists( path ) ? ZipFile.OpenRead( path ) : null )
			using ( var stream = File.Create( temp ) )
			using ( var output = new ZipArchive( stream, ZipArchiveMode.Create ) )
			{
				var buffer = new byte[64 * 1024];
				var keys = updates.Keys.Union( original?.Entries.Select( x => x.FullName ) ?? [], StringComparer.Ordinal );
				foreach ( var key in keys.Order( StringComparer.Ordinal ) )
				{
					var entry = output.CreateEntry( key, CompressionLevel.NoCompression );

					// Fixed ZIP timestamps keep identical bakes byte-for-byte reproducible.
					entry.LastWriteTime = new DateTimeOffset( 1980, 1, 1, 0, 0, 0, TimeSpan.Zero );
					using var destination = entry.Open();

					if ( updates.TryGetValue( key, out var value ) )
					{
						pending.Position = value.Offset;
						for ( var remaining = value.Length; remaining > 0; )
						{
							var chunk = buffer.AsSpan( 0, Math.Min( remaining, buffer.Length ) );
							pending.ReadExactly( chunk );
							destination.Write( chunk );
							remaining -= chunk.Length;
						}
					}
					else
					{
						using var source = original.GetEntry( key ).Open();
						source.CopyTo( destination );
					}
				}
			}

			if ( File.Exists( path ) )
			{
				File.Replace( temp, path, null );
			}
			else
			{
				File.Move( temp, path );
			}

			BakeCache.Close();
		}
		finally
		{
			File.Delete( temp );
		}
	}

	/// <summary>
	/// Discard staging data and release the writer lock.
	/// </summary>
	public void Dispose() => pending.Dispose();
}
