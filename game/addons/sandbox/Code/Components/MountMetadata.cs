/// <summary>
/// Attached to mount-spawned props. This tries to show a graceful bounding box and hints the player what game it's from if they don't have the mount installed.
/// </summary>
[Title( "Mount Metadata" )]
[Category( "Rendering" )]
public sealed class MountMetadata : Component
{
	[Property, Sync( SyncFlags.FromHost )] public string GameTitle { get; set; }
	[Property, Sync( SyncFlags.FromHost )] public Vector3 BoundsSize { get; set; }
	[Property, Sync( SyncFlags.FromHost )] public Vector3 BoundsCenter { get; set; }

	bool _isFallback;

	static Material _material;

	protected override async void OnStart()
	{
		UpdateVisual();
	}

	protected override void OnRefresh()
	{
		base.OnRefresh();
		UpdateVisual();
	}

	void UpdateVisual()
	{
		_material ??= Material.Load( "materials/effects/mount_fallback.vmat" );

		var prop = GameObject.GetComponentInChildren<Prop>();
		var modelMissing = !prop.IsValid() || !prop.Model.IsValid() || prop.Model.IsError;
		if ( !modelMissing ) return;

		// Client doesn't have the mount — hide the error model and show fallback overlay
		_isFallback = true;
		foreach ( var mr in GameObject.GetComponentsInChildren<ModelRenderer>() )
		{
			if ( mr != _box ) mr.Enabled = false;
		}
	}

	// The box and label live on a local child, drawing them through the debug overlay made new scene objects every frame
	GameObject _fallbackRoot;
	ModelRenderer _box;
	TextRenderer _label;
	string _labelTitle;

	protected override void OnDisabled()
	{
		_fallbackRoot?.Destroy();
		_fallbackRoot = null;
		_box = null;
		_label = null;
	}

	protected override void OnUpdate()
	{
		if ( !_isFallback ) return;

		EnsureFallbackRenderers();

		var bounds = new BBox( BoundsCenter - BoundsSize / 2f, BoundsCenter + BoundsSize / 2f );

		var t = WorldTransform;
		_box.WorldTransform = new Transform( t.PointToWorld( bounds.Center ), t.Rotation, t.Scale * (bounds.Size / 50f) );

		var hasTitle = !string.IsNullOrEmpty( GameTitle );
		_label.Enabled = hasTitle;
		if ( !hasTitle ) return;

		if ( _labelTitle != GameTitle )
		{
			_labelTitle = GameTitle;
			_label.TextScope = new TextRendering.Scope( Game.Language.GetPhrase( "entity.mount.install", new() { { "name", GameTitle } } ), Color.White, 32 )
			{
				Shadow = new TextRendering.Shadow { Color = Color.Black, Enabled = true, Offset = 2, Size = 4 }
			};
		}

		// Keep the label a fixed size: it is a child of the prop, so a scaled prop would scale the text too.
		// Anchor it above the world-space bounds so it stays on top when the prop tips over.
		var worldBounds = bounds.Transform( t );
		_label.WorldTransform = new Transform( worldBounds.Center.WithZ( worldBounds.Maxs.z ) + Vector3.Up * 8f, t.Rotation, 1f );
	}

	void EnsureFallbackRenderers()
	{
		if ( _box.IsValid() && _label.IsValid() ) return;

		// One went missing, start over rather than leave a second fallback beside the first
		_fallbackRoot?.Destroy();

		var go = _fallbackRoot = new GameObject( GameObject, true, "Mount Fallback" );
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.Hidden;

		_box = new GameObject( go, true, "Box" ).AddComponent<ModelRenderer>();
		_box.Model = Model.Cube;
		_box.MaterialOverride = _material;
		_box.RenderType = ModelRenderer.ShadowRenderType.Off;

		_label = new GameObject( go, true, "Label" ).AddComponent<TextRenderer>();
		_label.Billboard = TextRenderer.BillboardMode.Always;
		_labelTitle = null;
	}
}
