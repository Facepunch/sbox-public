using Sandbox.Engine;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox;

public partial class Project
{
	static CompileGroup CompileGroup;


	/// <summary>
	/// Current open project.
	/// </summary>
	public static Project Current { get; internal set; }

	internal static List<Project> All;
	internal static IEnumerable<Project> Libraries => All.Where( x => x.Config.Type == "library" );

	static Project()
	{
		Clear();
	}


	/// <summary>
	/// Remove all local packages. Used by unit tests to reset state.
	/// </summary>
	internal static void Clear()
	{
		All = new();
		PackageManager.UnmountTagged( "local" );
		CompileGroup?.Dispose();

		CompileGroup = new( "local" );
		CompileGroup.AccessControl = PackageManager.AccessControl;
		CompileGroup.PrintErrorsInConsole = !Application.IsEditor;
		CompileGroup.OnCompileStarted = OnCompileStarted;
		CompileGroup.OnCompileFinished = OnCompileFinished;
		CompileGroup.OnCompileSuccess = OnCompileSuccess;
	}

	internal static void RebuildCompilers()
	{
		CompileGroup?.Dispose();

		CompileGroup = new( "local" );
		CompileGroup.AccessControl = PackageManager.AccessControl;
		CompileGroup.PrintErrorsInConsole = !Application.IsEditor;
		CompileGroup.OnCompileStarted = OnCompileStarted;
		CompileGroup.OnCompileFinished = OnCompileFinished;
		CompileGroup.OnCompileSuccess = OnCompileSuccess;

		foreach ( var proj in All )
		{
			proj.Compiler?.Dispose();
			proj.Compiler = null;

			proj.EditorCompiler?.Dispose();
			proj.EditorCompiler = null;

			proj.lastCompilerHash = default;

			proj.UpdateCompiler();
		}
	}

	static void OnCompileStarted()
	{
		IToolsDll.Current?.RunEvent( "compile.started", CompileGroup );
	}

	static void OnCompileFinished()
	{
		IToolsDll.Current?.RunEvent( "compile.complete", CompileGroup );
	}

	internal static void Remove( Project project )
	{
		project.Dispose();
		All.Remove( project );
	}

	/// <summary>
	/// Check whether the group needs recompiling, and recompiles
	/// </summary>
	internal static void Tick()
	{
		if ( !CompileGroup.NeedsBuild )
			return;

		if ( CompileGroup.IsBuilding )
			return;

		CompileGroup.AllowFastHotload = HotloadManager.hotload_fast;

		_ = CompileGroup.BuildAsync();
	}

	/// <summary>
	/// Initializes all the base projects
	/// </summary>
	internal static async Task InitializeBuiltIn( bool syncPackageManager = true )
	{
		AddFromFileBuiltIn( "addons/base/.sbproj" );

		if ( !Application.IsStandalone && !Application.IsHeadless )
		{
			AddFromFileBuiltIn( "addons/menu/.sbproj" );
		}

		if ( Application.IsEditor || Application.IsUnitTest )
		{
			AddFromFileBuiltIn( "addons/tools/.sbproj" );
			AddFromFileBuiltIn( "editor/ShaderGraph/.sbproj" );
			AddFromFileBuiltIn( "editor/ActionGraph/.sbproj" );
			AddFromFileBuiltIn( "editor/MovieMaker/.sbproj" );
			AddFromFileBuiltIn( "editor/Hammer/.sbproj" );
		}

		if ( syncPackageManager )
		{
			// Is PackageManager tools-only?
			await SyncWithPackageManager();
		}
	}

	/// <summary>
	/// Takes all of the active projects and makes sure we're in sync
	/// with the package manager. Creates mock packages that act like real ones.
	/// Removes packages that are no longer active. If nothing changed then this should
	/// do nothing.
	/// </summary>
	internal static Task SyncWithPackageManager()
	{
		return PackageManager.InstallProjects( All.Where( x => x.Active ).ToArray() );
	}

	/// <summary>
	/// (Re)generate the active project's solution file.
	/// </summary>
	internal static async Task GenerateSolution()
	{
		var solutionName = $"s&box.slnx";
		var solutionFolder = EngineFileSystem.Root.GetFullPath( "/" );

		if ( Current is not null )
		{
			solutionName = $"{Current.Config.Ident}.slnx";
			solutionFolder = Current.GetRootPath();
		}

		try
		{
			var generator = new Sandbox.SolutionGenerator.Generator();

			foreach ( var project in All.ToArray() )
			{
				if ( !project.Active ) continue;

				// Don't put menu project in everyone's slns
				if ( project.Config.Ident == "menu" && Current?.Config?.Ident != "menu" ) continue;

				await project.GenerateProject( generator );
			}

			generator.Run( "sbox.exe", "bin/managed", solutionName, EngineFileSystem.Root.GetFullPath( "/" ), solutionFolder );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Exception when generating {solutionName} ({e.Message})" );
		}

		if ( Current is not null )
		{
			WriteVsCodeWorkspace( Current );
		}
	}

	private static readonly JsonSerializerOptions JsonWriteIndented = new() { WriteIndented = true };

	public class VSCodeExtensions
	{
		[JsonPropertyName( "recommendations" )]
		public string[] Recommendations { get; set; } = [];
	}

	class VSCodeSettings
	{
		[JsonPropertyName( "files.associations" )]
		public Dictionary<string, string> FilesAssociations { get; set; } = [];

		[JsonPropertyName( "slang.additionalSearchPaths" )]
		public string[] SlangIncludePaths { get; set; } = [];

		[JsonPropertyName( "slang.predefinedMacros" )]
		public string[] SlangDefines { get; set; } = [];

		[JsonPropertyName( "slang.workspaceFlavor" )]
		public string SlangWorkspaceFlavor { get; set; } = "vfx";
	}

	/// <summary>
	/// Writes a .vscode workspace configuration 
	/// </summary>
	/// <param name="project"></param>
	static void WriteVsCodeWorkspace( Project project )
	{
		var projectPath = project.GetRootPath();

		var vscodePath = Path.Combine( projectPath, ".vscode" );

		Directory.CreateDirectory( vscodePath );

		// Recommend C# Dev Kit and Slang extensions
		var extensions = new VSCodeExtensions { Recommendations = ["ms-dotnettools.csdevkit", "shader-slang.slang-language-extension"] };
		File.WriteAllText( Path.Combine( vscodePath, "extensions.json" ), JsonSerializer.Serialize( extensions, JsonWriteIndented ) );

		// Associate file extensions (defaults to Unity ShaderLab) and set up Slang search paths
		var settings = new VSCodeSettings
		{
			FilesAssociations = new() { { "*.shader", "slang" }, { "*.hlsl", "slang" } }
		};

		var shaderSearchPaths = new List<string> { EngineFileSystem.Root.GetFullPath( "/core/shaders" ) };

		foreach ( var p in Project.All )
		{
			shaderSearchPaths.Add( Path.Combine( p.GetAssetsPath(), "shaders" ) );
		}

		settings.SlangIncludePaths = [.. shaderSearchPaths];
		settings.SlangWorkspaceFlavor = "vfx";

		File.WriteAllText( Path.Combine( vscodePath, "settings.json" ), JsonSerializer.Serialize( settings, JsonWriteIndented ) );
	}

	/// <summary>
	/// Like AddFromFile but the project is marked as "built in" - which means
	/// it's always automatically loaded and can't be unloaded.
	/// </summary>
	internal static Project AddFromFileBuiltIn( string path )
	{
		var p = AddFromFile( path );
		if ( p == null ) return null;

		p.IsBuiltIn = true;
		return p;
	}

	internal static Project AddFromFile( string path, bool active = true )
	{
		// Need an project file
		if ( !path.EndsWith( ".sbproj" ) )
			path = System.IO.Path.Combine( path, ".sbproj" );

		var cleanPath = System.IO.Path.GetFullPath( path );

		// Don't add the same project twice
		if ( All.Where( a => a.ConfigFilePath == cleanPath ).FirstOrDefault() is Project lp )
			return lp;

		var project = new Project { ConfigFilePath = cleanPath, Active = active };
		project.Load();

		// If it loaded broken, don't bother with it
		if ( project.Broken )
		{
			throw new System.Exception( $"Couldn't add project." );
		}

		// If the schema needs upgrading then upgrade it and save before
		// the engine loads it, so it's up to date at that point.
		if ( project.Config.Upgrade() )
		{
			project.Save();
		}

		All.Add( project );

		return project;
	}

	internal static Project FindByIdent( string ident )
	{
		return All.FirstOrDefault( x => string.Equals( ident.Replace( "#local", "" ), x.Config.FullIdent, StringComparison.OrdinalIgnoreCase ) );
	}

	[ConCmd( "list_projects", ConVarFlags.Protected )]
	internal static void ListProjects()
	{
		Log.Info( $"Loaded projects:" );
		foreach ( var project in All.OrderBy( x => x.Config.Type ).ThenByDescending( x => x.Active ).ThenBy( x => x.Config.Title ) )
		{
			var sb = new StringBuilder();

			sb.Append( "\t- " );
			sb.Append( $"{project.Config.Type} " );
			sb.Append( $"{project.Config.FullIdent} " );
			sb.Append( $"({project.GetRootPath()}) " );

			if ( project.Active )
				sb.Append( "[ACTIVE] " );

			Log.Info( sb.ToString() );
		}
	}

	public static Project Load( string dir )
	{
		var cleanPath = System.IO.Path.GetFullPath( dir );

		var project = new Project { ConfigFilePath = cleanPath, Active = false };
		project.Load();

		// If it loaded broken, don't bother with it
		if ( project.Broken )
		{
			return null;
		}

		// If the schema needs upgrading then upgrade it and save before
		// the engine loads it, so it's up to date at that point.
		if ( project.Config.Upgrade() )
		{
			project.Save();
		}

		return project;
	}

	/// <summary>
	/// Resolve an assemblt to a compiler using the assembly name
	/// </summary>
	internal static Compiler ResolveCompiler( Assembly assembly )
	{
		return CompileGroup.FindCompilerByAssemblyName( assembly.GetName().Name );
	}
}

