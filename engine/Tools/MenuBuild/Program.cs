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
			var baseProject = Project.AddFromFileBuiltIn( "addons/base/.sbproj" );
			Project.AddFromFileBuiltIn( "addons/tools/.sbproj" );
			Project.AddFromFileBuiltIn( "editor/ActionGraph/.sbproj" );
			Project.AddFromFileBuiltIn( "editor/ShaderGraph/.sbproj" );
			Project.AddFromFileBuiltIn( "editor/MovieMaker/.sbproj" );
			Project.AddFromFileBuiltIn( "editor/Hammer/.sbproj" );
			Project.AddFromFileBuiltIn( "editor/DooEditor/DooEditor.sbproj" );
			var menuProject = Project.AddFromFile( "addons/menu/.sbproj" );

			SyncContext.RunBlocking( Project.CompileAsync() );

			CopyCompilerOutput( baseProject );
			CopyCompilerOutput( menuProject );
		}

		return 0;
	}

	static void CopyCompilerOutput( Project project )
	{
		foreach ( var assembly in project.AssemblyFileSystem.FindFile( "", "*.dll", true ) )
		{
			var bytes = project.AssemblyFileSystem.ReadAllBytes( assembly ).ToArray();
			var outputPath = Path.Combine( project.GetRootPath(), assembly );
			System.IO.Directory.CreateDirectory( Path.GetDirectoryName( outputPath ) );
			System.IO.File.WriteAllBytes( outputPath, bytes );
		}
	}
}
