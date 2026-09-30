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

			if ( !CopyCompilerOutput( menuProject ) ) return 1;
		}

		return 0;
	}

	/// <summary>Copies a project's assemblies to its .bin. False when there were none.</summary>
	static bool CopyCompilerOutput( Project project )
	{
		var copied = 0;

		foreach ( var assembly in project.AssemblyFileSystem.FindFile( "", "*.dll", true ) )
		{
			var bytes = project.AssemblyFileSystem.ReadAllBytes( assembly ).ToArray();
			var outputPath = Path.Combine( project.GetRootPath(), assembly );
			System.IO.Directory.CreateDirectory( Path.GetDirectoryName( outputPath ) );
			System.IO.File.WriteAllBytes( outputPath, bytes );
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
