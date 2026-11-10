using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
	public static bool ProcessDefinitionFile( string filename, bool skipNative )
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
				return true;
			}
			catch ( Exception e )
			{
				Log.Completion( $"Error: {e}", false );
				return false;
			}
		}
	}

	/// <summary>
	/// The tool's entry point. Reads manifest.def in the given directory and processes every listed
	/// .def file in parallel. Reports failure if the manifest is missing or any definition fails.
	/// Progress callbacks are serialized and count completed definitions, including failures.
	/// </summary>
	public static bool ProcessManifest( string directory, bool skipNative = false, Action<int, int> progress = null )
	{
		string filename = System.IO.Path.Combine( directory, "manifest.def" );
		if ( !System.IO.File.Exists( filename ) )
		{
			Log.Warning( $"Manifest not found: {filename}" );
			return false;
		}

		var paths = System.IO.File.ReadAllLines( filename )
			.Select( line => line.Trim() )
			.Where( line => line.EndsWith( ".def" ) )
			.Select( line => System.IO.Path.Combine( directory, line ) )
			.ToArray();
		List<Task<bool>> tasks = [];
		var progressLock = new object();
		var completed = 0;
		progress?.Invoke( completed, paths.Length );

		foreach ( string path in paths )
		{
			tasks.Add( Task.Run( () =>
			{
				var success = ProcessDefinitionFile( path, skipNative );
				lock ( progressLock )
				{
					progress?.Invoke( ++completed, paths.Length );
				}
				return success;
			} ) );
		}

		Task.WaitAll( tasks.ToArray() );
		return tasks.All( task => task.Result );
	}
}
