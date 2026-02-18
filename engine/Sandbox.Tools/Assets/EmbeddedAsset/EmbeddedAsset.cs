
using System;

namespace Editor;

[SkipHotload]
internal class EmbeddedAsset : Asset
{
	internal SerializedProperty property;

	internal EmbeddedAsset( SerializedProperty property )
	{
		this.property = property;
		AssetType = AssetType.FromType( property.PropertyType );
		Name = property.FindPathInScene()?.ToString();
	}

	internal override void UpdateInternals( bool compileImmediately = true )
	{

	}

	public override bool CanRecompile => false;
	internal override void OnRemoved() { }
	public override string GetCompiledFile( bool absolute = false ) => null;
	public override string GetSourceFile( bool absolute = false ) => null;
	internal override int FindIntEditInfo( string name ) => 0;
	public override string FindStringEditInfo( string name ) => null;


	/// <summary>
	/// Try to open this asset in a supported editor.
	/// You can specify nativeEditor to open in a specific editor.
	/// </summary>
	/// <param name="nativeEditor">A native editor specified in enginetools.txt (e.g modeldoc_editor, hammer, pet..)</param>
	public override void OpenInEditor( string nativeEditor = null )
	{
		if ( IAssetEditor.OpenInEditor( this, out _ ) )
		{
			return;
		}
	}

	public override bool Compile( bool full )
	{
		return false;
	}

	public override bool IsCompiled => true;
	public override bool IsCompiledAndUpToDate => true;
	public override bool IsCompileFailed => false;

	public override ValueTask<bool> CompileIfNeededAsync( float timeout = 30.0f )
	{
		return ValueTask.FromResult( true );
	}

	public override List<Asset> GetReferences( bool deep )
	{
		return [];
	}

	public override List<Asset> GetDependants( bool deep )
	{
		return [];
	}

	public override List<string> GetAdditionalContentFiles()
	{
		return [];
	}

	public override List<string> GetAdditionalGameFiles()
	{
		return [];
	}

	public override List<string> GetInputDependencies()
	{
		return [];
	}

	public override List<string> GetUnrecognizedReferencePaths()
	{
		return [];
	}

	public override Model GetPreviewModel()
	{
		return Model.Error;
	}

	public override void RecordOpened()
	{

	}

	public override bool SetInMemoryReplacement( string sourceData )
	{
		return false;
	}

	public override void ClearInMemoryReplacement()
	{
	}

	public override bool HasSourceFile => false;
	public override bool HasCompiledFile => false;

	internal override bool TryLoadGameResource( Type t, out GameResource obj, bool allowCreate = false )
	{
		obj = null;

		if ( !Game.Resources.TryGetType( AssetType.FileExtension, out var attribute ) )
			return false;

		if ( !attribute.TargetType.IsAssignableTo( t ) || attribute.TargetType.IsAbstract )
			return false;

		var resource = property.GetValue<Resource>( null );
		if ( resource is null ) return false;
		if ( !resource.GetType().IsAssignableTo( t ) ) return false;

		if ( resource is GameResource gr )
		{
			obj = gr;
			return true;
		}

		return false;
	}

	public override bool SaveToDisk( GameResource obj )
	{
		property.SetValue( obj );
		return true;
	}

}
