using Sandbox;
using Sandbox.Tasks;
using System;
using System.IO;

namespace Facepunch.MenuBuild;

class Program
{
	[STAThread]
	public static int Main( string[] args )
	{
		var projectPath = GetArgument( args, "--project" ) ?? "addons/menu/.sbproj";
		var outputPath = GetArgument( args, "--out" );

		if ( !string.IsNullOrEmpty( outputPath ) )
		{
			outputPath = Path.GetFullPath( outputPath );
		}

		using var appSystem = new ToolAppSystem();
		return BuildProject( Path.GetFullPath( projectPath ), outputPath );
	}

	static string GetArgument( string[] args, string name )
	{
		var i = Array.IndexOf( args, name );
		return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
	}

	/// <summary>
	/// Compiles a single project's game code (not its editor code) the same way the client would
	/// on load, and writes the assembly into <paramref name="outputPath"/>.
	/// </summary>
	static int BuildProject( string projectPath, string outputPath = null )
	{
		var project = Project.AddFromFile( projectPath );
		outputPath ??= Path.Combine( project.GetRootPath(), ".bin" );

		var compiler = project.Compiler;
		if ( compiler is null )
		{
			Console.Error.WriteLine( $"MenuBuild: {projectPath} has no code to compile" );
			return 1;
		}

		var settings = compiler.GetConfiguration() with
		{
			ReleaseMode = Compiler.ReleaseMode.Release
		};
		compiler.SetConfiguration( settings );

		bool success = SyncContext.RunBlocking( Project.CompileAsync() );
		if ( !success )
		{
			Console.Error.WriteLine( $"MenuBuild: {project.Config.FullIdent} failed to compile" );
			return 1;
		}

		if ( !CopyCompilerOutput( project, outputPath ) )
			return 1;

		Console.WriteLine( $"MenuBuild: compiled to {outputPath}" );
		return 0;
	}

	/// <summary>Copies a project's assemblies to its .bin. False when there were none.</summary>
	static bool CopyCompilerOutput( Project project, string outputPath )
	{
		var copied = 0;

		foreach ( var assembly in project.AssemblyFileSystem.FindFile( "", "*.dll", true ) )
		{
			var bytes = project.AssemblyFileSystem.ReadAllBytes( assembly ).ToArray();
			System.IO.Directory.CreateDirectory( outputPath );
			System.IO.File.WriteAllBytes( Path.Combine( outputPath, Path.GetFileName( assembly ) ), bytes );
			copied++;
		}

		if ( copied == 0 )
		{
			Console.Error.WriteLine(
				$"MenuBuild: {project.Config?.FullIdent ?? project.GetRootPath()} compiled no assemblies - " +
				$"nothing to copy to {Path.Combine( project.GetRootPath(), ".bin" )}" );
			return false;
		}

		Console.WriteLine( $"MenuBuild: copied {copied} assemblies for {project.Config?.FullIdent}" );
		return true;
	}
}
