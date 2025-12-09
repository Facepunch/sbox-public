using Sandbox;
using Sandbox.Diagnostics;
using Sandbox.Engine.Shaders;
using Sandbox.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Facepunch.ShaderCompiler;

public static partial class Program
{
	[STAThread]
	public static int Main( string[] args )
	{
		// Ensure main thread is registered for SyncContext assertions (NativeAOT/CLI)
		// ThreadSafe.MarkMainThread is internal; set the thread-static flag via reflection.
		var mark = typeof( ThreadSafe ).GetMethod( "MarkMainThread", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );
		mark?.Invoke( null, null );
		var field = typeof( ThreadSafe ).GetField( "isMainThread", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );
		field?.SetValue( null, true );
		SyncContext.Init();

		var options = new ShaderCompileOptions();
		options.ForceRecompile = args.Any( x => x.Contains( "-f" ) );
		options.SingleThreaded = args.Any( x => x.Contains( "-s" ) );
		options.ConsoleOutput = !args.Any( x => x.Contains( "-q" ) );
		var useOpenEngine = args.Any( x => x.Contains( "-openengine" ) );
		if ( useOpenEngine )
		{
			Environment.SetEnvironmentVariable( "SBOX_OPENENGINE_VFX", "1" );
		}

		List<ProcessList> failedList = new();

		HashSet<string> files = new();

		for ( int i = 0; i < args.Length; i++ )
		{
			var arg = args[i];
			if ( arg.StartsWith( "-" ) ) continue;

			files.Add( arg );
		}

		if ( files.Count == 0 )
		{
			files.Add( "*" );
			options.ForceRecompile = false;
		}

		using ( new ToolAppSystem() )
		{
			List<ProcessList> compileList = new();

			var wd = System.IO.Directory.GetCurrentDirectory();

			foreach ( var s in System.IO.Directory.EnumerateFiles( wd, "*.shader", new System.IO.EnumerationOptions { RecurseSubdirectories = true } ) )
			{
				if ( !files.Contains( s, StringComparer.OrdinalIgnoreCase ) && !files.Contains( "*" ) ) continue;

				// skip all the BS in junk folders
				if ( s.Contains( "\\download\\" ) ) continue;
				if ( s.Contains( "\\templates\\" ) ) continue;
				if ( s.Contains( "\\." ) ) continue;

				var relative = System.IO.Path.GetRelativePath( wd, s );
				var p = new ProcessList( relative, s );
				compileList.Add( p );
			}

			var totalTimer = FastTimer.StartNew();

			int iCount = 0;

			foreach ( var c in compileList )
			{
				iCount++;

				if ( options.ConsoleOutput )
				{
					Console.ForegroundColor = ConsoleColor.White;
					Console.WriteLine( $"({iCount}/{compileList.Count}) {c.RelativePath}" );
				}

				FastTimer fastTimer = FastTimer.StartNew();
				// Avoid main-thread assertion in RunBlocking for CLI: wait synchronously.
				var result = ShaderCompile.Compile( c.AbsolutePath, c.RelativePath, options, default ).GetAwaiter().GetResult();

				if ( !result.Success )
				{
					failedList.Add( c );
				}

				if ( options.ConsoleOutput )
				{
					if ( !result.Success )
					{
						Console.ForegroundColor = ConsoleColor.Red;
						Console.WriteLine( $"	Compile failed." );
						Console.ForegroundColor = ConsoleColor.White;
					}
					else
					{
						Console.ForegroundColor = ConsoleColor.Cyan;
						if ( result.Skipped )
						{
							Console.WriteLine( $"	Skipped, up to date." );
						}
						else
						{
							Console.WriteLine( $"	Compiled successfully in {fastTimer.Elapsed.Humanize( 3 )}." );
						}
						Console.ForegroundColor = ConsoleColor.White;
					}
				}
			}

			Console.ForegroundColor = ConsoleColor.Cyan;
			Console.WriteLine( $"Finished in {totalTimer.Elapsed.Humanize( 3 )}" );

			if ( failedList.Any() )
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine( $"Failed to build {failedList.Count} shaders!" );
				foreach ( var c in failedList )
				{
					Console.WriteLine( $"	{c.AbsolutePath}" );
				}
			}

			Console.ForegroundColor = ConsoleColor.White;

			// Shitty cleanup our garbage before exiting
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}

		return failedList.Any() ? 1 : 0;
	}
}

record struct ProcessList( string RelativePath, string AbsolutePath );
