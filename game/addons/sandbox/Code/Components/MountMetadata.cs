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
			mr.Enabled = false;
		}
	}

	protected override void OnUpdate()
	{
		if ( !_isFallback ) return;

		var bounds = new BBox( BoundsCenter - BoundsSize / 2f, BoundsCenter + BoundsSize / 2f );

		var t = WorldTransform;
		t = new Transform( t.PointToWorld( bounds.Center ), t.Rotation, t.Scale * (bounds.Size / 50f) );

		Game.ActiveScene.DebugOverlay.Model( Model.Cube, transform: t, overlay: false, materialOveride: _material );

		if ( !string.IsNullOrEmpty( GameTitle ) )
		{
			var textPos = WorldPosition + Vector3.Up * (BoundsSize.z / 2f + 8f);
			DebugOverlay.Text( textPos, Game.Language.GetPhrase( "entity.mount.install", new() { { "name", GameTitle } } ), color: Color.White, duration: 0f );
		}
	}
}
