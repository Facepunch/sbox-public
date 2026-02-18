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
	public string ConfigFilePath { get; set; }

	/// <summary>
	/// Root directory of this project
	/// </summary>
	[JsonIgnore]
	public DirectoryInfo RootDirectory { get; set; }

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

	public Project()
	{
		AssemblyFileSystem = new MemoryFileSystem();
	}

	internal void Dispose()
	{
		Compiler?.Dispose();
		Compiler = null;

		EditorCompiler?.Dispose();
		EditorCompiler = null;

		AssemblyFileSystem?.Dispose();
	}

	internal bool LoadMinimal()
	{
		if ( IsTransient )
			return false;

		try
		{
			RootDirectory = new DirectoryInfo( System.IO.Path.GetDirectoryName( ConfigFilePath ) );
			Assert.True( RootDirectory.Exists, $"{RootDirectory} does not exist" );

			if ( !ConfigFilePath.EndsWith( ".sbproj" ) )
			{
				// Turn Path from myproject/ into myproject/.sbproj
				ConfigFilePath = System.IO.Path.Combine( RootDirectory.FullName, ".sbproj" );
			}

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

	/// <summary>
	/// Absolute path to the Code folder of the project.
	/// </summary>
	public string GetCodePath() => System.IO.Path.Combine( RootDirectory.FullName, "Code" );

	/// <summary>
	/// Returns true if the Code path exists
	/// </summary>
	public bool HasCodePath() => RootDirectory is not null && System.IO.Directory.Exists( GetCodePath() );

	/// <summary>
	/// Absolute path to the Editor folder of the project.
	/// </summary>
	public string GetEditorPath() => System.IO.Path.Combine( RootDirectory.FullName, "Editor" );

	/// <summary>
	/// Returns true if the Editor path exists
	/// </summary>
	public bool HasEditorPath() => RootDirectory is not null && System.IO.Directory.Exists( GetEditorPath() );

	/// <summary>
	/// Absolute path to the Assets folder of the project, or <see langword="null"/> if not set.
	/// </summary>
	public string GetAssetsPath() => System.IO.Path.Combine( RootDirectory.FullName, "Assets" );

	/// <summary>
	/// Absolute path to the Localization folder of the project, or <see langword="null"/> if not set.
	/// </summary>
	/// <returns></returns>
	public string GetLocalizationPath() => System.IO.Path.Combine( RootDirectory.FullName, "Localization" );

	/// <summary>
	/// Returns true if the Assets path exists
	/// </summary>
	public bool HasAssetsPath() => RootDirectory is not null && System.IO.Directory.Exists( GetAssetsPath() );

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
