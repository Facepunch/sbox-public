using Sandbox.Engine;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

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
	internal static async Task InitializeBuiltIn( bool syncPackageManager = true, bool saveUpgradedConfigs = true )
	{
		if ( !Application.IsStandalone && !Application.IsHeadless )
		{
			AddFromFileBuiltIn( "addons/menu/.sbproj", saveUpgradedConfigs );
		}

		if ( Application.IsEditor || Application.IsUnitTest )
		{
			AddFromFileBuiltIn( "addons/tools/.sbproj", saveUpgradedConfigs );
			AddFromFileBuiltIn( "editor/ShaderGraph/.sbproj", saveUpgradedConfigs );
			AddFromFileBuiltIn( "editor/ActionGraph/.sbproj", saveUpgradedConfigs );
			AddFromFileBuiltIn( "editor/MovieMaker/.sbproj", saveUpgradedConfigs );
			AddFromFileBuiltIn( "editor/Hammer/.sbproj", saveUpgradedConfigs );
			AddFromFileBuiltIn( "editor/DooEditor/DooEditor.sbproj", saveUpgradedConfigs );
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
	internal static Task SyncWithPackageManager( CancellationToken cancellationToken = default, bool throwOnFailure = false )
	{
		return PackageManager.InstallProjects( All.Where( x => x.Active ).ToArray(), cancellationToken, throwOnFailure );
	}

	/// <summary>
	/// Add projects found directly beneath a project's Libraries folder. This only registers the
	/// projects; editor-specific content and native filesystem mounts remain the editor's concern.
	/// </summary>
	internal static IReadOnlyList<Project> AddLocalLibraries( Project project, bool throwOnInvalid = false, bool saveUpgradedConfigs = true )
	{
		ArgumentNullException.ThrowIfNull( project );

		var librariesPath = Path.Combine( project.GetRootPath(), "Libraries" );
		if ( !Directory.Exists( librariesPath ) )
			return Array.Empty<Project>();

		var libraries = new List<Project>();
		var folders = Directory.EnumerateDirectories( librariesPath )
			.OrderBy( x => Path.GetFileName( x ), StringComparer.OrdinalIgnoreCase )
			.ThenBy( x => Path.GetFileName( x ), StringComparer.Ordinal );

		foreach ( var folder in folders )
		{
			var configs = Directory.EnumerateFiles( folder, "*.sbproj", SearchOption.TopDirectoryOnly )
				.OrderBy( x => Path.GetFileName( x ), StringComparer.OrdinalIgnoreCase )
				.ThenBy( x => Path.GetFileName( x ), StringComparer.Ordinal )
				.ToArray();

			if ( configs.Length != 1 )
			{
				var message = $"Library folder '{folder}' must contain exactly one .sbproj file; found {configs.Length}.";
				if ( throwOnInvalid )
					throw new InvalidDataException( message );

				Log.Warning( message );
				continue;
			}

			var configPath = NormalizeConfigFilePath( configs[0] );
			var wasAlreadyRegistered = All.Any( x => x.ConfigFilePath == configPath );
			Project library;
			try
			{
				library = AddFromFile( configs[0], saveUpgradedConfig: saveUpgradedConfigs );
			}
			catch ( Exception e ) when ( !throwOnInvalid )
			{
				Log.Warning( e, $"Couldn't load library project '{configs[0]}': {e.Message}" );
				continue;
			}
			catch ( Exception e )
			{
				throw new InvalidDataException( $"Couldn't load library project '{configs[0]}'.", e );
			}

			if ( !string.Equals( library.Config.Type, "library", StringComparison.Ordinal ) )
			{
				var message = $"Project '{configs[0]}' has type '{library.Config.Type}', but projects beneath Libraries must have type 'library'.";
				if ( !wasAlreadyRegistered )
					Remove( library );

				if ( throwOnInvalid )
					throw new InvalidDataException( message );

				Log.Warning( message );
				continue;
			}

			libraries.Add( library );
		}

		return libraries;
	}

	/// <summary>
	/// Install the packages needed to compile a loaded active project, then reload it so its
	/// compilers can resolve those dependencies. This deliberately excludes editor UI, assets,
	/// solution generation and native filesystem mounts.
	/// </summary>
	internal static async Task PrepareForCompileAsync(
		Project project,
		Action<string> reportProgress = null,
		CancellationToken cancellationToken = default,
		bool throwOnPackageFailure = false )
	{
		ArgumentNullException.ThrowIfNull( project );

		if ( !All.Contains( project ) )
			throw new InvalidOperationException( "The project must be registered before it can be prepared for compilation." );

		if ( Current != project )
			throw new InvalidOperationException( "The project must be current before it can be prepared for compilation." );

		if ( !project.Active )
			throw new InvalidOperationException( "The project must be active before it can be prepared for compilation." );

		cancellationToken.ThrowIfCancellationRequested();
		reportProgress?.Invoke( "Loading built-in projects" );
		using ( var _ = Bootstrap.StartupTiming?.ScopeTimer( "Load Project: Builtin Projects" ) )
		{
			await PackageManager.InstallProjects(
				All.Where( x => x.IsBuiltIn ).ToArray(),
				cancellationToken,
				throwOnPackageFailure );
		}

		var parentPackage = project.Config.GetMetaOrDefault<string>( "ParentPackage", null );
		if ( project.Config.Type == "addon" && !string.IsNullOrWhiteSpace( parentPackage ) )
		{
			cancellationToken.ThrowIfCancellationRequested();
			reportProgress?.Invoke( $"Loading parent package ({parentPackage})" );
			using ( var _ = Bootstrap.StartupTiming?.ScopeTimer( "Load Project: ParentPackage" ) )
			{
				await PackageManager.InstallAsync( new PackageLoadOptions( parentPackage, "tools", cancellationToken )
				{
					ThrowOnCompileFailure = throwOnPackageFailure
				} );
			}
		}

		cancellationToken.ThrowIfCancellationRequested();
		reportProgress?.Invoke( "Syncing package manager" );
		using ( var _ = Bootstrap.StartupTiming?.ScopeTimer( "Load Project: Sync PackageManager" ) )
		{
			await SyncWithPackageManager( cancellationToken, throwOnPackageFailure );
		}

		cancellationToken.ThrowIfCancellationRequested();
		project.Load( upgradeConfig: true );

		if ( throwOnPackageFailure && project.Broken )
			throw new InvalidDataException( $"Project '{project.ConfigFilePath}' could not be reloaded after installing its dependencies." );
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

	[Flags]
	internal enum ProjectLoadFlags
	{
		None = 0,

		/// <summary>
		/// Component of the engine, always automatically loaded and can't be unloaded
		/// </summary>
		BuiltIn = 1 << 0,
	}

	internal static Project AddFromFileBuiltIn( string path, bool saveUpgradedConfig = true ) => AddFromFile( path, flags: ProjectLoadFlags.BuiltIn, saveUpgradedConfig: saveUpgradedConfig );

	internal static Project AddFromFile( string path, bool active = true, ProjectLoadFlags flags = ProjectLoadFlags.None, bool saveUpgradedConfig = true )
	{
		// Need an project file
		var cleanPath = NormalizeConfigFilePath( path );

		// Don't add the same project twice
		if ( All.Where( a => a.ConfigFilePath == cleanPath ).FirstOrDefault() is Project lp )
			return lp;

		var project = new Project( cleanPath )
		{
			Active = active,
			IsBuiltIn = flags.HasFlag( ProjectLoadFlags.BuiltIn )
		};
		project.Load();

		// If it loaded broken, don't bother with it
		if ( project.Broken )
		{
			throw new System.Exception( $"Couldn't add project." );
		}

		// Upgrade the schema in memory before the engine uses it. Interactive callers
		// persist that upgrade; automation can opt out of changing the source project.
		if ( project.Config.Upgrade() )
		{
			if ( saveUpgradedConfig )
			{
				project.Save();
			}
			else
			{
				project.UpdateMockPackage();
				project.UpdateCompiler();
			}
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
		project.Load();

		if ( project.Broken )
		{
			throw new System.Exception( $"Couldn't add embedded project rooted at {root.FullName}" );
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
		var project = new Project( NormalizeConfigFilePath( dir ) ) { Active = false };
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

