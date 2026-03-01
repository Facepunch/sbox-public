using Sandbox.Engine;
using System.Diagnostics;

namespace Sandbox;

internal static class ResourceLoader
{
	// Native resource extensions (with _c suffix) not covered by AssetTypeAttribute.
	private static readonly HashSet<string> NativeExtensions = new( StringComparer.OrdinalIgnoreCase )
	{
		".vmat_c", ".vmdl_c", ".vtex_c", ".shader_c", ".vanmgrph_c"
	};

	/// Registers resource paths into PathIndex without loading them, for any file whose
	/// extension is in <paramref name="extensions"/>. Called during LoadAllGameResource.
	private static void RegisterPaths( ReadOnlySpan<string> files, IReadOnlySet<string> extensions )
	{
		foreach ( var file in files )
		{
			if ( !extensions.Contains( System.IO.Path.GetExtension( file ) ) )
				continue;

			// RegisterPath calls FixPath internally, which strips the _c suffix.
			Game.Resources.RegisterPath( file );
		}
	}

	internal static void LoadAllGameResource( BaseFileSystem fileSystem )
	{
		var sw = Stopwatch.StartNew();
		var types = Game.TypeLibrary.GetAttributes<AssetTypeAttribute>().DistinctBy( x => x.Extension )
			.ToDictionary( x => $".{x.Extension}_c", x => x, StringComparer.OrdinalIgnoreCase );

		var allFiles = fileSystem.FindFile( "/", "*", true ).ToArray();

		// Union GameResource extensions with native-only ones so PathIndex covers everything.
		var allExtensions = new HashSet<string>( types.Keys, StringComparer.OrdinalIgnoreCase );
		allExtensions.UnionWith( NativeExtensions );

		RegisterPaths( allFiles, allExtensions );

		var allResources = new List<GameResource>();

		foreach ( var file in allFiles )
		{
			var extension = System.IO.Path.GetExtension( file );

			if ( !types.TryGetValue( extension, out var type ) )
				continue;

			// Skip resources that are already fully loaded - this allows calling this method
			// multiple times (e.g. once per package) without redundant work.
			if ( ResourceLibrary.TryGet<GameResource>( file.Trim( '/' ), out var existing ) && !existing.IsPromise )
				continue;

			try
			{
				var se = Game.Resources.LoadGameResource( type, file, fileSystem, true );
				if ( se != null ) allResources.Add( se );
			}
			catch ( Exception ex )
			{
				Log.Warning( ex, $"Exception when trying to load {file}" );
			}
		}

		//
		// When we're loading a bunch of GameResource we defer their PostLoad until everything is loaded.
		// Everyone is gonna wanna do Resource.Get<>() within their PostLoad and not care about load order.
		// This keeps things intuitive for end users.
		//
		foreach ( var resource in allResources )
		{
			resource.PostLoadInternal();
		}

		foreach ( var type in types )
		{
			AddWatcherForType( type.Value );
		}

		// TODO: Check for edited but not saved OR recompiled assets and load in their values on server/client
		// like editing an asset while the gamemode is running would?
	}





	static Dictionary<string, FileWatch> Watchers = new();

	static void AddWatcherForType( AssetTypeAttribute type )
	{
		// Watcher already set up for this type - no need to allocate another one.
		if ( Watchers.ContainsKey( type.Name ) )
			return;

		var watcher = EngineFileSystem.Mounted.Watch( $"*.{type.Extension}_c" );
		watcher.OnChanges += ( w ) => OnAssetFilesChanged( w, type );

		Watchers[type.Name] = watcher;
	}

	private static void OnAssetFilesChanged( FileWatch watch, AssetTypeAttribute type )
	{
		foreach ( var change in watch.Changes )
		{
			OnAssetFileChanged( change, type );
		}
	}

	static void OnAssetFileChanged( string file, AssetTypeAttribute type )
	{
		var fs = EngineFileSystem.Mounted;

		if ( !file.EndsWith( "_c" ) )
			file += "_c";

		//
		// Asset doesn't exist, maybe just added?
		//
		if ( !ResourceLibrary.TryGet<GameResource>( file.Trim( '/' ), out var asset ) || asset.IsPromise )
		{
			// file wasn't found, so I don't know what was happening.
			if ( !fs.FileExists( file ) )
				return;

			Log.Info( $"Detected Added File {file}" );
			Game.Resources.LoadGameResource( type, file, fs );
			return;
		}

		//
		// File was removed, tell the asset system it died
		//
		if ( !fs.FileExists( file ) )
		{
			Log.Info( $"Detected Asset File Deleted {file}" );

			// Removes from ResourceLibrary
			asset.DestroyInternal();
			return;
		}

		Span<byte> data = fs.ReadAllBytes( file );

		if ( data.Length <= 3 )
		{
			Log.Warning( $"Couldn't load json data from {file}" );
			return;
		}

		bool hasCompiledChanges = asset.TryLoadFromData( data );
		bool externalChanges = false;
		if ( hasCompiledChanges )
		{
			// check for source file changes
			if ( fs.FileExists( asset.ResourcePath ) )
			{
				var jsonBlob = fs.ReadAllText( asset.ResourcePath );
				if ( string.IsNullOrEmpty( jsonBlob ) ) return;

				var sourceHash = jsonBlob.FastHash();
				if ( sourceHash != asset.LastSavedSourceHash && asset.LastSavedSourceHash != 0 )
				{
					IToolsDll.Current?.RunEvent<ResourceLibrary.IEventListener>( i => i.OnExternalChanges( asset ) );
					externalChanges = true;
				}
			}
		}

		asset.PostReloadInternal();

		if ( externalChanges )
		{
			IToolsDll.Current?.RunEvent<ResourceLibrary.IEventListener>( i => i.OnExternalChangesPostLoad( asset ) );
		}
	}

	internal static void Clear()
	{
		// Dispose of watchers too
		foreach ( var watcher in Watchers ) watcher.Value.Dispose();
		Watchers.Clear();
	}
}
