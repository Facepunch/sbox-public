
[AssetType( Name = "Scripted Emitter", Extension = "semit", Category = "Sandbox", Flags = AssetTypeFlags.NoEmbedding | AssetTypeFlags.IncludeThumbnails )]
public class ScriptedEmitter : GameResource, IDefinitionResource
{
	/// <summary>
	/// The prefab containing the particle/VFX system to emit.
	/// </summary>
	[Property]
	public PrefabFile Prefab { get; set; }

	[Property]
	public string Title { get; set; }

	[Property]
	public string Description { get; set; }

	public override Bitmap RenderThumbnail( ThumbnailOptions options ) => Prefab?.RenderThumbnail( options );

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "💨", width, height, "#42b4f5" );
	}

	public override void ConfigurePublishing( ResourcePublishContext context )
	{
		if ( Prefab is null )
		{
			context.SetPublishingDisabled( "Invalid: missing a prefab" );
			return;
		}

		var scene = Prefab.GetScene();

		if ( scene.GetAllComponents<ModelRenderer>().Any() )
		{
			context.SetPublishingDisabled( "Invalid: emitter prefab must not contain a ModelRenderer" );
			return;
		}

		if ( scene.GetAllComponents<BaseSandboxWeapon>().Any() )
		{
			context.SetPublishingDisabled( "Invalid: emitter prefab must not contain a BaseSandboxWeapon" );
			return;
		}
	}
}
