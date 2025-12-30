using Sandbox.SolutionGenerator;
using System.IO;
using System.Text.RegularExpressions;

namespace Sandbox;

/// <summary>
/// Represents an on-disk project.
/// </summary>
public sealed partial class Project
{
	bool AddonTypeUsesCode()
	{
		if ( Config.Type == "game" ) return true;
		if ( Config.Type == "tool" ) return true;
		if ( Config.Type == "library" ) return true;
		if ( Config.Type == "addon" ) return true;

		return false;
	}

	internal async Task GenerateProject( Sandbox.SolutionGenerator.Generator generator )
	{
		// justify the async
		await Task.Yield();

		if ( !AddonTypeUsesCode() )
			return;

		if ( Config.Directory is null )
			return;

		var projectName = Config.GetMetaOrDefault( "CsProjName", Config.Ident );
		if ( !Regex.IsMatch( projectName, @"^[a-zA-Z\.\ \-_0-9]{1,32}$" ) )
		{
			if ( !string.IsNullOrWhiteSpace( projectName ) )
			{
				Log.Warning( $"Project name '{projectName}' is invalid - reverting to ident" );
			}

			projectName = Config.Ident;
		}

		string projectFolder = Config.Type == "library" ? "Libraries" : $"{Config.Type.ToTitleCase()}s";
		if ( projectFolder == "Games" ) projectFolder = null;

		ProjectInfo project = null;

		if ( HasCodePath() )
		{
			//
			// Code project
			//
			var compilerSettings = Config.GetCompileSettings();

			if ( Config.Type == "game" )
			{
				compilerSettings.IgnoreFolders.Add( "editor" );
				compilerSettings.IgnoreFolders.Add( "unittest" );
			}

			project = generator.AddProject( Config.Type, Config.FullIdent, projectName, GetCodePath(), compilerSettings );
			project.Folder = projectFolder;
			project.SandboxProjectFilePath = ConfigFilePath;

			//
			// Add each reference to the project
			//
			foreach ( var reference in compilerSettings.DistinctAssemblyReferences )
			{
				project.References.Add( $"{reference}.dll" );
			}

			//
			// Server projects
			//
			if ( Config.Type == "game" || Config.Type == "library" )
			{
				var serverProject = AddServerProjectFrom( projectName, generator );
				if ( serverProject is not null )
				{
					serverProject.Folder = projectFolder;

					//
					// Hide server files from the main project
					//
					project.IgnoreFiles.Add( "**/*.Server.cs" );
				}
			}

			if ( Config.Type == "tool" )
			{
				project.References.Add( "Sandbox.Tools.dll" );
				project.References.Add( "Sandbox.Compiling.dll" );
				project.References.Add( "Microsoft.CodeAnalysis.dll" );
				project.References.Add( "Microsoft.CodeAnalysis.CSharp.dll" );
				project.References.Add( "Sandbox.Bind.dll" );
				project.References.Add( "Facepunch.ActionGraphs.dll" );
				project.References.Add( "SkiaSharp.dll" );
				project.GlobalStatic.Add( "Sandbox.Internal.GlobalToolsNamespace" );
				project.GlobalStatic.Add( "Sandbox.Internal.GlobalGameNamespace" );

				if ( Config.Ident != "toolbase" )
					project.PackageReferences.Add( "local.toolbase" );
			}
			else if ( Config.Type == "game" || Config.Type == "library" )
			{
				project.GlobalUsing.Add( "Microsoft.AspNetCore.Components" );
				project.GlobalUsing.Add( "Microsoft.AspNetCore.Components.Rendering" );
				project.GlobalStatic.Add( "Sandbox.Internal.GlobalGameNamespace" );

				if ( !project.PackageReferences.Contains( "local.base" ) )
				{
					project.PackageReferences.Add( "local.base" );
				}
			}

			if ( Config.Type == "game" )
			{
				AddLibrariesToProject( project );

			}
		}

		if ( Config.Type == "game" || Config.Type == "library" )
		{
			//
			// Editor project
			//
			var editorProject = AddEditorProjectFrom( projectName, generator );
			if ( editorProject is not null )
			{
				editorProject.Folder = projectFolder;
				editorProject.SandboxProjectFilePath = ConfigFilePath;

				if ( project is not null )
					editorProject.PackageReferences.Add( project.Name );
			}

			//
			// Unit test project
			//
			var testProject = AddUnitTestProjectFrom( projectName, generator );
			if ( testProject is not null )
			{
				testProject.Folder = projectFolder;

				if ( project is not null )
					testProject.PackageReferences.Add( project.Name );

				if ( editorProject is not null )
					testProject.PackageReferences.Add( editorProject.Name );
			}
		}
	}

	private void AddLibrariesToProject( ProjectInfo project )
	{
		foreach ( var library in Project.Libraries.Where( x => x.HasCodePath() ) )
		{
			project.PackageReferences.Add( library.Package.GetIdent( false, false ) );
		}
	}

	private bool HasServersideCode()
	{
		if ( !HasCodePath() )
			return false;

		var codePath = GetCodePath().Replace( '\\', '/' );

		//
		// Check for any .Server.cs files recursively
		//
		try
		{
			return Directory.EnumerateFiles( codePath, "*.Server.cs", SearchOption.AllDirectories ).Any();
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to check for server files in {codePath}: {e.Message}" );
			return false;
		}
	}

	ProjectInfo AddServerProjectFrom( string projectName, Sandbox.SolutionGenerator.Generator generator )
	{
		if ( !HasCodePath() )
			return default;

		if ( !HasServersideCode() )
			return default;

		var serverSettings = Config.GetCompileSettings();
		serverSettings.DefineConstants += ";SERVER";

		var project = generator.AddProject( Config.Type,
			$"{Config.FullIdent}.server",
			$"{projectName}.server",
			GetCodePath(),
			serverSettings );

		foreach ( var reference in serverSettings.DistinctAssemblyReferences )
		{
			project.References.Add( $"{reference}.dll" );
		}

		// Add standard references
		if ( Config.Type == "game" || Config.Type == "library" )
		{
			project.GlobalUsing.Add( "Microsoft.AspNetCore.Components" );
			project.GlobalUsing.Add( "Microsoft.AspNetCore.Components.Rendering" );
			project.GlobalStatic.Add( "Sandbox.Internal.GlobalGameNamespace" );

			if ( !project.PackageReferences.Contains( "local.base" ) )
			{
				project.PackageReferences.Add( "local.base" );
			}
		}

		if ( Config.Type == "game" )
		{
			AddLibrariesToProject( project );
		}

		return project;
	}

	ProjectInfo AddEditorProjectFrom( string projectName, Sandbox.SolutionGenerator.Generator generator )
	{
		if ( !HasEditorPath() )
			return default;

		var compilerSettings = Config.GetCompileSettings();
		var project = generator.AddProject( "tool", $"{Config.FullIdent}.editor", $"{projectName}.editor", GetEditorPath(), compilerSettings );
		project.IsEditorProject = true;

		//
		// Add each reference to the project
		//
		foreach ( var reference in compilerSettings.DistinctAssemblyReferences )
		{
			project.References.Add( $"{reference}.dll" );
		}

		if ( Config.Type == "game" )
		{
			// editor libraries
			foreach ( var library in Libraries.Where( x => x.HasEditorPath() ) )
			{
				project.PackageReferences.Add( $"{library.Package.GetIdent( false, false )}.editor" );
			}
		}

		// tool includes
		project.References.Add( "Sandbox.Tools.dll" );
		project.References.Add( "Sandbox.Compiling.dll" );
		project.References.Add( "Microsoft.CodeAnalysis.dll" );
		project.References.Add( "Microsoft.CodeAnalysis.CSharp.dll" );
		project.References.Add( "Sandbox.Bind.dll" );
		project.References.Add( "Facepunch.ActionGraphs.dll" );
		project.References.Add( "SkiaSharp.dll" );
		project.GlobalStatic.Add( "Sandbox.Internal.GlobalToolsNamespace" );
		project.GlobalStatic.Add( "Sandbox.Internal.GlobalGameNamespace" );
		project.PackageReferences.Add( "local.toolbase" );
		project.PackageReferences.Add( "actiongraph" );
		project.PackageReferences.Add( "shadergraph" );
		project.PackageReferences.Add( "hammer" );
		return project;
	}

	ProjectInfo AddUnitTestProjectFrom( string projectName, Sandbox.SolutionGenerator.Generator generator )
	{
		var dirinfo = new DirectoryInfo( System.IO.Path.Combine( GetRootPath(), "UnitTests" ) );
		if ( !dirinfo.Exists )
			return default;

		var compilerSettings = Config.GetCompileSettings();
		var project = generator.AddProject( "unittest", $"{Config.FullIdent}.unittest", $"{projectName}.unittest", dirinfo.FullName, compilerSettings );
		project.IsUnitTestProject = true;

		//
		// Add each reference to the project
		//
		foreach ( var reference in compilerSettings.DistinctAssemblyReferences )
		{
			project.References.Add( $"{reference}.dll" );
		}

		// tool includes
		project.References.Add( "Sandbox.Tools.dll" );
		project.References.Add( "Sandbox.Compiling.dll" );
		project.References.Add( "Microsoft.CodeAnalysis.dll" );
		project.References.Add( "Microsoft.CodeAnalysis.CSharp.dll" );
		project.References.Add( "Sandbox.Bind.dll" );
		project.GlobalStatic.Add( "Sandbox.Internal.GlobalToolsNamespace" );
		project.GlobalStatic.Add( "Sandbox.Internal.GlobalGameNamespace" );
		project.PackageReferences.Add( "local.toolbase" );

		return project;
	}
}
