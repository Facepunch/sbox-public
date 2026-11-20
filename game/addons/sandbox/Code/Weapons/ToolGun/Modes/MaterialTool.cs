using Sandbox.UI;

[Icon( "🧱" )]
[Title( "#tool.name.material" )]
[ClassName( "material" )]
[Group( "#tool.group.render" )]
public sealed class MaterialTool : ToolMode
{
	/// <summary>
	/// The material to apply, a local path or a workshop package ident.
	/// </summary>
	[Property, ResourceSelect( Extension = "material", AllowPackages = true, Popup = true ), Title( "#tool.setting.material" )]
	public string MaterialPath { get; set; } = "hydraulics/metal_tile_01.vmat";

	public override string Description => "#tool.hint.material.description";

	protected override void OnStart()
	{
		base.OnStart();

		RegisterAction( ToolInput.Primary, () => "#tool.hint.material.apply", OnApply );
		RegisterAction( ToolInput.Secondary, () => "#tool.hint.material.copy", OnCopy );
		RegisterAction( ToolInput.Reload, () => "#tool.hint.material.reset", OnReset );
	}

	SelectionPoint SelectTarget()
	{
		var select = TraceSelect();
		IsValidState = select.IsValid() && !select.IsWorld && !select.IsPlayer;
		return select;
	}

	void OnApply()
	{
		var select = SelectTarget();
		if ( !IsValidState || string.IsNullOrEmpty( MaterialPath ) ) return;

		SetMaterial( select.GameObject, MaterialPath, ToolInput.Primary );
		ShootEffects( select );
	}

	void OnCopy()
	{
		var select = SelectTarget();
		if ( !IsValidState ) return;

		// overrides are replicated, so the owner can read them without asking the host
		var renderer = select.GameObject.GetComponent<ModelRenderer>() ?? select.GameObject.GetComponentInParent<ModelRenderer>();
		var material = GetMaterial( renderer );
		if ( material is null ) return;

		// a workshop material's path only resolves while its package is mounted, keep the ident so it can be fetched again later
		MaterialPath = Cloud.ResolvePrimaryAsset( material.ResourcePath )?.FullIdent ?? material.ResourcePath;
		ShootEffects( select );
	}

	void OnReset()
	{
		var select = SelectTarget();
		if ( !IsValidState ) return;

		SetMaterial( select.GameObject, null, ToolInput.Reload );
		ShootEffects( select );
	}

	static Material GetMaterial( ModelRenderer renderer )
	{
		if ( !renderer.IsValid() ) return null;
		if ( renderer.MaterialOverride is not null ) return renderer.MaterialOverride;
		if ( renderer.Materials.Count == 0 ) return null;

		return renderer.Materials.GetOverride( 0 ) ?? renderer.Materials.GetOriginal( 0 );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	public async void SetMaterial( GameObject target, string materialPath, ToolInput input )
	{
		if ( !FireToolAction( input ) ) return;

		if ( !target.IsValid() ) return;

		var root = target.Network?.RootGameObject ?? target;
		if ( root.IsProxy || root.Tags.Has( "world" ) || root.Tags.Has( "player" ) ) return;

		Material material = null;

		if ( !string.IsNullOrEmpty( materialPath ) )
		{
			material = Material.Load( materialPath );
			material ??= await Cloud.Load<Material>( materialPath );
			if ( material is null ) return;
		}

		if ( !root.IsValid() ) return;

		foreach ( var renderer in root.GetComponentsInChildren<ModelRenderer>() )
		{
			renderer.MaterialOverride = material;
			renderer.GameObject.Network?.Refresh();
		}

		FirePostToolAction( input );
	}
}
