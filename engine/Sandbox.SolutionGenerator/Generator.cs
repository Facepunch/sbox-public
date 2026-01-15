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

		private string NormalizePath( string path ) => new Uri( path ).LocalPath;

		private string AttemptAbsoluteToRelative( string basePath, string relativePath, int maxDepth = 5 )
		{
			string baseNormalized = NormalizePath( basePath );
			string relativeNormalized = NormalizePath( relativePath );
			string baseEnding = string.Empty;

			if ( Path.HasExtension( baseNormalized ) )
			{
				baseEnding = Path.GetFileName( baseNormalized );
				baseNormalized = NormalizePath( baseNormalized.Substring( 0, baseNormalized.Length - baseEnding.Length ) );
			}

			if ( Path.HasExtension( relativeNormalized ) )
			{
				relativeNormalized = Path.GetDirectoryName( relativeNormalized );
			}

			string finalPath = Path.GetRelativePath( relativeNormalized, basePath );

			// Exceed how far we want our relative path to go, bail out and use original path
			if ( finalPath.Split( ".." ).Length > maxDepth )
			{
				if ( baseEnding == null )
				{
					return baseNormalized;
				}
				else
				{
					return Path.Combine( baseNormalized, baseEnding );
				}
			}
			else
			{
				if ( baseEnding == null )
				{
					return finalPath;
				}
				else
				{
					return Path.Combine( finalPath, baseEnding );
				}
			}
		}

		private static readonly JsonSerializerOptions JsonWriteIndented = new() { WriteIndented = true };

		public void Run( string gameExePath, string managedFolder, string solutionPath, string relativePath, string projectPath )
		{
			string normalizedRelativePath = NormalizePath( projectPath );
			int relativePathoffset = normalizedRelativePath.Length + 1;

			managedFolder = Path.Combine( relativePath, managedFolder );
			solutionPath = Path.Combine( projectPath, solutionPath );
			gameExePath = Path.Combine( relativePath, gameExePath );

			foreach ( var p in Projects )
			{
				var csproj = new Project
				{
					ProjectName = p.Name,
					ProjectReferences = "",
					ManagedRoot = AttemptAbsoluteToRelative( managedFolder, p.CsprojPath ),
					GameRoot = AttemptAbsoluteToRelative( relativePath, p.CsprojPath ),
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
						var path = NormalizePath( $"{reference.Path}\\{reference.Name}.csproj" );
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

					var launchSettings = new LaunchSettings { Profiles = new() };
					launchSettings.Profiles.Add( "Editor", new LaunchSettings.Profile
					{
						CommandName = "Executable",
						ExecutablePath = Path.Combine( relativePath, "sbox-dev.exe" ),
						CommandLineArgs = $"-project \"{p.SandboxProjectFilePath}\"",
					} );

					WriteTextIfChanged( Path.Combine( propertiesPath, "launchSettings.json" ), JsonSerializer.Serialize( launchSettings, JsonWriteIndented ) );
				}
			}

			// Build solution
			var slnx = new Solution();

			foreach ( var p in Projects )
			{
				string normalizedProjectPath = NormalizePath( p.CsprojPath );
				if ( normalizedProjectPath.StartsWith( normalizedRelativePath ) )
				{
					normalizedProjectPath = normalizedProjectPath.Substring( relativePathoffset );
				}

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
