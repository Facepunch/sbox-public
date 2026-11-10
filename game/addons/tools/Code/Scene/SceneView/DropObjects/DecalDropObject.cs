using System.Threading;

namespace Editor;

[DropObject( "decal", "decal", "decal_c" )]
partial class DecalDropObject : BaseDropObject
{
	private IDisposable undoScope;

	protected override async Task Initialize( string dragData, CancellationToken token )
	{
		Asset asset = await InstallAsset( dragData, token );

		if ( asset is null )
			return;

		if ( token.IsCancellationRequested )
			return;

		var d = asset.LoadResource<DecalDefinition>();
		if ( d is null ) return;

		// We should REALLY be scoped over whatever scene we're dragging over
		using ( SceneEditorSession.Scope() )
		{
			undoScope = SceneEditorSession.Active.UndoScope( "Drop Prefab" ).WithGameObjectCreations().Push();

			GameObject = new GameObject( false );
			GameObject.Name = d.ResourceName;
			GameObject.Flags = GameObjectFlags.NotSaved | GameObjectFlags.Hidden;
			GameObject.Tags.Add( "isdragdrop" );

			var decal = GameObject.AddComponent<Decal>();
			decal.Decals = [d];
			decal.Rotation = 0;

			GameObject.Enabled = true;

			PivotPosition = Vector3.Zero;
			Rotation = new Angles( 90, 0, 0 );
			Scale = GameObject.WorldScale;
		}
	}

	public override void OnUpdate()
	{
		if ( GameObject.IsValid() )
		{
			GameObject.WorldTransform = traceTransform;
		}

		using var scope = Gizmo.Scope( "DropObject", traceTransform );

		Gizmo.Draw.Color = Color.White.WithAlpha( 0.3f );
		Gizmo.Draw.LineBBox( Bounds );

		Gizmo.Draw.Color = Color.White;

		if ( !string.IsNullOrWhiteSpace( PackageStatus ) )
		{
			Gizmo.Draw.Text( PackageStatus, new Transform( Bounds.Center ), "Inter", 12 );
		}
	}

	public override async Task OnDrop()
	{
		await WaitForLoad();

		if ( !GameObject.IsValid() )
			return;

		GameObject.WorldTransform = traceTransform;

		GameObject.Flags = GameObjectFlags.None;
		GameObject.Tags.Remove( "isdragdrop" );

		EditorScene.Selection.Clear();
		EditorScene.Selection.Add( GameObject );

		undoScope.Dispose();
		undoScope = null;

		GameObject = null;
	}

	public override void OnDestroy()
	{
		GameObject?.Destroy();
		GameObject = null;
	}
}
