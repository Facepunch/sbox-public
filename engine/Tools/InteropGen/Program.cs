using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Facepunch.InteropGen;

/// <summary>
/// Entry point for the interop generator, invoked by the build (Tools/SboxBuild). Reads a manifest of
/// .def files and turns each into its managed (.cs) and native (.cpp/.h) bindings.
/// </summary>
public static class Program
{
	/// <summary>
	/// Build one .def file and write its managed output (and, unless <paramref name="skipNative"/>, its
	/// native header and source), logging how long it took.
	/// </summary>
	public static void ProcessDefinitionFile( string filename, bool skipNative )
	{
		using ( Log.Group( ConsoleColor.Green, $"{System.IO.Path.GetFileName( filename )}" ) )
		{
			Stopwatch sw = Stopwatch.StartNew();

			try
			{
				Definition definitions = InteropPipeline.Build( filename );

				ManagedWriter managedWriter = new( definitions, definitions.SaveFileCs );
				managedWriter.Generate();
				managedWriter.SaveToFile( definitions.SaveFileCs );

				if ( !skipNative )
				{
					NativeHeaderWriter nativeHeaderWriter = new( definitions, definitions.SaveFileCppH );
					nativeHeaderWriter.Generate();
					nativeHeaderWriter.SaveToFile( definitions.SaveFileCppH );

					NativeWriter nativeWriter = new( definitions, definitions.SaveFileCpp );
					nativeWriter.Generate();
					nativeWriter.SaveToFile( definitions.SaveFileCpp );
				}

				Log.Completion( $"Done in {sw.Elapsed.TotalSeconds:0.00}s", true );
			}
			catch ( Exception e )
			{
				Log.Completion( $"Error: {e}", false );
			}
		}
	}

	/// <summary>
	/// The tool's entry point. Reads manifest.def in the given directory and processes every listed
	/// .def file in parallel. Does nothing if there's no manifest.
	/// </summary>
	public static void ProcessManifest( string directory, bool skipNative = false )
	{
		string filename = System.IO.Path.Combine( directory, "manifest.def" );
		if ( !System.IO.File.Exists( filename ) )
		{
			return;
		}

		List<Task> tasks = [];

		foreach ( string line in System.IO.File.ReadAllLines( filename ) )
		{
			if ( string.IsNullOrWhiteSpace( line ) )
			{
				continue;
			}

			if ( !line.Trim().EndsWith( ".def" ) )
			{
				continue;
			}

			string path = System.IO.Path.Combine( directory, line );
			tasks.Add( Task.Run( () => ProcessDefinitionFile( path, skipNative ) ) );
		}

		Task.WaitAll( tasks.ToArray() );
	}
}
