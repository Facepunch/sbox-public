using Sandbox.Engine;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Represents an on-disk project.
/// </summary>
[Expose]
public sealed partial class Project
{
	/// <summary>
	/// If this is a single asset project, this will be the asset object
	/// </summary>
	internal object ProjectSourceObject { get; set; }

	/// <summary>
	/// Absolute path to the .addon file
	/// </summary>
	[JsonPropertyName( "Path" )]
	public string ConfigFilePath { get; private set; }

	/// <summary>
	/// Root directory of this project, the folder its <see cref="ConfigFilePath"/> sits in. Fixed
	/// when the project is created, along with the filesystems rooted in it.
	/// </summary>
	[JsonIgnore]
	public DirectoryInfo RootDirectory { get; private set; }

	/// <summary>
	/// True if this project is active
	/// </summary>
	public bool Active { get; set; }

	/// <summary>
	/// True if this project is pinned, we'll prioritise it when sorting
	/// </summary>
	public bool Pinned { get; set; }

	/// <summary>
	/// When did the user last open this project?
	/// </summary>
	public DateTimeOffset LastOpened { get; set; }

	/// <summary>
	/// True if this project failed to load properly for some reason
	/// </summary>
	[JsonIgnore]
	public bool Broken { get; set; }

	/// <summary>
	/// Returns true if this project has previously been published. This is kind of a guess though
	/// because all it does is look to see if we have a published package cached with the same ident.
	/// </summary>
	[JsonIgnore]
	public bool IsPublished => Package.TryGetCached( Config.FullIdent, out _ );

	/// <summary>
	/// The URL to the package's page for editing
	/// </summary>
	[JsonIgnore]
	public string EditUrl => $"https://sbox.game/{Config.FullIdent.Replace( ".", "/" )}/edit";

	/// <summary>
	/// The URL to the package's page for viewing/linking
	/// </summary>
	[JsonIgnore]
	public string ViewUrl => $"https://sbox.game/{Config.FullIdent.Replace( ".", "/" )}/";

	/// <summary>
	/// Configuration of the project.
	/// </summary>
	[JsonIgnore]
	public DataModel.ProjectConfig Config { get; set; }

	/// <summary>
	/// If true this project isn't a 'real' project. It's likely a temporary project created with the
	/// intention to configure and publish a single asset.
	/// </summary>
	[JsonIgnore]
	public bool IsTransient { get; internal set; }

	/// <summary>
	/// If true this project isn't a 'real' project. It's likely a temporary project created with the
	/// intention to configure and publish a single asset.
	/// </summary>
	[JsonIgnore]
	public bool IsBuiltIn { get; internal set; }

	/// <summary>
	/// Called when the project is about to save
	/// </summary>
	internal Action OnSaveProject { get; set; }

	/// <summary>
	/// A filesystem into which compiled assemblies are written
	/// </summary>
	[JsonIgnore]
	internal MemoryFileSystem AssemblyFileSystem { get; }

	private Project()
	{
		AssemblyFileSystem = new MemoryFileSystem();
	}

	/// <summary>
	/// A project from its .sbproj on disk. Everything about where the project lives comes from this
	/// path, so a project can't be pointed somewhere else once it exists.
	/// </summary>
	[JsonConstructor]
	public Project( string configFilePath ) : this()
	{
		try
		{
			ConfigFilePath = NormalizeConfigFilePath( configFilePath );
			if ( ConfigFilePath is null ) return;

			RootDirectory = new DirectoryInfo( System.IO.Path.GetDirectoryName( ConfigFilePath ) );

			CreateFileSystems();
		}
		catch ( System.Exception e )
		{
			// A path we can't make sense of is a broken project, not an exception out of the
			// deserializer - the list it came from has to survive one bad entry.
			Log.Warning( e, $"Project path error ({e.Message}) - deactivating project" );
			Broken = true;
		}
	}

	/// <summary>
	/// A transient project rooted at <paramref name="rootDirectory"/>, or at nothing. There's no
	/// .sbproj behind one of these, see <c>Asset.Publishing.CreateTemporaryProject</c>.
	/// </summary>
	internal Project( DirectoryInfo rootDirectory ) : this()
	{
		IsTransient = true;
		RootDirectory = rootDirectory;

		CreateFileSystems();
	}

	/// <summary>
	/// A project is named by its .sbproj, but callers hand us the folder it's in just as often.
	/// </summary>
	internal static string NormalizeConfigFilePath( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return null;

		if ( !path.EndsWith( ".sbproj" ) )
			path = System.IO.Path.Combine( path, ".sbproj" );

		return System.IO.Path.GetFullPath( path );
	}

	internal void Dispose()
	{
		Compiler?.Dispose();
		Compiler = null;

		EditorCompiler?.Dispose();
		EditorCompiler = null;

		AssemblyFileSystem?.Dispose();

		DisposeFileSystems();
	}

	internal bool LoadMinimal()
	{
		if ( IsTransient )
			return false;

		try
		{
			Assert.True( RootDirectory?.Exists ?? false, $"{RootDirectory} does not exist" );

			var text = File.ReadAllText( ConfigFilePath );
			Config = JsonSerializer.Deserialize<DataModel.ProjectConfig>( text );
			Config.Init( ConfigFilePath );

			UpdateMockPackage();
			return true;
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Project config error ({e.Message}) - deactivating project" );
			Broken = true;
			Active = false;
			return false;
		}
	}

	internal void Load()
	{
		if ( !LoadMinimal() )
			return;

		try
		{
			UpdateCompiler();
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Project config error ({e.Message}) - deactivating project" );
			Broken = true;
			Active = false;
		}
	}

	/// <summary>
	/// Absolute path to the location of the <c>.sbproj</c> file of the project.
	/// </summary>
	public string GetRootPath() => RootDirectory.FullName;

	/// <summary>
	/// Gets the .sbproj file for this project
	/// </summary>
	/// <returns></returns>
	public string GetProjectPath() => System.IO.Directory.EnumerateFiles( GetRootPath(), "*.sbproj" ).FirstOrDefault();

	private readonly object fileSystemLock = new();

	/// <summary>
	/// A filesystem rooted at this project's folder.
	///
	/// Anything looking for one of our well known folders should go through this rather than
	/// touching System.IO directly. We always ask for them by their canonical name ("Code",
	/// "Assets"..) but what's on disk is whatever case the author's machine let them get away
	/// with, and on Linux that's the difference between a project having code and not. This
	/// resolves the case for us, and so does every path that comes back out of it.
	/// </summary>
	[JsonIgnore]
	internal BaseFileSystem FileSystem { get; private set; }

	/// <summary>
	/// The project's Code folder, or null if it hasn't got one.
	/// </summary>
	[JsonIgnore]
	internal BaseFileSystem CodeFileSystem { get; private set; }

	/// <summary>
	/// The project's Assets folder, or null if it hasn't got one.
	/// </summary>
	[JsonIgnore]
	internal BaseFileSystem AssetsFileSystem { get; private set; }

	/// <summary>
	/// The project's Localization folder, or null if it hasn't got one.
	/// </summary>
	[JsonIgnore]
	internal BaseFileSystem LocalizationFileSystem { get; private set; }

	/// <summary>
	/// The project's ProjectSettings folder, or null if it hasn't got one.
	/// </summary>
	[JsonIgnore]
	internal BaseFileSystem ProjectSettingsFileSystem { get; private set; }

	/// <summary>
	/// Open the project's folder, and one filesystem per well known folder in it. The project owns
	/// all of these, so mount them and hold onto them, but leave disposing them to us.
	/// </summary>
	private void CreateFileSystems()
	{
		lock ( fileSystemLock )
		{
			DisposeFileSystems();

			// A project can be pointed at nothing - see the publish wizard, which builds one to
			// decide whether it's including any code
			if ( RootDirectory is null || !RootDirectory.Exists ) return;

			FileSystem = new LocalFileSystem( RootDirectory.FullName );

			CodeFileSystem = CreateFolderFileSystem( "Code" );
			AssetsFileSystem = CreateFolderFileSystem( "Assets" );
			LocalizationFileSystem = CreateFolderFileSystem( "Localization" );
			ProjectSettingsFileSystem = CreateFolderFileSystem( "ProjectSettings" );
		}
	}

	/// <summary>
	/// A filesystem for one of our well known project folders, or null if the project hasn't got
	/// it - which callers can hand straight to Mount either way. The casing on disk is whatever
	/// the author's machine let them get away with; the filesystem sorts that out for us.
	/// </summary>
	private BaseFileSystem CreateFolderFileSystem( string name )
	{
		if ( !FileSystem.DirectoryExists( name ) ) return null;

		return FileSystem.CreateSubSystem( name );
	}

	private void DisposeFileSystems()
	{
		lock ( fileSystemLock )
		{
			CodeFileSystem?.Dispose();
			CodeFileSystem = null;

			AssetsFileSystem?.Dispose();
			AssetsFileSystem = null;

			LocalizationFileSystem?.Dispose();
			LocalizationFileSystem = null;

			ProjectSettingsFileSystem?.Dispose();
			ProjectSettingsFileSystem = null;

			FileSystem?.Dispose();
			FileSystem = null;
		}
	}

	/// <summary>
	/// Absolute path to one of our well known project folders, with the casing it has on disk.
	/// </summary>
	private string GetProjectFolder( string name ) => FileSystem?.GetFullPath( name ) ?? System.IO.Path.Combine( RootDirectory.FullName, name );

	/// <summary>
	/// Does this project have a well known folder called <paramref name="name"/>?
	/// </summary>
	private bool HasProjectFolder( string name ) => FileSystem?.DirectoryExists( name ) ?? false;

	/// <summary>
	/// Absolute path to the Code folder of the project.
	/// </summary>
	public string GetCodePath() => GetProjectFolder( "Code" );

	/// <summary>
	/// Returns true if the Code path exists
	/// </summary>
	public bool HasCodePath() => HasProjectFolder( "Code" );

	/// <summary>
	/// Absolute path to the Editor folder of the project.
	/// </summary>
	public string GetEditorPath() => GetProjectFolder( "Editor" );

	/// <summary>
	/// Returns true if the Editor path exists
	/// </summary>
	public bool HasEditorPath() => HasProjectFolder( "Editor" );

	/// <summary>
	/// Absolute path to the Assets folder of the project, or <see langword="null"/> if not set.
	/// </summary>
	public string GetAssetsPath() => GetProjectFolder( "Assets" );

	/// <summary>
	/// Absolute path to the Localization folder of the project, or <see langword="null"/> if not set.
	/// </summary>
	/// <returns></returns>
	public string GetLocalizationPath() => GetProjectFolder( "Localization" );

	/// <summary>
	/// Returns true if the Assets path exists
	/// </summary>
	public bool HasAssetsPath() => HasProjectFolder( "Assets" );

	internal void Save()
	{
		OnSaveProject?.Invoke();

		if ( Config == null )
			return;

		if ( IsTransient )
			return;

		if ( !ConfigFilePath.EndsWith( ".sbproj" ) ) return;

		var json = Config.ToJson();

		// Check if we need to do this first..
		try
		{
			if ( File.Exists( ConfigFilePath ) )
			{
				var existingContents = File.ReadAllText( ConfigFilePath );
				if ( json == existingContents ) return;
			}
		}
		catch ( System.Exception ) { }

		File.WriteAllText( ConfigFilePath, json );

		// update the package with new details
		UpdateMockPackage();
		UpdateCompiler();

		if ( Config.Type == "game" )
		{
			IGameInstanceDll.Current?.OnProjectConfigChanged( mockPackage );
		}
	}

	LocalPackage mockPackage;

	/// <summary>
	/// The package for this project. This is a mock up of the actual package.
	/// </summary>
	[JsonIgnore]
	public Package Package => UpdateMockPackage();

	LocalPackage UpdateMockPackage()
	{
		mockPackage ??= new LocalPackage( this );
		mockPackage.TypeName = Config.Type;
		mockPackage.Ident = Config.Ident;
		mockPackage.Title = Config.Title;
		mockPackage.PackageReferences = Config.PackageReferences?.ToArray() ?? Array.Empty<string>();
		mockPackage.EditorReferences = Config.EditorReferences?.ToArray() ?? Array.Empty<string>();

		mockPackage.Org = new Package.Organization
		{
			Ident = Config.Org,
			Title = Config.Org
		};

		mockPackage.Tags = Array.Empty<string>();

		// build a clean ident because the full ident will have #local
		var fullIdent = $"{mockPackage.Org.Ident}.{mockPackage.Ident}";

		//
		// Maybe we can fill in a bunch of stuff
		//
		if ( Package.TryGetCached( fullIdent, out var cachedPackage ) )
		{
			mockPackage.UpdateFromPackage( cachedPackage );
		}

		return mockPackage;
	}

	/// <summary>
	/// Return true if this project type uploads all the source files when it's published
	/// </summary>
	public bool IsSourcePublish()
	{
		return Config.Type == "library";
	}
}
