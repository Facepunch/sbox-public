using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Facepunch.InteropGen;

public static class Program
{
	public static void ProcessDefinitionFile( int index, string filename, bool skipNative )
	{
		using ( Log.Group( ConsoleColor.Green, $"{System.IO.Path.GetFileName( filename )}" ) )
		{
			Stopwatch sw = Stopwatch.StartNew();

			try
			{
				Definition definitions = Definition.FromFile( filename );

				// Détection automatique de l’OS
				bool isLinux = RuntimeInformation.IsOSPlatform( OSPlatform.Linux );

				if ( isLinux )
				{
					Log.WriteLine( "Detected Linux → Saving NativeAOT File" );

					var nativeAotWriter = new NativeAotWriter( definitions, definitions.SaveFileCsAot );
					nativeAotWriter.Generate();
					nativeAotWriter.SaveToFile( definitions.SaveFileCsAot );
				}
				else
				{
					Log.WriteLine( "Detected non-Linux → Saving Managed + Native C++ Files" );

					var managedWriter = new ManagerWriter( definitions, definitions.SaveFileCs );
					managedWriter.Generate();
					managedWriter.SaveToFile( definitions.SaveFileCs );

					if ( !skipNative )
					{
						var nativeHeaderWriter = new NativeHeaderWriter( definitions, definitions.SaveFileCppH );
						nativeHeaderWriter.Generate();
						nativeHeaderWriter.SaveToFile( definitions.SaveFileCppH );

						var nativeWriter = new NativeWriter( definitions, definitions.SaveFileCpp );
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

	public static void ProcessManifest( string directory, bool skipNative = false )
	{
		string filename = System.IO.Path.Combine( directory, "manifest.def" );
		if ( !System.IO.File.Exists( filename ) )
		{
			return;
		}

		var manifestLines = File.ReadAllLines( filename );
		var tasks = new List<Task>();

		int i = 0;
		foreach ( string line in manifestLines )
		{
			if ( string.IsNullOrWhiteSpace( line ) )
				continue;

			if ( line.Trim().EndsWith( ".def" ) )
			{
				int index = i++;
				string path = Path.Combine( directory, line );

				tasks.Add( Task.Run( () =>
					ProcessDefinitionFile( index, path, skipNative )
				));
			}
		}

		Task.WaitAll( tasks.ToArray() );
	}
}
