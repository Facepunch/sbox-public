using Editor.MapDoc;
using System.Diagnostics;

namespace Editor.MapEditor;

[CanDrop( "prefab" )]
class PrefabDropTarget : IMapViewDropTarget
{
	MapGameObject PrefabGameObject { get; set; }
	BBox Bounds { get; set; }
	Vector3 PivotPosition { get; set; }

	public void DragEnter( Package package, MapView view )
	{
		// ...
	}

	public void DragEnter( Asset asset, MapView view )
	{
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

		var scene = view.MapDoc.World.Scene;
		using ( scene.Push() )
		{
			var go = SceneUtility.GetPrefabScene( pf );
			var GameObject = go.Clone();
			GameObject.Flags = GameObjectFlags.NotSaved | GameObjectFlags.Hidden;
			GameObject.Tags.Add( "isdragdrop" );

			Bounds = GameObject.GetBounds();

			if ( Bounds.Size.Length < 4 )
				Bounds = BBox.FromPositionAndSize( 0, 4 );

			PivotPosition = Bounds.ClosestPoint( Vector3.Down * 10000 );

			PrefabGameObject = new( gameObject: GameObject );
		}
	}

	public void DragMove( MapView view )
	{
		if ( !PrefabGameObject.IsValid() )
			return;

		view.BuildRay( out Vector3 rayStart, out Vector3 rayEnd );
		var tr = Trace.Ray( rayStart, rayEnd ).Run( view.MapDoc.World );

		var rot = Rotation.LookAt( tr.Normal, Vector3.Up ) * Rotation.From( 90, 0, 0 );
		var pos = tr.HitPosition + tr.Normal * PivotPosition.Length;

		PrefabGameObject.Position = pos;
		PrefabGameObject.Angles = rot.Angles();

		traceTransform = new Transform( pos, rot );
	}

	Transform traceTransform;

	public void DrawGizmos( MapView view )
	{
		using var scope = Gizmo.Scope( "DropObject", traceTransform );
		Gizmo.Draw.Color = Color.White.WithAlpha( 0.3f );
		Gizmo.Draw.LineBBox( Bounds );
	}

	public void DragLeave( MapView view )
	{
		if ( PrefabGameObject.IsValid() ) view.MapDoc.DeleteNode( PrefabGameObject );
	}

	public void DragDropped( MapView view )
	{
		if ( !PrefabGameObject.IsValid() ) return;

		var go = PrefabGameObject.GameObject;
		go.Flags = GameObjectFlags.None;
		go.Tags.Remove( "isdragdrop" );

		History.MarkUndoPosition( "New Scene Prefab" );
		History.KeepNew( PrefabGameObject );

		Selection.Clear();
		Selection.Add( PrefabGameObject );

		PrefabGameObject = null;
	}
}
