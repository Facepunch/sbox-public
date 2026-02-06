namespace Editor;

[SkipHotload]
internal class NativeAsset : Asset
{
	internal IAsset native;

	internal NativeAsset( IAsset native )
	{
		this.native = native;
		AssetId = native.GetAssetIndexInt();
	}

	internal override void UpdateInternals( bool compileImmediately = true )
	{
		if ( AssetType.AssetTypeCache.TryGetValue( native.GetAssetTypeId(), out var type ) )
			AssetType = type;

		Assert.NotNull( AssetType ); // agh? Maybe we mock up an unknown type type?

		AssetId = native.GetAssetIndexInt();
		Name = native.GetFriendlyName_Transient().NormalizeFilename( false );
		RelativePath = native.GetRelativePath_Transient( AssetLocation_t.Invalid ).NormalizeFilename( false );
		Path = System.IO.Path.ChangeExtension( RelativePath, AssetType.FileExtension ).NormalizeFilename( false );
		AbsolutePath = native.GetAbsolutePath_Transient( AssetLocation_t.Invalid ).NormalizeFilename( false ); // invalid means get any
		AbsoluteSourcePath = native.GetAbsolutePath_Transient( AssetLocation_t.Content ).NormalizeFilename( false ); // invalid means get any
		AbsoluteCompiledPath = native.GetAbsolutePath_Transient( AssetLocation_t.Game ).NormalizeFilename( false ); // invalid means get any
		IsDeleted = string.IsNullOrEmpty( AbsolutePath );

		if ( AssetSystem.CloudDirectory is not null )
		{
			Package = AssetSystem.CloudDirectory.FindPackage( AbsolutePath, RelativePath );
		}

		// If we need a dependency update at this point, then this has taken a weird path through AssetSystem.AssetChanged
		// And will be resolved on the very next tick
		// The weird path it's taking was calling these below methods which resolving unresolved references before those assets had a chance to register...
		// I don't have a better solution that doesn't involve ripping it all up
		if ( native.NeedAnyDependencyUpdate_Virtual() )
			return;

		IsTrivialChild = native.IsTrivialChildAsset();

		// Reload all tags.
		LoadUserTags();
		UpdateAutoTags();

		if ( compileImmediately && !IsCompiled && AssetType.IsGameResource )
		{
			Compile( false );
		}
	}

	/// <summary>
	/// Can this asset be recompiled?
	/// </summary>
	public override bool CanRecompile => native.CanRecompile();

	internal override void OnRemoved()
	{
		base.OnRemoved();

		native = default;
	}

	/// <summary>
	/// Returns the compiled file path, if the asset is compiled.
	/// </summary>
	/// <param name="absolute">Whether the path should be absolute or relative.</param>F
	/// <returns>The compiled file path, or null if the asset was not compiled.</returns>
	public override string GetCompiledFile( bool absolute = false )
	{
		if ( absolute )
			return native.GetAbsolutePath_Transient( AssetLocation_t.Game ).NormalizeFilename( false );

		return native.GetRelativePath_Transient( AssetLocation_t.Game ).NormalizeFilename( false );
	}

	/// <summary>
	/// Returns the source file path, if the sources are present.
	/// </summary>
	/// <param name="absolute">Whether the path should be absolute or relative.</param>
	/// <returns>The source file path, or null if the source files are not present.</returns>
	public override string GetSourceFile( bool absolute = false )
	{
		if ( absolute )
			return native.GetAbsolutePath_Transient( AssetLocation_t.Content ).NormalizeFilename( false );

		return native.GetRelativePath_Transient( AssetLocation_t.Content ).NormalizeFilename( false );
	}


	internal override int FindIntEditInfo( string name )
	{
		return native.FindIntEditInfo( name );
	}

	public override string FindStringEditInfo( string name )
	{
		return native.FindStringEditInfo( name );
	}


	/// <summary>
	/// Try to open this asset in a supported editor.
	/// You can specify nativeEditor to open in a specific editor.
	/// </summary>
	/// <param name="nativeEditor">A native editor specified in enginetools.txt (e.g modeldoc_editor, hammer, pet..)</param>
	public override void OpenInEditor( string nativeEditor = null )
	{
		if ( AssetType == AssetType.Shader )
		{
			EditorEvent.Run( "open.shader", AbsolutePath );
			return;
		}

		if ( nativeEditor != null )
		{
			native.OpenInSecondaryTool( nativeEditor );
			return;
		}

		if ( IAssetEditor.OpenInEditor( this, out _ ) )
		{
			return;
		}

		native.OpenInTool();
	}

	/// <summary>
	/// Returns assets that this asset references/uses.
	/// </summary>
	/// <param name="deep">Whether to recurse. For example, will also include textures referenced by the materials used by this model asset, as opposed to returning just the materials.</param>
	public override List<Asset> GetReferences( bool deep )
	{
		var list = NativeEngine.CUtlVectorAsset.Create( 4, 4 );
		native.GetAssetsIReference( list, deep );
		return GetAssetList( list, true );
	}

	/// <summary>
	/// Returns assets that depend/use this asset.
	/// </summary>
	/// <param name="deep">Whether to recurse. For example, will also include maps that are using models which use this material asset, as opposed to returning just the models.</param>
	public override List<Asset> GetDependants( bool deep )
	{
		var list = NativeEngine.CUtlVectorAsset.Create( 4, 4 );
		native.GetAssetsReferencingMe( list, deep );
		return GetAssetList( list, true );
	}

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
	public override List<string> GetAdditionalContentFiles()
	{
		var l = new List<string>();

		for ( int i = 0; i < native.AdditionalRelatedFileCount(); i++ )
		{
			// Only include content-side files
			if ( native.GetAdditionalRelatedFileLocation( i ) == AssetLocation_t.Content )
			{
				l.Add( native.GetAdditionalRelatedFile_Transient( i ) );
			}
		}

		return l;
	}

	/// <summary>
	/// Gets additional game-side files to be packaged (e.g. navdata). These are files that are loaded by managed code, not as native resources.
	/// </summary>
	public override List<string> GetAdditionalGameFiles()
	{
		var l = new List<string>();

		for ( int i = 0; i < native.AdditionalRelatedFileCount(); i++ )
		{
			// Only include game-side files (registered via RegisterAdditionalRelatedFile_Game)
			if ( native.GetAdditionalRelatedFileLocation( i ) == AssetLocation_t.Game )
			{
				l.Add( native.GetAdditionalRelatedFileRelativePath_Transient( i ) );
			}
		}

		return l;
	}

	/// <summary>
	/// Gets input dependencies for an asset. This'll be tga's for a texture and stuff like that.
	/// </summary>
	public override List<string> GetInputDependencies()
	{
		var l = new List<string>();

		for ( int i = 0; i < native.AdditionalInputDependencyCount(); i++ )
		{
			l.Add( native.GetAdditionalInputDependency_Transient( i ) );
		}

		return l;
	}

	/// <summary>
	/// Unrecognized reference paths listed by the data that could not be resolved into Asset*s
	/// </summary>
	public override List<string> GetUnrecognizedReferencePaths()
	{
		var list = NativeEngine.CUtlVectorString.Create( 4, 4 );
		native.GetUnrecognizedRelatedPaths( AssetRelatedPathType_t.Reference, list );

		var l = new List<string>();

		for ( int i = 0; i < list.Count(); i++ )
		{
			l.Add( list.Element( i ) );
		}

		list.DeleteThis();

		return l;
	}

	/// <summary>
	/// Forcibly recompile the asset.
	/// </summary>
	/// <param name="full">TODO</param>
	public override bool Compile( bool full )
	{
		ThreadSafe.AssertIsMainThread();

		if ( AssetType == AssetType.Shader )
		{
			EditorEvent.Run( "compile.shader", Path );
			return true;
		}

		return IAssetSystem.RecompileAsset( native, full );
	}

	/// <summary>
	/// Try to create a preview model if we're fbx, obj, etc
	/// </summary>
	public override Model GetPreviewModel()
	{
		return Model.FromNative( IAssetPreviewSystem.GetModelForAsset( native ), true );
	}

	/// <summary>
	/// Tell asset system that this asset was opened. Sticks it on the recent opened list.
	/// </summary>
	public override void RecordOpened()
	{
		IAssetSystem.RecordAssetOpen( native );
	}

	/// <summary>
	/// Whether the asset is compiled.
	/// </summary>
	public override bool IsCompiled => native.IsCompiled();

	/// <summary>
	/// Whether the asset is compiled and all dependencies are up to date. (Slower than IsCompiled)
	/// </summary>
	public override bool IsCompiledAndUpToDate => native.IsCompiledAndUpToDate();

	/// <summary>
	/// Whether the asset failed to compile.
	/// </summary>
	public override bool IsCompileFailed => native.IsCompileFailed();

	/// <summary>
	/// Returns a task that will resolve when the asset is compiled. If the asset is already compiled, do nothing. Does not support maps.
	/// </summary>
	/// <returns>true if the compile was needed, and was successful.</returns>
	public override async ValueTask<bool> CompileIfNeededAsync( float timeout = 30.0f )
	{
		if ( IsCompiled )
			return false;

		// Map assets, shader assets, animation subgraphs do not compile via asset system
		if ( !CanRecompile )
			return false;

		Log.Warning( $"Not Compiled: {this} ({native.GetCompileStateReason_Transient()})" );

		// This is completely synchronous right now
		Compile( true );

		RealTimeSince t = 0;

		while ( !IsCompiled )
		{
			// If we don't run the frame, IsCompiled / HasFileInLocation will never have info and we'll be stuck
			IAssetSystem.RunFrame();

			await Task.Delay( 2 );

			if ( IsCompileFailed )
			{
				Log.Warning( $"Error compiling {this}" );
				return false;
			}

			if ( t.Relative > timeout )
			{
				Log.Warning( $"CompileIfNeededAsync took over {timeout} seconds {this} ({native.GetCompileStateReason_Transient()})" );
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// True if we have a source file, and aren't just a _c file
	/// </summary>
	public override bool HasSourceFile => native.HasSourceFile();

	/// <summary>
	/// True if we have a compiled file, and aren't just a source file
	/// </summary>
	public override bool HasCompiledFile => native.HasCompiledFile();

	/// <summary>
	/// Set data for this asset which will be compiled in memory. This is used to preview
	/// asset changes (like materials) before committing to disk.
	/// </summary>
	public override bool SetInMemoryReplacement( string sourceData )
	{
		return native.SetInMemoryReplacement( sourceData );
	}

	/// <summary>
	/// Reverse the changes of SetInMemoryReplacement
	/// </summary>
	public override void ClearInMemoryReplacement()
	{
		native.DiscardInMemoryReplacement();
	}

	internal override void Uncache()
	{
		native.UncacheAsset();
	}

	internal override async Task<bool> CacheAsync()
	{
		native.CacheAsset( true );

		if ( AssetType.IsGameResource || native.IsCached() )
			return true;

		await Task.Delay( 16 );

		return false;
	}
}
