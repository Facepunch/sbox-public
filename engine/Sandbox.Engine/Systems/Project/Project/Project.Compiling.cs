using System.IO;
using System.Text.Json.Serialization;

namespace Sandbox;

public partial class Project
{

	/// <summary>
	/// Whether the project's code has a compiler assigned.
	/// </summary>
	[JsonIgnore]
	public bool HasCompiler => Compiler is not null || EditorCompiler is not null;

	[JsonIgnore]
	internal Compiler Compiler { get; private set; }

	[JsonIgnore]
	internal Compiler EditorCompiler { get; private set; }

	int lastCompilerHash;

	// anything that means we need to re-create the compiler and recompile should be here
	int CompilerHash => HashCode.Combine( Active, Current == this, Json.SerializeAsObject( Config.GetCompileSettings() ).ToJsonString(), Config.IsStandaloneOnly, Config.Org, Config.Ident, Config.Type, string.Join( ";", PackageReferences() ) );

	/// <summary>
	/// Whether to save/load compiled assemblies to disk.
	/// </summary>
	bool CacheAssemblies => IsBuiltIn && Application.IsRetail;

	/// <summary>
	/// These package types should reference package.base
	/// </summary>
	private static HashSet<string> BaseReferencingTypes { get; } = new HashSet<string> { "game", "addon", "library" };

	private void UpdateCompiler()
	{
		// Menu can be precompiled, load that and don't bother making a compiler if so
		if ( LoadPrecompiled() )
			return;

		// Only update the compiler if the config has changed
		var hash = CompilerHash;
		if ( hash == lastCompilerHash ) return;
		lastCompilerHash = hash;

		Compiler?.Dispose();
		Compiler = null;

		if ( !Active )
			return;

		if ( HasCodePath() )
		{
			var compilerSettings = Config.GetCompileSettings();

			if ( Config.Type == "tool" )
			{
				// Allow unsafe - but only in tools projects
				compilerSettings.Unsafe = true;
				compilerSettings.Whitelist = false;
			}

			//
			// Anything with code can be serverside
			//
			if ( Application.IsDedicatedServer )
			{
				var defines = compilerSettings.GetPreprocessorSymbols();
				if ( !defines.Contains( "SERVER" ) )
				{
					compilerSettings.DefineConstants += ";SERVER";
				}
			}

			if ( Config.Type == "game" )
			{
				compilerSettings.IgnoreFolders.Add( "editor" );
				compilerSettings.IgnoreFolders.Add( "unittest" );

				//
				// Override unsafe and whitelist stuff for non-standalone
				// games to make sure nobody does anything fishy
				//
				if ( Config.IsStandaloneOnly )
				{
					compilerSettings.Unsafe = true;
					compilerSettings.Whitelist = false;
				}
			}

			var compilerName = $"{Config.Org}.{Config.Ident}".Trim( '.' );
			var codePath = GetCodePath();

			if ( compilerName == "local.base" ) compilerName = "base";
			if ( compilerName == "local.toolbase" ) compilerName = "toolbase";

			Log.Trace( $"Create Compiler `{compilerName}`" );

			Compiler = CompileGroup.CreateCompiler( compilerName, codePath, compilerSettings );

			if ( BaseReferencingTypes.Contains( Config.Type ) && compilerName != "base" )
			{
				Compiler.AddBaseReference();
			}

			Compiler.GeneratedCode.AppendLine( $"global using Microsoft.AspNetCore.Components;" );
			Compiler.GeneratedCode.AppendLine( $"global using Microsoft.AspNetCore.Components.Rendering;" );
			Compiler.GeneratedCode.AppendLine( $"global using static Sandbox.Internal.GlobalGameNamespace;" );

			if ( Config.Type == "tool" )
			{
				Compiler.GeneratedCode.AppendLine( $"global using static Sandbox.Internal.GlobalToolsNamespace;" );
				Compiler.AddReference( "Sandbox.Tools" );
				Compiler.AddReference( "Sandbox.Compiling" );
				Compiler.AddReference( "System.Diagnostics.Process" );
				Compiler.AddReference( "System.Net.WebSockets" );
				Compiler.AddReference( "System.Net.WebSockets.Client" );
				Compiler.AddReference( "Microsoft.Win32.Registry" );
				Compiler.AddReference( "System.Memory" );
				Compiler.AddReference( "Sandbox.Bind" );
				Compiler.AddReference( "Facepunch.ActionGraphs" );
				Compiler.AddReference( "SkiaSharp" );
				Compiler.AddReference( "Microsoft.CodeAnalysis" );
				Compiler.AddReference( "Microsoft.CodeAnalysis.CSharp" );

				if ( compilerName != "toolbase" )
				{
					Compiler.AddToolBaseReference();
					//await Project.WaitFor( "toolbase" );
				}
			}

			Compiler.GeneratedCode.AppendLine( $"[assembly: global::System.Reflection.AssemblyMetadata( \"Ident\", {Config.FullIdent.QuoteSafe()} )]" );
			Compiler.GeneratedCode.AppendLine( $"[assembly: global::System.Reflection.AssemblyMetadata( \"EngineVersion\", {Engine.Protocol.Api.ToString().QuoteSafe()} )]" );
			Compiler.GeneratedCode.AppendLine( $"[assembly: global::System.Reflection.AssemblyMetadata( \"EngineMinorVersion\", {1.ToString().QuoteSafe()} )]" );

			foreach ( var reference in PackageReferences() )
			{
				Compiler.AddReference( reference );
			}

			foreach ( var reference in compilerSettings.DistinctAssemblyReferences )
			{
				Compiler.AddReference( reference );
			}

			Compiler.WatchForChanges();
		}

		if ( Application.IsEditor )
		{
			// update editor compiler if we're running in the editor

			if ( Config.Type == "game" || Config.Type == "library" || Config.Type == "addon" )
			{
				UpdateEditorCompiler();
			}
		}

		if ( CacheAssemblies )
		{
			// see if we've got cached assemblies from a previous compile
			foreach ( var compiler in new[] { Compiler, EditorCompiler } )
			{
				if ( compiler is null ) continue;

				LoadCachedAssembly( compiler );

				if ( compiler.BuildSuccess )
					compiler.Group.ClearForRecompile( compiler );
			}
		}
	}

	private bool LoadCachedAssembly( Compiler compiler )
	{
		try
		{
			string directory = Path.Combine( GetRootPath(), ".sbox", "bin" );
			string binPath = Path.Combine( directory, $"{compiler.AssemblyName}.dll" );
			if ( !File.Exists( binPath ) )
				return false;

			byte[] bytes = File.ReadAllBytes( binPath );
			if ( bytes is null )
				return false;

			var attrs = AssemblyMetadata.GetCustomAttributes( bytes );
			var metadata = attrs
				.Where( a => a.AttributeFullName == "System.Reflection.AssemblyMetadataAttribute" )
				.Where( a => a.Arguments.Length == 2 )
				.ToDictionary(
					a => (string)a.Arguments[0],
					a => (string)a.Arguments[1]
				);

			//
			// Check Fingerprint
			// Can only be reused if it's from the same project and engine version, otherwise we'll need a full recompile.
			//
			if ( metadata.TryGetValue( "Ident", out var ident ) == false || ident != Config.FullIdent )
				return false;

			if ( metadata.TryGetValue( "EngineVersion", out var ver ) == false || ver != Sandbox.Engine.Protocol.Api.ToString() )
				return false;

			if ( metadata.TryGetValue( "CompileTime", out var timeStr ) == false ||
				!DateTime.TryParse( timeStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var compileTime ) )
				return false;

			//
			// Finally, check if it's out of date.
			// If any source files have been modified since the assembly was compiled, we can't use it.
			//
			foreach ( var fs in compiler.SourceLocations )
			{
				var files = fs.FindFile( "/", "*.*", true );
				foreach ( var file in files )
				{
					string ext = Path.GetExtension( file );
					if ( ext != ".cs" && ext != ".razor" )
						continue; // only care about source files

					string path = fs.GetFullPath( file );
					if ( File.GetLastWriteTimeUtc( path ) > compileTime )
					{
						Log.Info( $"{compiler.Name}: Source file {file} was modified after the cached assembly was compiled. Forcing recompile." );
						return false;
					}

					if ( File.GetCreationTimeUtc( path ) > compileTime )
					{
						Log.Info( $"{compiler.Name}: Source file {file} was added after the cached assembly was compiled. Forcing recompile." );
						return false;
					}
				}
			}

			// All good, swap in the assembly
			compiler.UpdateFromAssembly( bytes );

			AssemblyFileSystem.CreateDirectory( "/.bin" );
			AssemblyFileSystem.WriteAllBytes( $"/.bin/{compiler.AssemblyName}.dll", bytes );

			return true;
		}
		catch
		{
			return false;
		}
	}

	IEnumerable<Package> PackageReferences()
	{
		if ( (Config.Type == "game" || Config.Type == "addon") && !IsBuiltIn )
		{
			foreach ( var library in Project.Libraries.Where( x => x.HasCodePath() ) )
			{
				yield return library.Package;
			}
		}
	}

	/// <summary>
	/// If required, create the editor compiler
	/// </summary>
	void UpdateEditorCompiler()
	{
		EditorCompiler?.Dispose();
		EditorCompiler = null;

		if ( !Active )
			return;

		// Horrific, but Current isn't avaliable yet, so this is the only way to tell if we're in the menu project
		var path = Sandbox.Utility.CommandLine.GetSwitch( "-project", "" ).TrimQuoted();
		var isMenu = path.EndsWith( "menu\\.sbproj" );

		// only want menu editor code if we're in the menu project
		if ( Config.Ident == "menu" && !isMenu )
			return;

		if ( !HasEditorPath() )
			return;

		var compilerSettings = Config.GetCompileSettings();
		var compilerName = $"{Config.Org}.{Config.Ident}".Trim( '.' ) + ".editor";

		compilerSettings.Unsafe = true;
		compilerSettings.Whitelist = false;

		Log.Trace( $"Create Editor Compiler `{compilerName}`" );

		EditorCompiler = CompileGroup.CreateCompiler( compilerName, GetEditorPath(), compilerSettings );

		if ( Compiler is not null )
		{
			// reference the main code assembly		
			EditorCompiler.AddReference( Compiler );
		}

		EditorCompiler.AddBaseReference();
		EditorCompiler.AddToolBaseReference();

		EditorCompiler.AddReference( "Sandbox.Tools" );
		EditorCompiler.AddReference( "Sandbox.Compiling" );
		EditorCompiler.AddReference( "System.Diagnostics.Process" );
		EditorCompiler.AddReference( "System.Net.WebSockets" );
		EditorCompiler.AddReference( "System.Net.WebSockets.Client" );
		EditorCompiler.AddReference( "Microsoft.Win32.Registry" );
		EditorCompiler.AddReference( "System.Memory" );
		EditorCompiler.AddReference( "Sandbox.Bind" );
		EditorCompiler.AddReference( "Facepunch.ActionGraphs" );
		EditorCompiler.AddReference( "SkiaSharp" );
		EditorCompiler.AddReference( "Microsoft.CodeAnalysis" );
		EditorCompiler.AddReference( "Microsoft.CodeAnalysis.CSharp" );

		EditorCompiler.GeneratedCode.AppendLine( $"global using static Sandbox.Internal.GlobalToolsNamespace;" );
		EditorCompiler.GeneratedCode.AppendLine( $"global using static Sandbox.Internal.GlobalGameNamespace;" );

		foreach ( var reference in PackageReferences() )
		{
			EditorCompiler.AddReference( reference );
		}

		if ( (Config.Type == "game" || Config.Type == "addon") && !IsBuiltIn )
		{
			// editor libraries
			foreach ( var library in Libraries.Where( x => x.HasEditorPath() ) )
			{
				EditorCompiler.AddEditorReference( library.Package );
			}
		}

		EditorCompiler.AddReference( "package.local.actiongraph" );
		EditorCompiler.AddReference( "package.local.shadergraph" );
		EditorCompiler.AddReference( "package.local.moviemaker" );
		EditorCompiler.AddReference( "package.local.hammer" );
		EditorCompiler.AddReference( "package.local.dooeditor" );

		foreach ( var reference in compilerSettings.DistinctAssemblyReferences )
		{
			EditorCompiler.AddReference( reference );
		}

		EditorCompiler.WatchForChanges();
	}

	internal static async Task<bool> CompileAsync()
	{
		while ( CompileGroup.IsBuilding )
		{
			await Task.Delay( 50 );
		}

		CompileGroup.AllowFastHotload = HotloadManager.hotload_fast;

		return await CompileGroup.BuildAsync();
	}

	internal static IEnumerable<Microsoft.CodeAnalysis.Diagnostic> GetCompileDiagnostics()
	{
		if ( CompileGroup?.BuildResult.Diagnostics is null )
			return Array.Empty<Microsoft.CodeAnalysis.Diagnostic>();

		return CompileGroup.BuildResult.Diagnostics;
	}

	private static void OnCompileSuccess()
	{
		var grouped = CompileGroup.BuildResult.Output.GroupBy( x => Project.FindByCompiler( x.Compiler ) );

		foreach ( var grouping in grouped )
		{
			var project = grouping.Key;
			if ( project is null ) continue;

			foreach ( var assembly in grouping )
			{
				if ( !assembly.Successful )
				{
					continue;
				}

				project.AssemblyFileSystem.CreateDirectory( "/.bin" );
				project.AssemblyFileSystem.WriteAllBytes( $"/.bin/{assembly.Compiler.AssemblyName}.dll", assembly.AssemblyData );
				project.AssemblyFileSystem.WriteAllBytes( $"/.bin/{assembly.Compiler.AssemblyName}.cll", assembly.Archive.Serialize() );

				if ( project.CacheAssemblies )
				{
					try
					{
						string binPath = Path.Combine( project.GetRootPath(), ".sbox", "bin" );
						Directory.CreateDirectory( binPath );

						File.WriteAllBytes( Path.Combine( binPath, $"{assembly.Compiler.AssemblyName}.dll" ), assembly.AssemblyData );
					}
					catch ( Exception ex )
					{
						Log.Warning( $"Failed to write cached assembly for project {project.Config.FullIdent}: {ex.Message}" );
					}
				}
			}
		}
	}

	/// <summary>
	/// Find a project from a compiler
	/// </summary>
	private static Project FindByCompiler( Compiler compiler )
	{
		return Project.All.FirstOrDefault( x => x.Compiler == compiler || x.EditorCompiler == compiler );
	}

	/// <summary>
	/// Loads precompiled assemblies that might exist (menu) as long as we're not in editor or headless or anything
	/// </summary>
	private bool LoadPrecompiled()
	{
		// Don't use precompiled in editor or any sort of headless
		if ( Application.IsEditor || Application.IsUnitTest || Application.IsHeadless )
			return false;

		var assemblyPath = Path.Combine( GetRootPath(), ".bin" );
		if ( !Directory.Exists( assemblyPath ) )
		{
			return false;
		}

		var assemblies = Directory.EnumerateFiles( assemblyPath, "*.dll" );
		if ( assemblies.Count() == 0 )
		{
			return false;
		}

		AssemblyFileSystem.CreateDirectory( "/.bin" );
		foreach ( var assembly in assemblies )
		{
			AssemblyFileSystem.WriteAllBytes( $"/.bin/{Path.GetFileName( assembly )}", File.ReadAllBytes( assembly ) );
		}

		return true;
	}
}
