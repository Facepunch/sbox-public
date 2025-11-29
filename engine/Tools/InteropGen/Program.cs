using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Facepunch.InteropGen;

public static class Program
{
	public static void ProcessDefinitionFile( int index, string filename, bool skipNative, bool aot = false )
	{
		using ( Log.Group( ConsoleColor.Green, $"{System.IO.Path.GetFileName( filename )}" ) )
		{
			Stopwatch sw = Stopwatch.StartNew();

			try
			{
				Definition definitions = Definition.FromFile( filename );

				if ( aot )
                {
                    Log.WriteLine( "Saving NativeAOT File" );
                    NativeAotWriter nativeAotWriter = new( definitions, definitions.SaveFileCsAot );
                    nativeAotWriter.Generate();
                    nativeAotWriter.SaveToFile( definitions.SaveFileCsAot );
                }
				else
				{
					// Log.WriteLine( "Saving Managed File" );
					ManagerWriter managedWriter = new( definitions, definitions.SaveFileCs );
					managedWriter.Generate();
					managedWriter.SaveToFile( definitions.SaveFileCs );

					if ( !skipNative )
					{
						// Log.WriteLine( "Saving Native Header" );
						NativeHeaderWriter nativeHeaderWriter = new( definitions, definitions.SaveFileCppH );
						nativeHeaderWriter.Generate();
						nativeHeaderWriter.SaveToFile( definitions.SaveFileCppH );

						// Log.WriteLine( "Saving Native" );
						NativeWriter nativeWriter = new( definitions, definitions.SaveFileCpp );
						nativeWriter.Generate();
						nativeWriter.SaveToFile( definitions.SaveFileCpp );
					}
				}


				Log.Completion( $"Done in {sw.Elapsed.TotalSeconds:0.00}s", true );
			}
			catch ( Exception e )
			{
				Log.Completion( $"Error: {e}", false );
			}
		}
	}

	public static void ProcessManifest( string directory, bool skipNative = false, bool aot = false )
	{
		string filename = System.IO.Path.Combine( directory, "manifest.def" );
		if ( !System.IO.File.Exists( filename ) )
		{
			return;
		}

		string[] manifestLines = System.IO.File.ReadAllLines( filename );
		List<Task> tasks = [];

		int i = 0;
		foreach ( string line in manifestLines )
		{
			if ( string.IsNullOrWhiteSpace( line ) )
			{
				continue;
			}

			if ( line.Trim().EndsWith( ".def" ) )
			{
				int index = i++;
				string path = System.IO.Path.Combine( directory, line );

				Task t = Task.Run( () => ProcessDefinitionFile( index, path, skipNative, aot ) );
				tasks.Add( t );
			}
		}

		Task.WaitAll( tasks.ToArray() );
	}
}
