

using Sandbox.Engine;

namespace Sandbox;

internal static class EngineFileSystem
{
	public static LocalFileSystem Root { get; private set; }
	public static BaseFileSystem Config { get; private set; }
	public static BaseFileSystem Addons { get; private set; }
	public static BaseFileSystem Data { get; private set; }
	public static BaseFileSystem CoreContent { get; private set; }
	public static BaseFileSystem Mounted => GlobalContext.Current.FileMount;

	/// <summary>
	/// Content from libraries. This only exists in editor.
	/// </summary>
	public static BaseFileSystem LibraryContent { get; private set; }

	/// <summary>
	/// For tools, maintain a list of mounted addon content paths
	/// </summary>
	public static BaseFileSystem Assets { get; private set; }

	internal static BaseFileSystem DownloadedFiles { get; private set; }

	/// <summary>
	/// A place to write files temporarily. This is stored in memory so 
	/// cleaning up after yourself is a good idea (!)
	/// </summary>
	public static BaseFileSystem Temporary { get; private set; }

	/// <summary>
	/// The .source2/temp folder
	/// </summary>
	public static BaseFileSystem EditorTemporary { get; private set; }

	/// <summary>
	/// The folder holding the project's settings files
	/// </summary>
	internal static BaseFileSystem ProjectSettings { get; set; }

	/// <summary>
	/// Don't try to use the filesystem until you've called this!
	/// </summary>
	internal static void Initialize( string rootFolder, bool skipBaseFolderInit = false )
	{
		if ( Root != null )
			throw new System.Exception( "Filesystem Multi-Initialize" );

		Root = new LocalFileSystem( rootFolder );
		Temporary = new MemoryFileSystem();

		if ( skipBaseFolderInit ) return;

		if ( Application.IsEditor )
		{
			LibraryContent = new AggregateFileSystem();
			EditorTemporary = Root.CreateSubSystem( "/.source2/temp" );
		}

		Assets = new AggregateFileSystem();
		CoreContent = new AggregateFileSystem();

		if ( Application.IsStandalone )
		{
			CoreContent.CreateAndMount( Root, "/core/" );

			Assets.CreateAndMount( Root, "/core/" );
			Assets.CreateAndMount( Root, "/addons/base/assets" );
		}
		else
		{
			CoreContent.CreateAndMount( Root, "/core/" );
			CoreContent.CreateAndMount( Root, "/addons/base/assets/" );
			CoreContent.CreateAndMount( Root, "/addons/citizen/assets/" );

			Assets.CreateAndMount( Root, "/core/" );
			Assets.CreateAndMount( Root, "/addons/base/assets/" );
			Assets.CreateAndMount( Root, "/addons/citizen/assets/" );
		}
	}

	/// <summary>
	/// Setup Config parameter
	/// </summary>
	internal static void InitializeConfigFolder( string name = "/config" )
	{
		Assert.NotNull( name );
		Assert.NotNull( Root );

		Root.CreateDirectory( "/config" );
		Config = Root.CreateSubSystem( "/config" );
	}

	/// <summary>
	/// Setup Addons parameter (there's no reason for this to exist now?)
	/// </summary>
	internal static void InitializeAddonsFolder( string name = "/addons" )
	{
		Assert.NotNull( name );
		Assert.NotNull( Root );

		Addons = Root.CreateSubSystem( "/addons" );
	}

	/// <summary>
	/// Setup Download folder
	/// </summary>
	internal static void InitializeDownloadsFolder( string name = "/download" )
	{
		Assert.NotNull( name );
		Assert.NotNull( Root );

		// alex: Don't bother if we're in standalone mode, because games aren't able
		// to download anything from the backend
		if ( Application.IsStandalone )
			return;

		Root.CreateDirectory( $"{name}" );
		Root.CreateDirectory( $"{name}/.sv" );
		DownloadedFiles = Root.CreateSubSystem( $"{name}" );
	}

	/// <summary>
	/// Setup Addons parameter (there's no reason for this to exist now?)
	/// </summary>
	internal static void InitializeDataFolder( string name = "/data" )
	{
		Assert.NotNull( name );
		Assert.NotNull( Root );

		Root.CreateDirectory( $"{name}" );
		Data = Root.CreateSubSystem( $"{name}" );
	}

	/// <summary>
	/// Should only be called at the very death
	/// </summary>
	internal static void Shutdown()
	{
		Root = null;
		Config = null;

		DownloadedFiles?.Dispose();
		DownloadedFiles = null;

		Addons?.Dispose();
		Addons = null;

		Root?.Dispose();
		Root = null;
	}

	internal static void AddContentPath( string v )
	{
		CoreContent.Mount( new LocalFileSystem( v ) );
	}

	internal static void AddAssetPath( string ident, string path )
	{
		Mounted.Mount( new LocalFileSystem( path ) );
		NativeEngine.FullFileSystem.AddProjectPath( "xxx", path );
	}
}
