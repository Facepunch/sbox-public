using Sandbox.Mounting;

namespace Editor;

[SkipHotload]
internal class MountAsset : Asset
{
	private Sandbox.Mounting.ResourceLoader file;
	private BaseGameMount source;

	public MountAsset( ulong assetId, Sandbox.Mounting.ResourceLoader file, BaseGameMount source )
	{
		AssetId = assetId;
		this.file = file;
		this.source = source;
	}

	internal override void UpdateInternals( bool compileImmediately = true )
	{
		AssetType = AssetType.ResolveFromPath( file.Path );

		Name = System.IO.Path.GetFileNameWithoutExtension( file.Path );
		RelativePath = file.Path;
		Path = file.Path;
		AbsolutePath = file.Path;
		AbsoluteSourcePath = null;
		AbsoluteCompiledPath = file.Path;
		IsDeleted = false;
		Package = null;
		IsTrivialChild = false;

		// Reload all tags.
		LoadUserTags();
		UpdateAutoTags();
	}


	public override bool CanRecompile => false;
	public override string GetCompiledFile( bool absolute = false ) => Path;
	public override string GetSourceFile( bool absolute = false ) => null;

	internal override int FindIntEditInfo( string name ) => 0;
	public override string FindStringEditInfo( string name ) => null;

	public override void OpenInEditor( string nativeEditor = null ) { } // I don't think we can?

	public override List<Asset> GetReferences( bool deep ) => new List<Asset>();
	public override List<Asset> GetDependants( bool deep ) => new List<Asset>();
	public override List<string> GetAdditionalContentFiles() => new List<string>();
	public override List<string> GetAdditionalGameFiles() => new List<string>();
	public override List<string> GetInputDependencies() => new List<string>();
	public override List<string> GetUnrecognizedReferencePaths() => new List<string>();

	public override bool Compile( bool full ) { return false; }
	public override Model GetPreviewModel() { return Model.Sphere; } // TODO
	public override void RecordOpened()
	{
		// TODO, record this in c#
	}

	public override bool IsCompiled => true;
	public override bool IsCompiledAndUpToDate => true;
	public override bool IsCompileFailed => false;

	public override ValueTask<bool> CompileIfNeededAsync( float timeout = 30.0f ) => ValueTask.FromResult( false );
	public override bool HasSourceFile => false;
	public override bool HasCompiledFile => true;

	/// <summary>
	/// Everything in Mount assets is procedural
	/// </summary>
	public override bool IsProcedural => true;

	/// <summary>
	/// Set data for this asset which will be compiled in memory. This is used to preview
	/// asset changes (like materials) before committing to disk.
	/// </summary>
	public override bool SetInMemoryReplacement( string sourceData )
	{
		return false;
	}

	/// <summary>
	/// Reverse the changes of SetInMemoryReplacement
	/// </summary>
	public override void ClearInMemoryReplacement()
	{

	}

	internal override async Task<bool> CacheAsync()
	{
		var r = await file.GetOrCreate();
		return r is not null;
	}

}
