using Sandbox;
using Sandbox.Tasks;
using System;
using System.IO;
using System.Linq;

namespace Facepunch.MenuBuild;

class Program
{
	[STAThread]
	public static int Main( string[] args )
	{
		var projectPath = GetArgument( args, "--project" );
		if ( projectPath is not null )
		{
			var outputPath = GetArgument( args, "--out" );
			if ( outputPath is null )
			{
				Console.Error.WriteLine( "MenuBuild: --project needs --out <dir> to write the compiled assembly to" );
				return 1;
			}

			return BuildProject( Path.GetFullPath( projectPath ), Path.GetFullPath( outputPath ) );
		}

		using ( new ToolAppSystem() )
		{
			Project.AddFromFileBuiltIn( "addons/tools/.sbproj" );
			Project.AddFromFileBuiltIn( "editor/ActionGraph/.sbproj" );
			Project.AddFromFileBuiltIn( "editor/ShaderGraph/.sbproj" );
			Project.AddFromFileBuiltIn( "editor/MovieMaker/.sbproj" );
			Project.AddFromFileBuiltIn( "editor/Hammer/.sbproj" );
			Project.AddFromFileBuiltIn( "editor/DooEditor/DooEditor.sbproj" );
			var menuProject = Project.AddFromFile( "addons/menu/.sbproj" );

			SyncContext.RunBlocking( Project.CompileAsync() );

			var outputPath = Path.Combine( menuProject.GetRootPath(), ".bin" );
			if ( !CopyCompilerOutput( menuProject, outputPath ) ) return 1;
		}

		return 0;
	}

	static string GetArgument( string[] args, string name )
	{
		var i = Array.IndexOf( args, name );
		return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
	}

	/// <summary>
	/// Compiles a single project's game code (not its editor code) the same way the client would
	/// on load, and writes the assembly into <paramref name="outputPath"/>. Used to precompile mounts.
	/// </summary>
	static int BuildProject( string projectPath, string outputPath )
	{
		// ToolAppSystem moves the working directory to game/, resolve paths before it does
		using ( new ToolAppSystem() )
		{
			var project = Project.AddFromFile( projectPath );

			if ( project.Compiler is null )
			{
				Console.Error.WriteLine( $"MenuBuild: {projectPath} has no code to compile" );
				return 1;
			}

			SyncContext.RunBlocking( Project.CompileAsync() );

			if ( !CopyCompilerOutput( project, outputPath ) )
			{
				Console.Error.WriteLine( $"MenuBuild: {project.Config.FullIdent} failed to compile" );
				return 1;
			}

			Console.WriteLine( $"MenuBuild: compiled to {outputPath}" );
		}

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
