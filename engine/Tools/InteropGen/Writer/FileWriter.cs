using System.Collections.Generic;
using System.IO;

namespace Facepunch.InteropGen;

/// <summary>
/// Persists generated output to disk: the main file plus any named sub-files. Files that are already
/// up to date are left untouched, and stale sub-files from a previous run are cleaned up.
/// </summary>
internal static class FileWriter
{
	public static void Save( string rootFullName, string file, string mainContent, IReadOnlyDictionary<string, string> subFiles )
	{
		string fullname = Path.GetFullPath( Path.Combine( rootFullName, file ) );
		string path = Path.GetDirectoryName( fullname );

		CleanStaleSubFiles( fullname, subFiles );

		foreach ( KeyValuePair<string, string> sub in subFiles )
		{
			WriteIfChanged( Path.Combine( path, sub.Key ), sub.Value );
		}

		Log.WriteLine( $"Writing File {file}" );

		WriteIfChanged( fullname, mainContent );
	}

	private static void WriteIfChanged( string filename, string text )
	{
		text = text.Replace( "(  )", "()" ); // pet peeve

		// Don't rewrite a file that's already up to date
		if ( File.Exists( filename ) && File.ReadAllText( filename ) == text )
		{
			return;
		}

		File.WriteAllText( filename, text );
	}

	/// <summary>
	/// Delete any children of this filename. Children of interop.hammer.h look like interop.hammer.childname.h.
	/// We clear them out incase they're left over from a previous run - since they're quite messy they might be
	/// and we'll be in the situation of our code #including stuff that only exists on our local computer.
	/// </summary>
	private static void CleanStaleSubFiles( string filename, IReadOnlyDictionary<string, string> subFiles )
	{
		string folder = Path.GetDirectoryName( filename );
		string fileName = Path.GetFileNameWithoutExtension( filename );
		string extension = Path.GetExtension( filename );

		foreach ( string file in Directory.EnumerateFiles( folder, $"{fileName}.*{extension}" ) )
		{
			// Don't delete active subfiles, that way we can compare and not rewrite them
			if ( subFiles.ContainsKey( Path.GetFileName( file ) ) )
			{
				continue;
			}

			File.Delete( file );
		}
	}
}
