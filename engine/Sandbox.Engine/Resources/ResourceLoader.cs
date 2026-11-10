using Sandbox.Engine;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Sandbox;

internal static class ResourceLoader
{
	// Native resource extensions (with _c suffix) not covered by AssetTypeAttribute.
	private static readonly HashSet<string> NativeExtensions = new( StringComparer.OrdinalIgnoreCase )
	{
		".vmat_c", ".vmdl_c", ".vtex_c", ".shader_c", ".vanmgrph_c", ".ag_c"
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

	internal static void LoadAllGameResource( BaseFileSystem fileSystem, bool reloadExisting = false, Package sourcePackage = null )
	{
		var sw = Stopwatch.StartNew();
		var types = GlobalContext.Current.TypeLibrary.GetAttributes<AssetTypeAttribute>().DistinctBy( x => x.Extension )
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
			if ( !reloadExisting && ResourceLibrary.TryGet<GameResource>( file.Trim( '/' ), out var existing ) && !existing.IsPromise )
				continue;

			try
			{
				var se = Game.Resources.LoadGameResource( type, file, fileSystem, true, sourcePackage );
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

	internal static async Task LoadAllGameResourceAsync( BaseFileSystem fileSystem, CancellationToken ct = default, bool reloadExisting = false, Package sourcePackage = null, Task<List<string>> preEnumeratedFiles = null, Func<string, bool> includeExtension = null, Func<string, bool> includeFile = null )
	{
		var sw = Stopwatch.StartNew();
		var types = GlobalContext.Current.TypeLibrary.GetAttributes<AssetTypeAttribute>().DistinctBy( x => x.Extension )
			.ToDictionary( x => $".{x.Extension}_c", x => x, StringComparer.OrdinalIgnoreCase );

		var allExtensions = new HashSet<string>( types.Keys, StringComparer.OrdinalIgnoreCase );
		allExtensions.UnionWith( NativeExtensions );

		var allFiles = new List<string>();
		var enumeratedFiles = preEnumeratedFiles is null ? fileSystem.FindFile( "/", "*", true ) : await preEnumeratedFiles;
		foreach ( var file in enumeratedFiles )
		{
			ct.ThrowIfCancellationRequested();
			allFiles.Add( file );
			if ( allExtensions.Contains( System.IO.Path.GetExtension( file ) ) )
				Game.Resources.RegisterPath( file );
			if ( sw.ElapsedMilliseconds > 8 ) { LoadingScreen.Subtitle = System.IO.Path.GetFileName( file ); await Task.Yield(); sw.Restart(); }
		}

		var allResources = new List<GameResource>();

		for ( int i = 0; i < allFiles.Count; i++ )
		{
			var file = allFiles[i];
			ct.ThrowIfCancellationRequested();
			LoadProgress.Report( (double)i / allFiles.Count );
			var extension = System.IO.Path.GetExtension( file );
			if ( !types.TryGetValue( extension, out var type ) ) continue;
			if ( includeExtension is not null && !includeExtension( extension ) ) continue;
			if ( includeFile is not null && !includeFile( file ) ) continue;

			// Skip resources that are already fully loaded - this allows calling this method
			// multiple times (e.g. once per package) without redundant work.
			if ( !reloadExisting && ResourceLibrary.TryGet<GameResource>( file.Trim( '/' ), out var existing ) && !existing.IsPromise )
				continue;

			try
			{
				var se = Game.Resources.LoadGameResource( type, file, fileSystem, true, sourcePackage, knownToExist: true );
				if ( se != null ) allResources.Add( se );
			}
			catch ( Exception ex )
			{
				Log.Warning( ex, $"Exception when trying to load {file}" );
			}

			if ( sw.ElapsedMilliseconds > 8 ) { LoadingScreen.Subtitle = System.IO.Path.GetFileName( file ); await Task.Yield(); sw.Restart(); }
		}

		foreach ( var resource in allResources )
		{
			ct.ThrowIfCancellationRequested();
			resource.PostLoadInternal();
			if ( sw.ElapsedMilliseconds > 8 ) { LoadingScreen.Subtitle = System.IO.Path.GetFileName( resource.ResourcePath ); await Task.Yield(); sw.Restart(); }
		}

		LoadingScreen.Subtitle = null;

		foreach ( var type in types )
			AddWatcherForType( type.Value );
	}


	/// <summary>
	/// <paramref name="roots"/> and everything they reference, as <see cref="Resource.FixPath"/> paths - read from the
	/// external reference list compiled into each GameResource, so nothing's loaded. Native resources are taken as they
	/// are, not opened: what they reference loads with them.
	/// </summary>
	internal static HashSet<string> FindReferenced( BaseFileSystem fileSystem, IEnumerable<string> roots )
	{
		var found = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var pending = new Stack<string>( roots.Select( Resource.FixPath ) );

		while ( pending.TryPop( out var path ) )
		{
			if ( string.IsNullOrEmpty( path ) || !found.Add( path ) ) continue;
			if ( !Game.Resources.TryGetType( System.IO.Path.GetExtension( path ), out _ ) ) continue;

			foreach ( var reference in ReadReferences( fileSystem, path + "_c" ) )
				pending.Push( Resource.FixPath( reference ) );
		}

		return found;
	}

	/// <summary>
	/// The resources a compiled file references - its RERL block, laid out as ResourceWriter.BuildRERLBlock writes it
	/// </summary>
	static List<string> ReadReferences( BaseFileSystem fileSystem, string compiledPath )
	{
		var references = new List<string>();
		if ( !fileSystem.FileExists( compiledPath ) ) return references;

		var block = Game.Resources.ReadCompiledResourceBlock( "RERL", fileSystem.ReadAllBytes( compiledPath ) );
		if ( block is null || block.Length < 8 ) return references;

		var entries = BinaryPrimitives.ReadInt32LittleEndian( block );
		var count = BinaryPrimitives.ReadUInt32LittleEndian( block.AsSpan( 4 ) );

		for ( var i = 0; i < count; i++ )
		{
			// uint64 id, then the name - an offset from where it's stored
			var nameField = entries + i * 16 + 8;
			if ( nameField < 0 || nameField + 4 > block.Length ) break;

			var start = nameField + BinaryPrimitives.ReadInt32LittleEndian( block.AsSpan( nameField ) );
			if ( start < 0 || start >= block.Length ) continue;

			var length = block.AsSpan( start ).IndexOf( (byte)0 );
			if ( length < 0 ) length = block.Length - start;

			references.Add( Encoding.UTF8.GetString( block, start, length ) );
		}

		return references;
	}

	static Dictionary<string, FileWatch> Watchers = new();

	static void AddWatcherForType( AssetTypeAttribute type )
	{
		if ( string.IsNullOrEmpty( type.Extension ) )
			return;

		// Watcher already set up for this type - no need to allocate another one.
		if ( Watchers.ContainsKey( type.TargetType.AssemblyQualifiedName ) )
			return;

		var watcher = EngineFileSystem.Mounted.Watch( $"*.{type.Extension}_c" );
		watcher.OnChanges += ( w ) => OnAssetFilesChanged( w, type );

		Watchers[type.TargetType.AssemblyQualifiedName] = watcher;
	}

	private static void OnAssetFilesChanged( FileWatch watch, AssetTypeAttribute type )
	{
		var fs = EngineFileSystem.Mounted;

		Dictionary<string, (GameResource resource, ulong id)> removes = new();

		foreach ( var f in watch.Changes )
		{
			string path = Resource.FixPath( f );
			if ( !path.EndsWith( "_c" ) )
				path += "_c";

			// Added?
			if ( !ResourceLibrary.TryGet<GameResource>( path, out var resource ) || resource.IsPromise )
			{
				if ( !fs.FileExists( path ) )
					continue; // but doesn't exist? wtf

				Log.Info( $"Detected Added File {path}" );
				Game.Resources.LoadGameResource( type, path, fs );
				continue;
			}

			// Removed?
			if ( !fs.FileExists( path ) )
			{
				// but defer until later incase it's actually just been moved
				removes.TryAdd( path, (resource, resource.ResourceIdLong) );
				continue;
			}

			// Modified
			Span<byte> data = fs.ReadAllBytes( path );
			if ( data.Length <= 3 )
			{
				Log.Warning( $"Couldn't load json data from {path}" );
				continue;
			}

			// check what part of the resource was modified - compiled or source - do events
			bool isModifiedCompiled = resource.TryLoadFromData( data );
			bool isModifiedSource = false;
			if ( isModifiedCompiled && fs.FileExists( resource.ResourcePath ) )
			{
				var jsonBlob = fs.ReadAllText( resource.ResourcePath );
				if ( string.IsNullOrEmpty( jsonBlob ) ) continue;

				var sourceHash = jsonBlob.FastHash();
				if ( sourceHash != resource.LastSavedSourceHash && resource.LastSavedSourceHash != 0 )
				{
					IToolsDll.Current?.RunEvent<ResourceLibrary.IEventListener>( i => i.OnExternalChanges( resource ) );
					isModifiedSource = true;
				}
			}

			resource.PostReloadInternal();

			if ( isModifiedSource )
			{
				IToolsDll.Current?.RunEvent<ResourceLibrary.IEventListener>( i => i.OnExternalChangesPostLoad( resource ) );
			}
		}

		foreach ( (string path, (GameResource resource, ulong id)) in removes )
		{
			if ( id != resource.ResourceIdLong )
				continue; // id has been reassigned - this is a move, not a delete

			Log.Info( $"Detected Asset File Deleted {path}" );
			resource.DestroyInternal();
		}
	}

	internal static void Clear()
	{
		// Dispose of watchers too
		foreach ( var watcher in Watchers ) watcher.Value.Dispose();
		Watchers.Clear();
	}
}
