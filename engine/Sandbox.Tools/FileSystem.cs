using System.IO;

namespace Editor;

/// <summary>
/// A filesystem that can be accessed by the game.
/// </summary>
public static class FileSystem
{
	/// <summary>
	/// Paths from tool addons which are mounted.
	/// </summary>
	public static BaseFileSystem Mounted => Sandbox.FileSystem.Mounted;

	/// <summary>
	/// Root of the game's folder.
	/// </summary>
	public static BaseFileSystem Root => EngineFileSystem.Root;

	/// <summary>
	/// The engine /game/.source2/ folder for temporary files and caches.
	/// </summary>
	public static BaseFileSystem Temporary { get; internal set; }

	/// <summary>
	/// The engine /game/config/ folder
	/// </summary>
	public static BaseFileSystem Config => EngineFileSystem.Config;

	/// <summary>
	/// The engine /game/.source2/http/ folder.
	/// </summary>
	public static BaseFileSystem WebCache { get; internal set; }

	/// <summary>
	/// The current project's .sbox/ folder for temporary files and caches.
	/// </summary>
	public static BaseFileSystem ProjectTemporary { get; internal set; }

	/// <summary>
	/// The current project's .sbox/cloud/ folder. We download files from sbox.game right into this filesystem.
	/// </summary>
	public static BaseFileSystem Cloud { get; internal set; }

	/// <summary>
	/// The current project's .sbox/transient/ folder. This is where assets are created at runtime. These are assets
	/// that are created by another asset,that don't need to be stored in source control or anything - because they
	/// can get re-created at will.
	/// </summary>
	public static BaseFileSystem Transient { get; internal set; }

	/// <summary>
	/// Content from active addons (and content paths)
	/// </summary>
	public static BaseFileSystem Content { get; internal set; }

	/// <summary>
	/// The current project's ProjectSettings folder
	/// </summary>
	public static BaseFileSystem ProjectSettings { get; internal set; }

	/// <summary>
	/// The current project's Libraries folder
	/// </summary>
	public static BaseFileSystem Libraries { get; internal set; }

	/// <summary>
	/// The current project's Localization folder
	/// </summary>
	public static BaseFileSystem Localization { get; internal set; }

	/// <summary>
	/// Should be called on startup and whenever the mounted local addons have changed
	/// </summary>
	internal static void RebuildContentPath()
	{
		Content?.Dispose();
		Content = null;

		Content = new AggregateFileSystem();
		Content.CreateAndMount( EngineFileSystem.Root, "/core/" );
		Content.CreateAndMount( EngineFileSystem.Root, "/addons/base/Assets/" );
		Content.CreateAndMount( EngineFileSystem.Root, "/addons/citizen/Assets/" );
		Content.Mount( Cloud );

		foreach ( var addon in Project.All.Where( x => x.Active ) )
		{
			var contentPath = addon.GetAssetsPath();
			if ( string.IsNullOrWhiteSpace( contentPath ) ) continue;
			if ( !System.IO.Directory.Exists( contentPath ) ) continue;

			Content.CreateAndMount( contentPath );
		}

		var watch = Content.Watch();
		watch.OnChangedFile += OnContentFileChanged;

		// Mount assets from tool addons for Qt, example being hammer outliner
		foreach ( var addon in Project.All.Where( x => x.Active && x.Config.Type == "tool" ) )
		{
			var contentPath = addon.GetAssetsPath();
			if ( string.IsNullOrWhiteSpace( contentPath ) ) continue;
			if ( !System.IO.Directory.Exists( contentPath ) ) continue;

			QDir.addSearchPath( "toolimages", contentPath );
		}
	}

	/// <summary>
	/// Called when a file has changed on the <see cref="Content"/> path.
	/// </summary>
	private static void OnContentFileChanged( string filename )
	{
		ThreadSafe.AssertIsMainThread();

		// Check to see if this asset was deleted from explorer - and mark it as deleted in the asset system.
		if ( AssetSystem.FindByPath( filename ) is Asset asset )
		{
			var fileExists = System.IO.File.Exists( asset.AbsolutePath );
			if ( !asset.IsDeleted && !fileExists )
			{
				asset.IsDeleted = true;
			}

			// Source file for a gameresource was edited, let's compile it (for detecting changes from version control)
			if ( asset.TryLoadResource<GameResource>( out var gameResource ) )
			{
				asset.Compile( false );
			}
		}

		EditorEvent.Run( "content.changed", filename );
	}

	/// <summary>
	/// Stop the game from triggering a hotload for this file - because presumably you have
	/// already reloaded it.
	/// </summary>
	public static void SuppressNextHotload()
	{
		FileWatch.SuppressWatchers = RealTime.Now + 0.5f;
	}

	/// <summary>
	/// Initialize the editor filesytems from this project, which is assumably the main game project.
	/// </summary>
	internal static void InitializeFromProject( Project project )
	{
		var root = project.GetRootPath();

		var projectsFolder = Path.Combine( root, "ProjectSettings" );
		Directory.CreateDirectory( projectsFolder );
		ProjectSettings = new LocalFileSystem( projectsFolder );

		var librariesFolder = Path.Combine( root, "Libraries" );
		Directory.CreateDirectory( librariesFolder );
		Libraries = new LocalFileSystem( librariesFolder );

		var localizationFolder = Path.Combine( root, "Localization" );
		Directory.CreateDirectory( localizationFolder );
		Localization = new LocalFileSystem( localizationFolder );

		var sboxFolder = Path.Combine( root, ".sbox" );
		Directory.CreateDirectory( sboxFolder );
		ProjectTemporary = new LocalFileSystem( sboxFolder );

		// Set folder as hidden. Will hide it from explorer (by default) and from the asset browser
		var di = new DirectoryInfo( sboxFolder );
		di.Attributes |= FileAttributes.Hidden;

		var cloudFolder = Path.Combine( sboxFolder, "cloud" );
		Directory.CreateDirectory( cloudFolder );
		Cloud = new LocalFileSystem( cloudFolder );
		Mounted.Mount( Cloud );

		var transientFolder = Path.Combine( sboxFolder, "transient" );
		Directory.CreateDirectory( transientFolder );
		Transient = new LocalFileSystem( transientFolder );
		Mounted.Mount( Transient );
	}
}
