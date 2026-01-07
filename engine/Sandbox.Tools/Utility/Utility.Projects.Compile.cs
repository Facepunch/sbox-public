using System;
using System.Reflection;

namespace Editor;

public static partial class EditorUtility
{
	public static partial class Projects
	{
		/// <inheritdoc cref="Compile(Project, Compiler.Configuration, Action{string})"/>
		public static async Task<CompilerOutput[]> Compile( Project project, Action<string> logOutput )
			=> await Compile( project, project.Config.GetCompileSettings(), logOutput );

		/// <summary>
		/// Compiled a project ready to be published. Will return the compiled result and any CodeArchives
		/// </summary>
		internal static async Task<CompilerOutput[]> Compile( Project project, Compiler.Configuration compilerSettings, Action<string> logOutput )
		{
			bool isGame = project.Config.Type == "game";
			var codePath = project.GetCodePath();

			if ( !System.IO.Directory.Exists( codePath ) )
				return default;

			using CompileGroup compileGroup = new CompileGroup( "Publish" );
			compileGroup.AccessControl = PackageManager.AccessControl;

			compilerSettings.IgnoreFolders.Add( "editor" );
			compilerSettings.IgnoreFolders.Add( "unittest" );
			compilerSettings.ReleaseMode = Compiler.ReleaseMode.Release;
			compilerSettings.StripDisabledTextTrivia = true;

			var compiler = compileGroup.CreateCompiler( project.Package.CompilerName, codePath, compilerSettings );
			compiler.UseAbsoluteSourcePaths = false;

			//Log.Info( $"Code Path: {addon.GetCodePath()}" );

			bool hasBase = false;

			//
			// Install any libraries (unless we are a library)
			//
			if ( project.Config.Type != "library" )
			{
				foreach ( var (library, references) in SortLibrariesForCompilation( Project.Libraries, logOutput ) )
				{
					await PackageManager.InstallAsync( new PackageLoadOptions( library.Package.FullIdent, "publish" ) );

					// Compile library
					{
						var compileSettings = new Compiler.Configuration();
						compileSettings.Clean();
						compileSettings.ReleaseMode = Compiler.ReleaseMode.Release;

						var libCompiler = compileGroup.CreateCompiler( library.Package.CompilerName, library.GetCodePath(), compileSettings );
						libCompiler.UseAbsoluteSourcePaths = false;
						libCompiler.GeneratedCode.AppendLine( "global using static Sandbox.Internal.GlobalGameNamespace;" );

						// Required by razor
						libCompiler.GeneratedCode.AppendLine( "global using Microsoft.AspNetCore.Components;" );
						libCompiler.GeneratedCode.AppendLine( "global using Microsoft.AspNetCore.Components.Rendering;" );

						libCompiler.AddBaseReference();

						foreach ( var reference in references )
						{
							libCompiler.AddReference( reference.Package );
						}
					}

					// add a reference to it from the main compiler
					{
						compiler.AddReference( library.Package );
					}
				}
			}

			//
			// if we're a game or an addon then put the base code in the base
			//
			if ( !hasBase )
			{
				var baseSettings = new Compiler.Configuration();
				baseSettings.Clean();
				baseSettings.ReleaseMode = Compiler.ReleaseMode.Release;

				logOutput?.Invoke( "Adding package.base to compiler" );

				var baseCompiler = compileGroup.CreateCompiler( "base", EngineFileSystem.Root.GetFullPath( "/addons/base/code/" ), baseSettings );
				baseCompiler.UseAbsoluteSourcePaths = false;
				baseCompiler.GeneratedCode.AppendLine( "global using static Sandbox.Internal.GlobalGameNamespace;" );

				// Required by razor
				baseCompiler.GeneratedCode.AppendLine( "global using Microsoft.AspNetCore.Components;" );
				baseCompiler.GeneratedCode.AppendLine( "global using Microsoft.AspNetCore.Components.Rendering;" );

				// reference this from the main compiler
				compiler.AddReference( baseCompiler );
			}

			logOutput?.Invoke( $"Creating package.{project.Config.Org}.{project.Config.Ident} compiler" );
			logOutput?.Invoke( $"Compile path is {project.GetCodePath()}" );

			logOutput?.Invoke( $"Generating code.." );
			compiler.GeneratedCode.AppendLine( "global using static Sandbox.Internal.GlobalGameNamespace;" );

			// Required by razor
			compiler.GeneratedCode.AppendLine( "global using Microsoft.AspNetCore.Components;" );
			compiler.GeneratedCode.AppendLine( "global using Microsoft.AspNetCore.Components.Rendering;" );

			foreach ( var c in compileGroup.Compilers )
			{
				// important - this is what is used by the error system to determine which addon it came from
				// we really only want to set this metadata on assemblies that are being published, that way
				// we can skip reporting errors in addons that are being developed locally, and only get the
				// count of "in the wild" errors.

				c.GeneratedCode.AppendLine( $"[assembly: global::System.Reflection.AssemblyMetadata( \"AddonTitle\", {project.Config.Title.QuoteSafe()} )]" );
				c.GeneratedCode.AppendLine( $"[assembly: global::System.Reflection.AssemblyMetadata( \"AddonIdent\", {project.Config.Ident.QuoteSafe()} )]" );
				c.GeneratedCode.AppendLine( $"[assembly: global::System.Reflection.AssemblyMetadata( \"OrgIdent\", {project.Config.Org.QuoteSafe()} )]" );
				c.GeneratedCode.AppendLine( $"[assembly: global::System.Reflection.AssemblyMetadata( \"Ident\", {project.Config.FullIdent.QuoteSafe()} )]" );
				c.GeneratedCode.AppendLine( $"[assembly: global::System.Reflection.AssemblyMetadata( \"CompileTime\", {System.DateTime.UtcNow.ToString().QuoteSafe()} )]" );
				c.GeneratedCode.AppendLine( $"[assembly: global::System.Reflection.AssemblyMetadata( \"EngineVersion\", {Sandbox.Engine.Protocol.Api.ToString().QuoteSafe()} )]" );
				c.GeneratedCode.AppendLine( $"[assembly: global::System.Reflection.AssemblyMetadata( \"EngineMinorVersion\", {1.ToString().QuoteSafe()} )]" );
			}

			logOutput?.Invoke( $"Building.." );
			await compileGroup.BuildAsync();

			PackageManager.UnmountTagged( "publish" );

			logOutput?.Invoke( $"Done.." );

			if ( !compiler.BuildSuccess )
			{
				logOutput?.Invoke( $"COMPILE FAILED!!" );
				logOutput?.Invoke( $"" );
				logOutput?.Invoke( compileGroup.BuildResult.BuildDiagnosticsString() );
				logOutput?.Invoke( $"" );

				throw new System.InvalidOperationException( "Compiling failed. Can't continue." );
			}

			logOutput?.Invoke( $"Success!" );

			return compileGroup.BuildResult.Output.ToArray();

		}

		/// <summary>
		/// Resolve a compiler from an assembly, using the assembly name
		/// </summary>
		public static Compiler ResolveCompiler( Assembly assembly )
		{
			return Project.ResolveCompiler( assembly );
		}

		/// <summary>
		/// Finds the compilation order for any of the given libraries containing code,
		/// based on which libraries reference others.
		/// </summary>
		private static IReadOnlyList<(Project Project, IReadOnlyList<Project> References)> SortLibrariesForCompilation( IEnumerable<Project> libraries, Action<string> logOutput )
		{
			libraries = libraries.Where( x => x.HasCodePath() );

			var identMap = libraries
				.DistinctBy( x => x.Package.GetIdent( false, false ) )
				.ToDictionary( x => x.Package.GetIdent( false, false ), x => x,
					StringComparer.OrdinalIgnoreCase );

			IReadOnlyList<Project> FindReferences( Project project )
			{
				return project.Package.PackageReferences
					.Select( refName => identMap.TryGetValue( refName, out var reference ) ? reference : null )
					.Where( x => x is not null )
					.ToArray();
			}

			var remaining = identMap.Values
				.Select( project => (Project: project, References: FindReferences( project )) )
				.ToList();

			var sorted = new List<(Project Project, IReadOnlyList<Project> References)>();
			var added = new HashSet<Project>();

			while ( remaining.Any() )
			{
				var next = remaining.MinBy(
					x => x.References.Count( y => !added.Contains( y ) ) );

				var cyclicReferences = next.References
					.Where( y => !added.Contains( y ) )
					.ToArray();

				if ( cyclicReferences.Any() )
				{
					logOutput?.Invoke( $"Cyclic library dependency: {next.Project.Package.FullIdent}, " +
						$"{string.Join( ", ", cyclicReferences.Select( x => x.Package.FullIdent ) )}" );
				}

				sorted.Add( next );
				remaining.Remove( next );
				added.Add( next.Project );
			}

			return sorted;
		}
	}
}
