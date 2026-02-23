using NativeEngine;
using System;

namespace Editor;

[SkipHotload]
public abstract partial class Asset
{
	internal Pixmap thumbnailOverride;

	internal Asset()
	{
		Tags = new AssetTags( this );
	}

	public override string ToString() => Path;

	internal abstract void UpdateInternals( bool compileImmediately = true );

	/// <summary>
	/// Tags for this asset, for filtering purposes in the Asset Browser.
	/// </summary>
	public AssetTags Tags { get; private set; }

	internal ulong AssetId { get; set; }

	/// <summary>
	/// Name of the asset, usually the filename.
	/// </summary>
	public string Name { get; protected set; }

	/// <summary>
	/// The relative path with the asset extension. ie .wav becomes .vsnd
	/// </summary>
	public string Path { get; protected set; }

	/// <summary>
	/// The relative path as it is on disk (ie .wav not .vsnd)
	/// </summary>
	public string RelativePath { get; protected set; }

	/// <summary>
	/// The absolute path as it is on disk (ie .wav not .vsnd)
	/// </summary>
	public string AbsolutePath { get; protected set; }

	/// <summary>
	/// The absolute path as it is on disk. Should be null if we don't have source.
	/// </summary>
	internal string AbsoluteSourcePath { get; set; }

	/// <summary>
	/// The absolute path as it is on disk. Should be null if we don't have a compiled.
	/// </summary>
	internal string AbsoluteCompiledPath { get; set; }

	/// <summary>
	/// When the asset was last opened through the editor.
	/// </summary>
	public DateTime? LastOpened { get; internal set; }

	/// <summary>
	/// The type of this asset.
	/// </summary>
	public AssetType AssetType { get; protected set; }

	/// <summary>
	/// If this asset was downloaded from sbox.game then this will
	/// be the package from which this asset was downloaded. If not then
	/// it'll be null.
	/// </summary>
	public Package Package { get; internal set; }

	/// <summary>
	/// Whether the asset is deleted or not.
	/// This can happen after <see cref="Delete"/> was called on it, or <see cref="AbsolutePath"/> is empty.
	/// </summary>
	public bool IsDeleted { get; internal set; }

	/// <summary>
	/// If true then this asset is generated at runtime somehow. Possibly from a mount system.
	/// </summary>
	public virtual bool IsProcedural => false;

	/// <summary>
	/// This asset is generated in the transient folder. You don't need to see it, or keep it around. It will re-generate from something else.
	/// </summary>
	public virtual bool IsTransient => AbsolutePath.Contains( "/.sbox/transient/" );

	/// <summary>
	/// This asset is from the cloud, it's in the cloud folder
	/// </summary>
	public virtual bool IsCloud => AbsolutePath.Contains( "/.sbox/cloud/" );

	/// <summary>
	/// The asset was generated from another asset compile and has no source asset of its own. For example model break gibs .vmdl, .vtex files for materials, etc.
	/// </summary>
	public bool IsTrivialChild { get; protected set; }

	/// <summary>
	/// Can this asset be recompiled?
	/// </summary>
	public abstract bool CanRecompile { get; }

	internal virtual void OnRemoved()
	{
		CachedThumbnail = default;
	}

	/// <summary>
	/// Delete this asset. Will send the source and compiled files to the recycle bin.
	/// </summary>
	public void Delete()
	{
		var compiled = GetCompiledFile( true );
		var source = GetSourceFile( true );

		if ( !string.IsNullOrWhiteSpace( compiled ) )
			EditorUtility.SendToRecycleBin( compiled );

		if ( !string.IsNullOrWhiteSpace( source ) )
			EditorUtility.SendToRecycleBin( source );

		IsDeleted = true;
	}

	/// <summary>
	/// Returns the compiled file path, if the asset is compiled.
	/// </summary>
	/// <param name="absolute">Whether the path should be absolute or relative.</param>F
	/// <returns>The compiled file path, or null if the asset was not compiled.</returns>
	public abstract string GetCompiledFile( bool absolute = false );

	/// <summary>
	/// Returns the source file path, if the sources are present.
	/// </summary>
	/// <param name="absolute">Whether the path should be absolute or relative.</param>
	/// <returns>The source file path, or null if the source files are not present.</returns>
	public abstract string GetSourceFile( bool absolute = false );

	internal Pixmap CachedThumbnail;
	public bool HasCachedThumbnail => CachedThumbnail is not null && CachedThumbnail != AssetType?.Icon256;

	/// <summary>
	/// Returns the asset preview thumbnail, with fallback to the asset type icon if there is no preview.
	/// </summary>
	public Pixmap GetAssetThumb( bool generateIfNotInCache = true )
	{
		return AssetThumbnail.GetAssetThumb( this, generateIfNotInCache );
	}

	public void CancelThumbBuild()
	{
		AssetThumbnail.DequeueThumbBuild( this );
	}

	internal abstract int FindIntEditInfo( string name );

	public abstract string FindStringEditInfo( string name );

	/// <summary>
	/// Delete existing cached thumbnail, optionally queuing for building a new one ASAP.
	/// </summary>
	/// <param name="startBuild">Queue building the new thumbnail ASAP, as opposed to waiting when it is actually needed and doing it then.</param>
	public void RebuildThumbnail( bool startBuild = true )
	{
		var fullPath = AssetThumbnail.GetThumbnailFile( this, false );

		try
		{
			if ( System.IO.File.Exists( fullPath ) )
				System.IO.File.Delete( fullPath );
		}
		catch ( System.Exception )
		{
			return;
		}

		CachedThumbnail = thumbnailOverride ?? AssetType.Icon256;

		if ( startBuild )
			AssetThumbnail.QueueThumbBuild( this );
		else
			CachedThumbnail = null;
	}

	/// <summary>
	/// Try to open this asset in a supported editor.
	/// You can specify nativeEditor to open in a specific editor.
	/// </summary>
	/// <param name="nativeEditor">A native editor specified in enginetools.txt (e.g modeldoc_editor, hammer, pet..)</param>
	public abstract void OpenInEditor( string nativeEditor = null );

	/// <summary>
	/// Returns assets that this asset references/uses.
	/// </summary>
	/// <param name="deep">Whether to recurse. For example, will also include textures referenced by the materials used by this model asset, as opposed to returning just the materials.</param>
	public abstract List<Asset> GetReferences( bool deep );

	/// <summary>
	/// Returns assets that depend/use this asset.
	/// </summary>
	/// <param name="deep">Whether to recurse. For example, will also include maps that are using models which use this material asset, as opposed to returning just the models.</param>
	public abstract List<Asset> GetDependants( bool deep );

	List<Asset> GetAssetList( NativeEngine.CUtlVectorAsset v, bool free )
	{
		var l = new List<Asset>();

		for ( int i = 0; i < v.Count(); i++ )
		{
			l.Add( AssetSystem.Get( v.Element( i ) ) );
		}

		if ( free )
			v.DeleteThis();

		return l;
	}

	/// <summary>
	/// Gets additional content-side related files. This includes like .rect files for materials, all .fbx and .lxo files for models, etc.
	/// </summary>
	public abstract List<string> GetAdditionalContentFiles();

	/// <summary>
	/// Gets additional game-side files to be packaged (e.g. navdata). These are files that are loaded by managed code, not as native resources.
	/// </summary>
	public abstract List<string> GetAdditionalGameFiles();

	/// <summary>
	/// Gets input dependencies for an asset. This'll be tga's for a texture and stuff like that.
	/// </summary>
	public abstract List<string> GetInputDependencies();

	/// <summary>
	/// Unrecognized reference paths listed by the data that could not be resolved into Asset*s
	/// </summary>
	public abstract List<string> GetUnrecognizedReferencePaths();

	/// <summary>
	/// Forcibly recompile the asset.
	/// </summary>
	/// <param name="full">TODO</param>
	public abstract bool Compile( bool full );

	MetaData _metadata;

	/// <summary>
	/// Asset type specific key-value based data storage.
	/// </summary>
	public MetaData MetaData
	{
		get
		{
			if ( _metadata != null ) return _metadata;

			var f = GetSourceFile( true );
			if ( string.IsNullOrEmpty( f ) ) f = GetCompiledFile( true );
			if ( string.IsNullOrEmpty( f ) ) return null;

			// tony: Don't make metadata for cloud assets.. not my favourite addition ever
			// we're mounting all the downloaded cloud assets first, and checking their tags, which is making fake metadata
			// using the wrong paths
			if ( f.Contains( "/.sbox/cloud/" ) ) return null;

			// modelname.vmdl_c -> modelname.vmdl
			if ( f.EndsWith( "_c" ) )
				f = f.Substring( 0, f.Length - 2 );

			// modelname.vmdl -> modelname.vmdl.meta
			f += ".meta";

			_metadata = new MetaData( f );
			return _metadata;
		}
	}

	/// <summary>
	/// Renders the thumbnail and then saves it to disk.
	/// </summary>
	public async Task DumpThumbnail()
	{
		var thumb = await RenderThumb();
		var thumbPath = $"{AbsolutePath}.png";

		// Thumbnail might not be valid (e.g. not a supported asset type)
		if ( thumb == null )
			return;

		thumb.SavePng( thumbPath );
	}

	/// <summary>
	/// Immediately render a preview thumbnail for this asset, and return it.
	/// </summary>
	/// <returns>The rendered preview thumbnail, or null if asset type does not support previews.</returns>
	public Task<Pixmap> RenderThumb()
	{
		return AssetThumbnail.RenderAssetThumb( this );
	}

	/// <summary>
	/// Try to create a preview model if we're fbx, obj, etc
	/// </summary>
	public abstract Model GetPreviewModel();

	/// <summary>
	/// Try to load this asset as an automatically determined resource type.
	/// If this isn't a resource type (like an Image) then it will return null.
	/// </summary>
	public Resource LoadResource()
	{
		if ( AssetType.ResourceType is not { } type )
		{
			return null;
		}

		return LoadResource( type );
	}

	/// <summary>
	/// Try to load this asset as a <see cref="Resource"/> of given type.
	/// </summary>
	/// <typeparam name="T">The type of resource to try to load.</typeparam>
	/// <returns>The loaded <see cref="Resource"/> instance of given type, or null on failure.</returns>
	public T LoadResource<T>() where T : Resource
	{
		return LoadResource( typeof( T ) ) as T;
	}

	/// <summary>
	/// Try to load this asset as a <see cref="Resource"/> of given type.
	/// </summary>
	/// <returns>The loaded <see cref="Resource"/> instance of given type, or null on failure.</returns>
	public Resource LoadResource( Type resourceType )
	{
		if ( resourceType.IsAssignableTo( typeof( GameResource ) ) )
		{
			if ( TryLoadGameResource( resourceType, out var o ) )
				return o;

			return null;
		}

		if ( resourceType.IsAssignableTo( typeof( Texture ) ) )
		{
			// Use the relative path here so we can load png/jpg/ect not *just* vtex
			return Texture.Load( RelativePath );
		}

		return Resource.Load( resourceType, Path );
	}

	/// <summary>
	/// Try to load this asset as a <see cref="Resource"/> of given type.
	/// </summary>
	/// <typeparam name="T">The type of resource to try to load.</typeparam>
	/// <param name="obj">Output resource on success, null on failure.</param>
	/// <returns>true if <paramref name="obj"/> was successfully set.</returns>
	public bool TryLoadResource<T>( out T obj ) where T : Resource
	{
		obj = LoadResource<T>();
		return obj != null;
	}

	private bool TryLoadGameResource<T>( out T obj ) where T : GameResource
	{
		obj = null;

		if ( TryLoadGameResource( typeof( T ), out GameResource resource ) == false )
		{
			return false;
		}

		obj = (T)resource;
		return obj is not null;
	}

	internal virtual bool TryLoadGameResource( Type t, out GameResource obj, bool allowCreate = false )
	{
		obj = null;

		if ( !Game.Resources.TryGetType( AssetType.FileExtension, out var attribute ) )
			return false;

		if ( !attribute.TargetType.IsAssignableTo( t ) || attribute.TargetType.IsAbstract )
			return false;

		// Make sure we have an up to date compiled version
		if ( CanRecompile )
		{
			Compile( false );
		}

		obj = GameResource.GetPromise( attribute.TargetType, Path );
		if ( obj != null && !obj.IsPromise )
			return true; // already exists and loaded

		// get compiled path
		var compiledFilePath = GetCompiledFile( true );

		if ( string.IsNullOrEmpty( compiledFilePath ) )
		{
			if ( allowCreate ) return true;

			Log.Warning( $"Tried to load {this} but couldn't get compiled file" );
			return false;
		}

		if ( !System.IO.File.Exists( compiledFilePath ) )
		{
			Log.Warning( $"Tried to load {this} but compiled file doesn't exist ({compiledFilePath})" );
			return false;
		}

		var data = System.IO.File.ReadAllBytes( compiledFilePath );
		if ( data == null )
		{
			Log.Warning( $"Tried to load {this} but couldn't read file" );
			return false;
		}

		if ( !obj.TryLoadFromData( data ) )
		{
			Log.Warning( $"Tried to load {this} but couldn't load from data" );
			return false;
		}

		return true;
	}

	/// <summary>
	/// Try to get the raw Json string, for a managed asset type (a GameResource)
	/// </summary>
	public unsafe string ReadJson()
	{
		// Don't bother
		if ( !AssetType.IsGameResource )
			return null;

		try
		{
			var filename = GetSourceFile( true );

			// If we only have the compiled, load from that
			// this isn't the ideal, but right now this lets us show info
			// in the editor - even if they can't edit it.
			if ( string.IsNullOrWhiteSpace( filename ) )
			{
				filename = GetCompiledFile( true );
				var data = System.IO.File.ReadAllBytes( filename );

				fixed ( byte* ptr = data )
				{
					return EngineGlue.ReadCompiledResourceFileJson( (IntPtr)ptr );
				}
			}
			else
			{
				var txt = System.IO.File.ReadAllText( filename );

				// This isn't json, it's the the keyvalues system. Lets update
				// it by converting to json and then returning the new json value
				// We should be able to get rid of this code in a few months when
				// there are no more keyvalue assets.
				if ( txt.First() == '<' )
				{
					var kv = EngineGlue.LoadKeyValues3( txt );
					if ( kv.IsNull ) return null;

					var json = EngineGlue.KeyValues3ToJson( kv.FindOrCreateMember( "data" ) );
					kv.DeleteThis();

					// Upgrade with json
					if ( json is not null )
					{
						System.IO.File.WriteAllText( filename, json );
					}

					return json;
				}

				return txt;
			}
		}
		catch ( System.IO.DirectoryNotFoundException )
		{
			return null;
		}
		catch ( System.IO.FileNotFoundException )
		{
			return null;
		}
	}

	/// <summary>
	/// Save a game resource instance to disk. This is used internally by asset inspector and for asset creation.
	/// </summary>
	/// <param name="obj">The instance data to save.</param>
	/// <returns>Whether the instance was successfully saved or not.</returns>
	public virtual bool SaveToDisk( GameResource obj )
	{
		if ( obj == null )
			return false;

		var filename = GetSourceFile( true );

		if ( string.IsNullOrWhiteSpace( filename ) )
			return false;

		var attribute = EditorTypeLibrary.GetAttributes<AssetTypeAttribute>().Where( x => x.Extension == AssetType.FileExtension ).FirstOrDefault();

		if ( attribute == null || !obj.GetType().IsAssignableTo( attribute.TargetType ) )
			return false;

		RecordOpened();

		var json = obj.Serialize();
		var jsonString = json.ToJsonString( Json.options );

		for ( int retry = 0; retry < 10; retry++ )
		{
			try
			{
				obj.SaveToDisk( filename, jsonString );

				Compile( false );
				obj.Register( Path );
				return true;
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Problem writing {filename} - retrying" );

				if ( retry == 9 )
				{
					Log.Warning( "Aborting - couldn't write." );
				}

				System.Threading.Thread.Sleep( 200 );
			}
		}

		return false;
	}

	/// <summary>
	/// Tell asset system that this asset was opened. Sticks it on the recent opened list.
	/// </summary>
	public abstract void RecordOpened();

	/// <summary>
	/// Whether the asset is compiled.
	/// </summary>
	public abstract bool IsCompiled { get; }

	/// <summary>
	/// Whether the asset is compiled and all dependencies are up to date. (Slower than IsCompiled)
	/// </summary>
	public abstract bool IsCompiledAndUpToDate { get; }

	/// <summary>
	/// Whether the asset failed to compile.
	/// </summary>
	public abstract bool IsCompileFailed { get; }

	/// <summary>
	/// Returns a task that will resolve when the asset is compiled. If the asset is already compiled, do nothing. Does not support maps.
	/// </summary>
	/// <returns>true if the compile was needed, and was successful.</returns>
	public abstract ValueTask<bool> CompileIfNeededAsync( float timeout = 30.0f );

	/// <summary>
	/// Override the Assets thumbnail with given one.
	/// </summary>
	public void OverrideThumbnail( Pixmap pixmap )
	{
		thumbnailOverride = pixmap;
		RebuildThumbnail();
	}

	/// <summary>
	/// True if we have a source file, and aren't just a _c file
	/// </summary>
	public abstract bool HasSourceFile { get; }

	/// <summary>
	/// True if we have a compiled file, and aren't just a source file
	/// </summary>
	public abstract bool HasCompiledFile { get; }

	/// <summary>
	/// A free-use variable for the editor to use to portray that this asset
	/// somehow has changes that need to be saved to disk.
	/// </summary>
	public bool HasUnsavedChanges { get; set; }

	/// <summary>
	/// Set data for this asset which will be compiled in memory. This is used to preview
	/// asset changes (like materials) before committing to disk.
	/// </summary>
	public abstract bool SetInMemoryReplacement( string sourceData );

	/// <summary>
	/// Reverse the changes of SetInMemoryReplacement
	/// </summary>
	public abstract void ClearInMemoryReplacement();

	internal virtual void Uncache() { }
	internal virtual Task<bool> CacheAsync() { return Task.FromResult( false ); }
}
