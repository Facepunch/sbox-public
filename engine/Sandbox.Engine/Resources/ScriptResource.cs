namespace Sandbox;

/// <summary>
/// Loads the source code of a plain-text script.
/// Resource scans publish script declarations together before executing their startup statements.
/// Loading this resource directly only reads its source; use <see cref="ScriptSystem.LoadFiles(IReadOnlyDictionary{string, string})"/> for explicit named-file updates.
/// </summary>
[Expose]
[AssetType( Name = "Script", Extension = "csx", Category = "Code", Flags = AssetTypeFlags.Uncompiled | AssetTypeFlags.NoEmbedding )]
public sealed class ScriptResource : Resource
{
	/// <summary>
	/// The script's source code, refreshed whenever the resource is reloaded.
	/// </summary>
	public string Source { get; private set; }

	/// <summary>
	/// Whether source has been loaded. Script syntax and execution are checked by ScriptSystem.
	/// </summary>
	public override bool IsValid => Source is not null;

	/// <inheritdoc />
	protected internal override void OnLoaded( ResourceLoadContext context )
	{
		Source = context.ReadSourceAsString();
	}

	/// <inheritdoc />
	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "code", width, height );
	}
}
