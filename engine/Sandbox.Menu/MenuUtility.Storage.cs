using System;
using System.IO;

namespace Sandbox;

public static partial class MenuUtility
{
	/// <summary>
	/// Allows to menu addon to interact with the downloaded file cache
	/// </summary>
	public static class Storage
	{
		public struct FileEntry
		{
			public string Filename { get; set; }
			public long Size { get; set; }
			public DateTime Created { get; set; }
			public DateTime LastAccessed { get; set; }
		}

		/// <summary>
		/// Get a list of all the local cache files (download/)
		/// </summary>
		public static IEnumerable<FileEntry> GetStorageFiles()
		{
			var path = EngineFileSystem.DownloadedFiles.GetFullPath( "/" );

			foreach ( var file in Directory.EnumerateFiles( path, "*", SearchOption.AllDirectories ) )
			{
				var f = new FileEntry();

				try
				{
					var info = new FileInfo( file );

					f.Filename = info.FullName;
					f.Size = info.Length;
					f.Created = info.CreationTime;
					f.LastAccessed = info.LastAccessTime;
				}
				catch ( FileNotFoundException )
				{
					continue;
				}

				yield return f;
			}
		}

		/// <summary>
		/// A package we've downloaded into the cache at some point
		/// </summary>
		public struct PackageEntry
		{
			public string Ident { get; set; }
			public string Title { get; set; }
			public string Type { get; set; }
			public string Thumb { get; set; }
			public DateTimeOffset Downloaded { get; set; }
			public DateTimeOffset LastPlayed { get; set; }

			/// <summary>
			/// Bytes this package's files are currently taking up in the cache
			/// </summary>
			public long Size { get; set; }

			/// <summary>
			/// How many of this package's files are currently in the cache
			/// </summary>
			public int FileCount { get; set; }
		}

		/// <summary>
		/// Get all the packages we have download records for, with their current cache usage.
		/// Runs in a thread because it stats a lot of files.
		/// </summary>
		public static async Task<List<PackageEntry>> GetPackagesAsync()
		{
			return await Task.Run( () =>
			{
				var list = new List<PackageEntry>();

				foreach ( var record in DownloadedPackages.All() )
				{
					var entry = new PackageEntry
					{
						Ident = record.Ident,
						Title = record.Title,
						Type = record.Type,
						Thumb = record.Thumb,
						Downloaded = record.Downloaded,
						LastPlayed = record.LastUsed
					};

					foreach ( var file in record.Files )
					{
						if ( DownloadedPackages.GetCachedFilePath( file ) is null )
							continue;

						entry.Size += file.Size;
						entry.FileCount++;
					}

					list.Add( entry );
				}

				return list;
			} );
		}

		/// <summary>
		/// Get the files a package has in the cache right now. Filename is the
		/// content path, so it can be categorized by extension.
		/// </summary>
		public static IEnumerable<FileEntry> GetPackageFiles( string ident )
		{
			var record = DownloadedPackages.Find( ident );
			if ( record is null ) yield break;

			foreach ( var file in record.Files )
			{
				var path = DownloadedPackages.GetCachedFilePath( file );
				if ( path is null ) continue;

				yield return new FileEntry
				{
					Filename = file.Path,
					Size = file.Size
				};
			}
		}

		/// <summary>
		/// Delete a package's cached files. It'll redownload next time it's played.
		/// </summary>
		public static async Task DeletePackageAsync( string ident )
		{
			var record = DownloadedPackages.Find( ident );
			if ( record is null ) return;

			await Task.Run( () => DownloadedPackages.Delete( record ) );
		}

		/// <summary>
		/// Delete all files that haven't been used since x date.
		/// </summary>
		public static async Task FlushAsync( DateTime beforeDate )
		{
			var path = EngineFileSystem.DownloadedFiles.GetFullPath( "/" );

			//
			// Run the guts of the logic in a thread to avoid hitching
			//
			await Task.Run( () =>
			{
				foreach ( var file in Directory.EnumerateFiles( path, "*", SearchOption.AllDirectories ) )
				{
					var info = new FileInfo( file );

					if ( info.LastAccessTime < beforeDate )
					{
						try
						{
							File.Delete( file );
						}
						catch ( Exception e )
						{
							Log.Error( e, $"Failed to delete file {file}" );
						}

					}
				}
			} );
		}
	}
}
