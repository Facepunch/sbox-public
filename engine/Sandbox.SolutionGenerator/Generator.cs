using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Sandbox.SolutionGenerator
{
	public class Generator
	{
		List<ProjectInfo> Projects = new();

		public ProjectInfo AddProject( string type, string packageIdent, string name, string path, Compiler.Configuration settings )
		{
			var project = new ProjectInfo( type, packageIdent, name, path, settings );
			Projects.Add( project );
			return project;
		}

		/// <summary>
		/// Normalize the path to use forward slashes
		/// </summary>
		private string NormalizePath( string path )
		{
			return path.Replace( '\\', '/' );
		}

		/// <summary>
		/// Converts a path to be relative to a base path, always returning forward slashes.
		/// </summary>
		private string AttemptAbsoluteToRelative( string basePath, string targetPath )
		{
			string targetFileName = string.Empty;

			if ( Path.HasExtension( targetPath ) )
			{
				targetFileName = Path.GetFileName( targetPath );
				targetPath = Path.GetDirectoryName( targetPath ) ?? targetPath;
			}

			if ( Path.HasExtension( basePath ) )
			{
				basePath = Path.GetDirectoryName( basePath ) ?? basePath;
			}

			string baseDir = NativeFileSystem.GetCanonicalPath( basePath );
			string targetDir = NativeFileSystem.GetCanonicalPath( targetPath );

			string relativePath = NormalizePath( Path.GetRelativePath( baseDir, targetDir ) );

			if ( string.IsNullOrEmpty( targetFileName ) )
				return relativePath;

			return relativePath + "/" + targetFileName;
		}

		private static readonly JsonSerializerOptions JsonWriteIndented = new() { WriteIndented = true };

		public void Run( string gameExePath, string managedFolder, string solutionPath, string relativePath, string projectPath )
		{
			managedFolder = Path.Combine( relativePath, managedFolder );
			solutionPath = Path.Combine( projectPath, solutionPath );
			gameExePath = Path.Combine( relativePath, gameExePath );

			foreach ( var p in Projects )
			{
				var csproj = new Project
				{
					ProjectName = p.Name,
					ProjectReferences = "",
					ManagedRoot = AttemptAbsoluteToRelative( p.CsprojPath, managedFolder ),
					GameRoot = AttemptAbsoluteToRelative( p.CsprojPath, relativePath ),
					References = p.References,
					GlobalStatic = p.GlobalStatic,
					GlobalUsing = p.GlobalUsing,
					RootNamespace = p.Settings.RootNamespace ?? "Sandbox",
					Nullable = p.Settings.Nullables ? "enable" : "disable",
					NoWarn = p.Settings.NoWarn,
					WarningsAsErrors = p.Settings.WarningsAsErrors,
					TreatWarningsAsErrors = p.Settings.TreatWarningsAsErrors,
					DefineConstants = p.Settings.DefineConstants,
					Unsafe = p.Type == "tool",
					IgnoreFolders = p.Settings.IgnoreFolders.ToList(),
					IsEditorProject = p.IsEditorProject,
					IsUnitTestProject = p.IsUnitTestProject,
					IgnoreFiles = p.IgnoreFiles
				};

				foreach ( var proj in p.PackageReferences.Distinct().Order() )
				{
					if ( proj.Contains( "\\" ) )
					{
						csproj.ProjectReferences += $"		<Reference Include=\"{proj}\" />\n";
						continue;
					}

					var reference = Projects.FirstOrDefault( x => x.Name == proj || x.PackageIdent == proj );
					if ( reference != null )
					{
						var absolutePath = NormalizePath( $"{reference.Path}/{reference.Name}.csproj" );
						var path = AttemptAbsoluteToRelative( p.CsprojPath, absolutePath );
						csproj.ProjectReferences += $"		<ProjectReference Include=\"{System.Web.HttpUtility.HtmlEncode( path )}\" />\n";
					}
					else
					{
						csproj.ProjectReferences += $"		<!-- Couldn't find project '{proj}' for {csproj.ProjectName} to reference -->\" />\n";
						new Sandbox.Diagnostics.Logger( "SolutionGenerator" ).Warning( $"Couldn't find project '{proj}' for {csproj.ProjectName} to reference" );
					}
				}

				WriteTextIfChanged( p.CsprojPath, csproj.TransformText() );

				if ( gameExePath != null && !p.IsUnitTestProject )
				{
					var propertiesPath = Path.Combine( p.Path, "Properties" );
					Directory.CreateDirectory( propertiesPath );

					var absoluteExePath = Path.Combine( relativePath, "sbox-dev.exe" );
					var relativeExePath = AttemptAbsoluteToRelative( propertiesPath, absoluteExePath );

					var launchSettings = new LaunchSettings { Profiles = new() };
					launchSettings.Profiles.Add( "Editor", new LaunchSettings.Profile
					{
						CommandName = "Executable",
						ExecutablePath = relativeExePath,
						CommandLineArgs = $"-project \"{p.SandboxProjectFilePath}\"",
					} );

					WriteTextIfChanged( Path.Combine( propertiesPath, "launchSettings.json" ), JsonSerializer.Serialize( launchSettings, JsonWriteIndented ) );
				}
			}

			// Build solution
			var slnx = new Solution();

			foreach ( var p in Projects )
			{
				string normalizedProjectPath = AttemptAbsoluteToRelative( solutionPath, p.CsprojPath );
				normalizedProjectPath = normalizedProjectPath.Trim( '/', '\\' );
				slnx.AddProject( normalizedProjectPath, p.Folder );
			}

			WriteTextIfChanged( solutionPath, slnx.Generate() );
		}

		private static void WriteTextIfChanged( string path, string contents )
		{
			try
			{
				if ( File.Exists( path ) )
				{
					var existingContents = File.ReadAllText( path );
					if ( contents == existingContents )
						return;
				}
			}
			catch { }

			var folder = Path.GetDirectoryName( path );
			if ( !string.IsNullOrEmpty( folder ) && !Directory.Exists( folder ) )
				Directory.CreateDirectory( folder );

			File.WriteAllText( path, contents );
		}
	}
}
