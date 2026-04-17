using NativeEngine;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace Editor;

/// <summary>
/// The asset system, provides access to all the assets.
/// </summary>
public static partial class AssetSystem
{
	static Logger log = new Logger( "AssetSystem" );

	[SkipHotload]
	static Dictionary<ulong, Asset> allAssets = new();

	[SkipHotload] static ConcurrentDictionary<string, Asset> assetsByPath = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// All the assets that are being tracked by the asset system. Does not include deleted assets.
	/// </summary>
	public static IEnumerable<Asset> All => allAssets.Values.Where( x => !x.IsDeleted );
	static bool HasChanges;
	static bool IsInitialized = false;

	static HashSet<Asset> UpdateQueue = new();

	/// <summary>
	/// Called after the asset types have been loaded from
	/// </summary>
	internal static void PreInitialize()
	{
		log.Trace( "PreInitialize" );

		foreach ( var type in AssetType.AssetTypeCache )
		{
			type.Value.Init();
		}
	}

	internal static void InitializeFromProject( Project project )
	{
		string path = System.IO.Path.Combine( project.GetRootPath(), ".sbox", "cloud.db" );
		CloudDirectory = new CloudAssetDirectory( path );

		HasChanges = true;
		IsInitialized = true;
		Tick();
	}

	internal static void Shutdown()
	{
		CloudDirectory?.Dispose();
		CloudDirectory = null;
	}

	internal static void AssetAdded( IAsset native )
	{
		// log.Trace( $"Asset Added: {native.GetAbsolutePath_Transient()}" );

		var a = new NativeAsset( native );

		allAssets[a.AssetId] = a;
		UpdateQueue.Add( a );
		HasChanges = true;
	}


	static void UpdateAsset( Asset asset, bool compileImmediately = true )
	{
		asset.UpdateInternals( compileImmediately );

		if ( string.IsNullOrWhiteSpace( asset.Path ) )
			return;

		assetsByPath[asset.Path] = asset;
		assetsByPath[asset.AbsolutePath] = asset;
		assetsByPath[asset.RelativePath] = asset;
		assetsByPath[asset.AbsoluteCompiledPath] = asset;
	}


	/// <summary>
	/// This is only called on startup. The cache is loaded, so a bunch of assets are known,
	/// then it does a bit of research and sees that the asset is removed, and it can remove it now.
	/// This is the only point where an Asset is actually destroyed, during the mainloop the asset
	/// is just marked as deleted but never destroyed.
	/// </summary>
	internal static void AssetRemoved( uint index )
	{
		var a = allAssets[index];
		Assert.NotNull( a );

		log.Trace( $"Removed: {a}" );

		if ( a.Path is not null ) assetsByPath.TryRemove( a.Path, out _ );
		if ( a.AbsolutePath is not null ) assetsByPath.TryRemove( a.AbsolutePath, out _ );
		if ( a.RelativePath is not null ) assetsByPath.TryRemove( a.RelativePath, out _ );
		if ( a.AbsoluteCompiledPath is not null ) assetsByPath.TryRemove( a.AbsoluteCompiledPath, out _ );

		// Can it ever come back??
		UpdateQueue.Remove( a );
		allAssets.Remove( index );
		a.OnRemoved();

	}

	internal static void RecordAssetOpen( uint index )
	{
		var a = allAssets[index];
		Assert.NotNull( a );

		a.LastOpened = DateTime.Now;

	}

	internal static void AssetChanged( uint index )
	{
		var asset = allAssets[index];
		UpdateAsset( asset );

		if ( UpdateQueue.Add( asset ) )
		{
			NativeAssetProcessor.OnAssetChanged( asset );

			HasChanges = true;
		}
	}

	internal static void UpdateAssetAutoTags( uint index )
	{
		allAssets[index].UpdateAutoTags();
	}

	internal static void AssetScanComplete()
	{
		foreach ( var type in AssetType.AssetTypeCache )
		{
			type.Value.Init();
		}

		HasChanges = true;
		Tick();
		DeleteOrphans();
	}

	[EditorEvent.Frame]
	internal static void Tick()
	{
		if ( !HasChanges || !IsInitialized ) return;
		HasChanges = false;

		// Everything below is safe to do in parallel as long as our dependency info is up to date
		// Which needs to be done on the main thread
		foreach ( var asset in UpdateQueue )
		{
			if ( asset is NativeAsset nativeAsset )
			{
				nativeAsset.native.RequireDependencyInfo_Virtual();
			}
		}

		Parallel.ForEach( UpdateQueue, ( Asset asset ) => UpdateAsset( asset, false ) );

		// Thumbnails have their own queue, so do that all in main
		foreach ( var asset in UpdateQueue )
		{
			EditorEvent.RunInterface<IEventListener>( x => x.OnAssetChanged( asset ) );

			if ( asset.HasCachedThumbnail )
			{
				asset.RebuildThumbnail( true );
			}

			if ( !asset.IsCompiled && asset.AssetType.IsGameResource )
			{
				asset.Compile( false );
			}
		}

		UpdateQueue.Clear();

		EditorEvent.RunInterface<IEventListener>( x => x.OnAssetSystemChanges() );
	}

	/// <summary>
	/// Find an asset by path.
	/// </summary>
	/// <param name="path">The file path to an asset. Can be absolute or relative.</param>
	public static Asset FindByPath( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		path = path.Replace( '\\', '/' );
		path = path.TrimStart( '/' );

		if ( assetsByPath.TryGetValue( path, out var asset ) && !asset.IsDeleted )
		{
			return asset;
		}

		return null;
	}

	internal static Asset Get( IAsset asset )
	{
		return allAssets[asset.GetAssetIndexInt()];
	}

	internal static Asset Get( uint index )
	{
		return allAssets[index];
	}

	internal static void RegisterAssetType( int id, IAssetType assetType )
	{
		var color = Color.Parse( assetType.GetColor() );

		var at = new AssetType
		{
			FriendlyName = assetType.GetFriendlyName(),
			FileExtension = assetType.GetPrimaryExtension(),
			HiddenByDefault = assetType.HideTypeByDefault(),
			PrefersIconThumbnail = assetType.PrefersIconForThumbnail(),
			IconPathSmall = assetType.GetIconSm(),
			IconPathLarge = assetType.GetIconLg(),
			IsSimpleAsset = assetType.IsSimpleAsset(),
			HasDependencies = assetType.HasDependencies(),
			Category = assetType.GetCategory(),
			Color = color ?? Color.Gray
		};

		at.AllFileExtensions.Add( at.FileExtension );

		var additionalExtensions = CUtlVectorString.Create( 8, 8 );
		assetType.GetAdditionalExtensions( additionalExtensions );

		for ( var i = 0; i < additionalExtensions.Count(); ++i )
		{
			at.AllFileExtensions.Add( additionalExtensions.Element( i ) );
		}

		additionalExtensions.DeleteThis();

		AssetType.AssetTypeCache[id] = at;

		at.Init();
	}

	/// <summary>
	/// If you just created an asset, you probably want to immediately register it
	/// </summary>
	public static Asset RegisterFile( string absoluteFilePath )
	{
		ThreadSafe.AssertIsMainThread();

		var asset = IAssetSystem.RegisterAssetFile( absoluteFilePath );
		if ( !asset.IsValid ) return null;

		var ret = Get( asset.GetAssetIndexInt() );

		// Make sure the ResoureLibrary has proper loaded version, so that properties that target
		// GameResources do not break child assets if child asset was not loaded with Asset.CreateUI beforehand.
		ret.TryLoadGameResource( typeof( GameResource ), out _, true );

		return ret;
	}

	/// <summary>
	/// Delete orphaned trivial children. These are things that are generated for
	/// usage by an asset, but aren't referenced by anything, so are useless.
	/// </summary>
	public static void DeleteOrphans()
	{
		// Only delete orphans from the current project's transient folder.
		// Engine-shipped transients (e.g. addons/menu/Transients/) are shared
		// across all projects and must not be deleted.
		var transientRoot = FileSystem.Transient?.GetFullPath( "/" )?.NormalizeFilename( false );

		var orphans = All
						.Where( x => x.IsTrivialChild )
						.Where( x => !x.IsDeleted )
						.Where( x => x.AssetType == AssetType.Texture ) // Note - gib models can be trivial children too, but I don't want to push my luck
						.Where( x => transientRoot is not null && x.AbsolutePath.StartsWith( transientRoot, StringComparison.OrdinalIgnoreCase ) )
						.Where( x => x.GetDependants( false ).Count == 0 && x.GetParents( false ).Count == 0 )
						.ToArray();

		foreach ( var o in orphans )
		{
			log.Trace( $"Deleting Orphan \"{o}\"" );

			o.Delete();
			UpdateQueue.Add( o );
			HasChanges = true;
		}
	}

	internal static void InitializeCompilerForFilename( IResourceCompilerContext context, string filename )
	{
		var ext = System.IO.Path.GetExtension( filename )[1..].ToLowerInvariant();
		var assetType = AssetType.FromExtension( ext );

		if ( assetType is null )
			return;

		if ( assetType.IsGameResource == false )
			return;

		context.SetCompiler( $"ManagedResourceCompiler" );
		context.SetExtension( ext );
	}

	/// <summary>
	/// Create an empty <see cref="GameResource"/>.
	/// </summary>
	/// <param name="type">Asset type extension for our new <see cref="GameResource"/> instance.</param>
	/// <param name="absoluteFilename">Where to save the new <see cref="GameResource"/> instance. For example from <see cref="FileDialog"/>.</param>
	/// <returns>The new asset, or null if creation failed.</returns>
	public static Asset CreateResource( string type, string absoluteFilename )
	{
		var gameResourceType = EditorTypeLibrary.GetAttributes<AssetTypeAttribute>().Where( x => x.Extension == type ).FirstOrDefault();
		if ( gameResourceType is null )
		{
			Log.Warning( $"Couldn't find matching resource type for extension {type}" );
			return null;
		}

		var extension = gameResourceType.Extension;
		absoluteFilename = System.IO.Path.ChangeExtension( absoluteFilename, extension );

		// try to find it first. If we find it, return it.
		var found = AssetSystem.FindByPath( absoluteFilename );
		if ( found is not null ) return found;

		// create an empty file
		System.IO.File.WriteAllText( absoluteFilename, "{}" );

		// convert it to an .asset
		var asset = RegisterFile( absoluteFilename );
		if ( asset == null )
		{
			log.Warning( $"Something went wrong when registering {absoluteFilename}" );
			return null;
		}

		//
		// Load and save it, to create an empty version of it
		//
		if ( asset.TryLoadGameResource( typeof( GameResource ), out var obj, true ) )
		{
			asset.SaveToDisk( obj );
		}

		return asset;
	}

	internal static void OnSoundReload( string filename )
	{
		if ( SoundFile.Loaded.TryGetValue( filename, out var soundFile ) )
		{
			soundFile.OnReloadInternal();
		}
	}

	internal static void OnSoundReloaded( string filename )
	{
		if ( SoundFile.Loaded.TryGetValue( filename, out var soundFile ) )
		{
			soundFile.OnReloadedInternal();
		}
	}

	internal static void OnDemandRecompile( uint index, string reason )
	{
		// matt: disabling this for now because it's causing re-entrants in CResourceSystem::ForceSynchronizationAndBlockUntilManifestLoaded()
		//       which was making the entire resource system shit the bed
		//       specifically I was seeing this because Qt events run CHammerEditorSession::RenderView but I imagine it can happen
		//       in a shit load of places... layla said they'd got this in-game too but I'm not sure if it's the same thing
		// IToolsDll.Current?.Spin();
	}

	/// <summary>
	/// Passed parameters for the AssetPicker going from engine to addon code
	/// </summary>
	[EditorBrowsable( EditorBrowsableState.Never )]
	public struct AssetPickerParameters
	{
		public Widget ParentWidget { get; init; }
		public List<AssetType> FilterAssetTypes { get; init; }
		public Action<Asset[]> AssetSelectedCallback { get; init; }
		public int ViewMode { get; init; }
		public Asset InitialSelectedAsset { get; init; }
		public string Title { get; init; }
		public bool ShowCloudAssets { get; init; }
		public string InitialSearchText { get; init; }
	}

	internal static void PopulateAssetMenu( Native.QMenu qMenu, IAsset asset )
	{
		var menu = new Menu( qMenu );
		EditorEvent.Run( "asset.nativecontextmenu", menu, AssetSystem.Get( asset ) );
	}

	/// <summary>
	/// Called from native to open our managed AssetPicker
	/// </summary>
	internal static void OpenPicker( Native.QWidget parentWidget, CUtlVectorAssetType assetTypes, IAssetPickerListener listener, int viewmode, IAsset selectedAsset, string titleAndSettingsName, bool cloudAllowed, string initialSearchText )
	{
		List<AssetType> filterAssetTypes = new();

		if ( assetTypes.IsValid )
		{
			for ( int i = 0; i < assetTypes.Count(); i++ )
			{
				filterAssetTypes.Add( AssetType.Find( assetTypes.Element( i ).GetPrimaryExtension() ) );
			}
		}

		Action<Asset[]> assetsPicked = ( Asset[] assets ) =>
		{
			if ( !listener.IsValid ) return;

			var picked = AssetPickedWrapper.Create();
			foreach ( var asset in assets )
			{
				if ( asset is NativeAsset nativeAsset )
				{
					picked.AddAsset( nativeAsset.native );
				}
			}
			listener.NotifyAssetPicked( picked );
			picked.DeleteThis();
		};

		AssetPickerParameters parameters = new()
		{
			ParentWidget = parentWidget.IsValid ? new Widget( parentWidget ) : null,
			InitialSelectedAsset = selectedAsset.IsValid ? AssetSystem.Get( selectedAsset ) : null,
			FilterAssetTypes = filterAssetTypes,
			AssetSelectedCallback = assetsPicked,
			ViewMode = viewmode,
			Title = titleAndSettingsName,
			ShowCloudAssets = true,
			InitialSearchText = initialSearchText
		};

		EditorEvent.Run( "assetsystem.openpicker", parameters );
	}

	static ulong _freeIndex = uint.MaxValue;

	internal static void AddAssetsFromMount( Sandbox.Mounting.BaseGameMount source )
	{
		foreach ( var file in source.Resources )
		{
			var index = _freeIndex++;

			var assetType = AssetType.ResolveFromPath( file.Path );

			if ( assetType == null )
			{
				//Log.Warning( $"assetType was null for {file.Path}" );
				continue;
			}

			var asset = new MountAsset( index, file, source );

			allAssets[asset.AssetId] = asset;
			UpdateQueue.Add( asset );
			HasChanges = true;
		}

		Tick();
	}

	/// <summary>
	/// Callbacks for the asset system. Add this interface to your Widget to get events.
	/// </summary>
	public interface IEventListener
	{
		/// <summary>
		/// An asset has been modified
		/// </summary>
		void OnAssetChanged( Asset asset ) { }

		/// <summary>
		/// The thumbnail for an asset has been updated
		/// </summary>
		void OnAssetThumbGenerated( Asset asset ) { }

		/// <summary>
		/// Changes have been detected in the asset system. We won't tell you what, but
		/// you probably need to update the asset list or something.
		/// </summary>
		void OnAssetSystemChanges() { }

		/// <summary>
		/// Called when a new tag has been added to the asset system.
		/// </summary>
		void OnAssetTagsChanged() { }
	}

	/// <summary>
	/// Create an Asset from a serialized property. This is expected to be an embedded asset property.
	/// </summary>
	public static Asset CreateEmbeddedAsset( SerializedProperty target )
	{
		return new EmbeddedAsset( target );
	}
}

