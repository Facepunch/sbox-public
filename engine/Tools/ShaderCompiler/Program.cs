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
		var options = new ShaderCompileOptions();
		options.ForceRecompile = args.Any( x => x.Contains( "-f" ) );
		options.SingleThreaded = args.Any( x => x.Contains( "-s" ) );
		options.ConsoleOutput = !args.Any( x => x.Contains( "-q" ) );

		// Recompile mode: bring already-compiled .shader_c files up to the current VCS version using the HLSL
		// source embedded in them - no .shader source file needed. This is what the backend shader-recompile
		// worker calls; published packages ship only .shader_c, so the normal "*.shader" build path can't help.
		if ( args.Any( x => x.Equals( "--recompile", StringComparison.OrdinalIgnoreCase ) ) )
		{
			options.ForceRecompile = true; // there's no source .shader to diff against, so always recompile
			return Recompile( args, options );
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
				var result = SyncContext.RunBlocking( ShaderCompile.Compile( c.AbsolutePath, c.RelativePath, options, default ) );

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
			MainThread.RunQueues();

			// Sucks that we need to do this, but lets Hard-exit before ToolAppSystem.Dispose() triggers native engine teardown, to avoid crashses.
			// Compilation is done at this point so a clean process exit is safe and avoids the whole teardown path.
			NativeEngine.EngineGlobal.Plat_ExitProcess( failedList.Any() ? 1 : 0 );
		}

		return failedList.Any() ? 1 : 0;
	}

	/// <summary>
	/// Recompile already-compiled <c>.shader_c</c> files in place from their embedded source. Each path passed
	/// on the command line (any non-flag arg) is recompiled and overwritten on success. Returns non-zero if any
	/// file failed.
	/// </summary>
	static int Recompile( string[] args, ShaderCompileOptions options )
	{
		var files = args
			.Where( a => !a.StartsWith( "-" ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.ToList();

		if ( files.Count == 0 )
		{
			Console.Error.WriteLine( "Usage: ShaderCompiler --recompile <file.shader_c> [more.shader_c ...]" );
			return 1;
		}

		int failed = 0;

		using ( new ToolAppSystem() )
		{
			foreach ( var file in files )
			{
				var absolute = System.IO.Path.GetFullPath( file );

				if ( !System.IO.File.Exists( absolute ) )
				{
					Console.Error.WriteLine( $"File not found: {absolute}" );
					failed++;
					continue;
				}

				try
				{
					var result = SyncContext.RunBlocking( ShaderCompile.RecompileFromCompiled( absolute, options, default ) );

					if ( !result.Success || result.CompiledShader is null )
					{
						Console.Error.WriteLine( $"Failed to recompile {absolute}" );
						failed++;
						continue;
					}

					System.IO.File.WriteAllBytes( absolute, result.CompiledShader );

					if ( options.ConsoleOutput )
					{
						Console.ForegroundColor = ConsoleColor.Cyan;
						Console.WriteLine( $"Recompiled {absolute} ({result.CompiledShader.Length:n0} bytes)" );
						Console.ForegroundColor = ConsoleColor.White;
					}
				}
				catch ( System.Exception e )
				{
					Console.Error.WriteLine( $"Error recompiling {absolute}: {e.Message}" );
					failed++;
				}
			}

			// Hard-exit before ToolAppSystem.Dispose() triggers native teardown, same as the main path.
			NativeEngine.EngineGlobal.Plat_ExitProcess( failed > 0 ? 1 : 0 );
		}

		return failed > 0 ? 1 : 0;
	}
}

record struct ProcessList( string RelativePath, string AbsolutePath );
