using System.Threading;

namespace Editor;

[DropObject( "prefab", "prefab", "prefab_c" )]
partial class PrefabDropObject : BaseDropObject
{
	private IDisposable undoScope;

	protected override async Task Initialize( string dragData, CancellationToken token )
	{
		Asset asset = await InstallAsset( dragData, token );

		if ( asset is null )
			return;

		if ( token.IsCancellationRequested )
			return;

		var pf = asset.LoadResource<PrefabFile>();
		if ( pf is null ) return;

		if ( SceneEditorSession.Active.Scene is PrefabScene prefabScene )
		{
			var currentPf = prefabScene.ToPrefabFile();
			if ( currentPf == pf )
			{
				Log.Warning( "Cannot place the same prefab in itself." );
				return;
			}
		}

		// We should REALLY be scoped over whatever scene we're dragging over
		using ( SceneEditorSession.Scope() )
		{
			undoScope = SceneEditorSession.Active.UndoScope( "Drop Prefab" ).WithGameObjectCreations().Push();

			var go = SceneUtility.GetPrefabScene( pf );
			GameObject = go.Clone();
			GameObject.Flags = GameObjectFlags.NotSaved | GameObjectFlags.Hidden;
			GameObject.Tags.Add( "isdragdrop" );

			Bounds = GameObject.GetLocalBounds();

			if ( Bounds.Size.Length < 4 )
				Bounds = BBox.FromPositionAndSize( 0, 4 );

			PivotPosition = Bounds.ClosestPoint( Vector3.Down * 10000 );
			Rotation = GameObject.WorldRotation;
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
