using Sandbox.Engine;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox;

public partial class Project
{
	/// <summary>
	/// The compile group every local project's code builds in. Internal so game code can't see
	/// it - editor code reaches it through the Sandbox.Tools Project extensions.
	/// </summary>
	internal static CompileGroup CompileGroup { get; private set; }


	/// <summary>
	/// Current open project.
	/// </summary>
	public static Project Current { get; internal set; }

	internal static List<Project> All;
	internal static IEnumerable<Project> Libraries => All.Where( x => x.Active && x.Config.Type == "library" );

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
		CompileGroup.PrintErrorsInConsole = !Application.IsEditor;
		CompileGroup.OnCompileStarted = OnCompileStarted;
		CompileGroup.OnCompileFinished = OnCompileFinished;
		CompileGroup.OnCompileSuccess = OnCompileSuccess;
	}

	internal static void RebuildCompilers()
	{
		CompileGroup?.Dispose();

		CompileGroup = new( "local" );
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

	/// <summary>
	/// Game and addon compilers reference every library, and those references are only added when the
	/// compiler is created. Call this after a library is added while projects are open so their compilers
	/// are remade with it. Projects whose library references haven't changed are left alone.
	/// </summary>
	internal static void UpdateLibraryReferences()
	{
		foreach ( var project in All )
		{
			if ( project.Config.Type is "game" or "addon" )
				project.UpdateCompiler();
		}
	}

	static void OnCompileStarted()
	{
		TakeCacheFingerprints();
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
	/// Discovers the base projects. Content builds activate every addon to load its resource types.
	/// </summary>
	internal static async Task InitializeBuiltIn( bool syncPackageManager = true, bool activateAddons = false )
	{
		if ( !Application.IsStandalone )
			AddAddonPath( EngineFileSystem.Root.GetFullPath( "addons" ), activateAddons );

		if ( Application.IsEditor || Application.IsUnitTest )
			AddAddonPath( EngineFileSystem.Root.GetFullPath( "editor" ) );

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
				if ( project.Config.Type == "menu" && Current != project ) continue;

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

		// Recommend C# Dev Kit and Slang extensions
		var extensions = new VSCodeExtensions { Recommendations = ["ms-dotnettools.csdevkit", "shader-slang.slang-language-extension"] };
		WriteVsCodeFile( vscodePath, "extensions.json", JsonSerializer.Serialize( extensions, JsonWriteIndented ) );

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

		WriteVsCodeFile( vscodePath, "settings.json", JsonSerializer.Serialize( settings, JsonWriteIndented ) );
	}

	/// <summary>
	/// Refresh an optional workspace file when its contents change. A locked file can wait until the next refresh.
	/// </summary>
	static void WriteVsCodeFile( string directory, string filename, string contents )
	{
		var path = Path.Combine( directory, filename );

		try
		{
			Directory.CreateDirectory( directory );

			if ( File.Exists( path ) && File.ReadAllText( path ) == contents )
				return;

			File.WriteAllText( path, contents );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			Log.Warning( $"Couldn't update {path}: {e.Message}" );
		}
	}

	/// <summary>
	/// Load projects from the immediate subfolders of an addon path into the shared project list.
	/// Projects activate according to their config unless <paramref name="activate"/> requests all of them.
	/// Existing projects are reused.
	/// </summary>
	internal static void AddAddonPath( string addonsPath, bool activate = false )
	{
		if ( !Directory.Exists( addonsPath ) ) return;

		foreach ( var folder in Directory.EnumerateDirectories( addonsPath ).Order( StringComparer.OrdinalIgnoreCase ) )
		{
			try
			{
				var configs = Directory.EnumerateFiles( folder, "*.sbproj" ).ToArray();
				if ( configs.Length != 1 ) continue;

				if ( AddFromFile( configs[0], active: activate ) is null && activate )
					throw new InvalidOperationException( $"Couldn't activate addon project '{configs[0]}'." );
			}
			catch ( Exception e ) when ( !activate )
			{
				Log.Warning( $"Couldn't load project in {folder}: {e.Message}" );
			}
		}
	}

	/// <summary>
	/// Add a game project from its .sbproj - its assets mounted, the libraries in its Libraries folder added
	/// alongside it - and compile it, so it can be run like a package. Null if it didn't load.
	/// </summary>
	internal static async Task<Project> AddGameFromFile( string path )
	{
		var project = AddFromFile( path );
		if ( project is null ) return null;

		if ( project.pendingCompilation is not { IsCompleted: false } )
			project.StartCompilation();

		var build = project.pendingCompilation;
		if ( await build ) return project.Active ? project : null;

		if ( project.Active && project.pendingCompilation == build )
		{
			project.Active = false;
			project.UpdateCompiler();
		}

		return null;
	}

	private Task<bool> pendingCompilation;

	/// <summary>
	/// A discovered project with code must be prepared before mounting it. Activation happens
	/// before compilation, so a concurrent install must also wait for the build that activated it.
	/// </summary>
	internal bool NeedsInstallCompilation
	{
		get
		{
			if ( pendingCompilation is { } build && (!build.IsCompletedSuccessfully || !build.Result) )
				return true;

			return !Active && (Config.Type == "game" || HasCodePath() || HasEditorPath());
		}
	}

	private void StartCompilation()
	{
		NativeEngine.FullFileSystem.AddProjectPath( ConfigFilePath, GetAssetsPath().ToLowerInvariant() );

		// What the editor's put on disk for it - the cloud assets its own use, and generated files - found from there as
		// they are in the editor, rather than nowhere
		var sbox = Path.Combine( GetRootPath(), ".sbox" );
		NativeEngine.FullFileSystem.AddCloudPath( "mod_cloud", Path.Combine( sbox, "cloud" ) );
		NativeEngine.FullFileSystem.AddCloudPath( "mod_transient", Path.Combine( sbox, "transient" ) );

		var libraries = Path.Combine( RootDirectory.FullName, "Libraries" );
		var dependencies = new List<Project>();

		// We should iterate all available libraries and add their projects
		foreach ( var folder in Directory.Exists( libraries ) ? Directory.EnumerateDirectories( libraries ) : [] )
		{
			var configs = Directory.EnumerateFiles( folder, "*.sbproj" ).ToArray();
			if ( configs.Length != 1 ) continue;
			if ( AddFromFile( configs[0] ) is { } library )
				dependencies.Add( library );
		}

		// Refresh compiler references now that the game's libraries are registered.
		UpdateCompiler();

		pendingCompilation = CompileAsync();
		foreach ( var library in dependencies )
			library.pendingCompilation = pendingCompilation;
	}

	internal static Project AddFromFile( string path, bool active = true )
	{
		// Need an project file
		var cleanPath = NormalizeConfigFilePath( path );

		// Don't add the same project twice
		if ( All.FirstOrDefault( a => string.Equals( a.ConfigFilePath, cleanPath, StringComparison.OrdinalIgnoreCase ) ) is Project lp )
		{
			if ( active && !lp.Active )
			{
				lp.Active = true;
				if ( !lp.Load() ) return null;
			}

			return lp;
		}

		var project = new Project( cleanPath ) { Active = active };
		if ( !project.Load( registering: true ) )
		{
			project.Dispose();
			if ( project.Broken )
				throw new System.Exception( $"Couldn't add project." );

			return null;
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

	/// <summary>
	/// Add a project from config JSON rather than a .sbproj on disk, rooted at <paramref name="rootPath"/>.
	/// This is how an exported standalone game loads itself: the config was embedded in its
	/// executable at export time, so it's read-only and never upgraded or saved here.
	/// </summary>
	internal static Project AddFromEmbeddedConfig( string configJson, string rootPath )
	{
		var root = new DirectoryInfo( Path.GetFullPath( rootPath ) );

		if ( All.FirstOrDefault( a => a.RootDirectory?.FullName == root.FullName ) is Project existing )
			return existing;

		var project = new Project( root, configJson ) { Active = true };
		if ( !project.Load( registering: true ) )
		{
			project.Dispose();
			throw new System.Exception( $"Couldn't add embedded project rooted at {root.FullName}" );
		}

		All.Add( project );

		return project;
	}

	internal static Project FindByIdent( string ident )
	{
		if ( !Package.TryParseIdentParts( ident, out var parsed ) ) return null;

		return FindByIdent( parsed.org, parsed.package );
	}

	internal static Project FindByIdent( string org, string ident )
	{
		return All.FirstOrDefault( x => string.Equals( x.savedIdent.Org, org, StringComparison.OrdinalIgnoreCase )
			&& string.Equals( x.savedIdent.Ident, ident, StringComparison.OrdinalIgnoreCase ) );
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

	/// <summary>
	/// Load a project from disk without registering it. Returns null if it fails or its type cannot load here.
	/// </summary>
	public static Project Load( string dir )
	{
		var project = new Project( NormalizeConfigFilePath( dir ) ) { Active = false };
		if ( !project.Load() )
		{
			project.Dispose();
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

