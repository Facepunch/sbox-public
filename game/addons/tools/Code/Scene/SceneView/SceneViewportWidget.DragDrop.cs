namespace Editor;

public partial class SceneViewportWidget : Widget
{
	List<BaseDropObject> drops = new();
	BaseDropObject currentDrop;

	void UpdateDragDrops()
	{
		drops.RemoveAll( x => x.Deleted );

		// TODO: make this work better with multiviewport (draw gizmos in correct viewport, use correct gizmo settings etc)
		foreach ( var drop in drops )
		{
			drop.Tick();
		}
	}

	public override void OnDragDrop( DragEvent ev )
	{
		base.OnDragDrop( ev );

		if ( currentDrop is not null )
		{
			currentDrop.Dropped = true;
			currentDrop = null;
		}

		Focus();
	}

	Task<BaseDropObject> creatingDrop;

	float? lastHeight;

	public override void OnDragHover( DragEvent ev )
	{
		base.OnDragHover( ev );

		// TODO: Use DragAssetData and deprecate StartInitialize using string

		if ( string.IsNullOrWhiteSpace( ev.Data.Text ) )
			return;

		// Dragging an asset while one is selected will drag them both,
		// in that situation we only want the first one.
		var file = ev.Data.Files.FirstOrDefault();
		if ( string.IsNullOrWhiteSpace( file ) )
		{
			var split = ev.Data.Text.Split( "\n" );
			if ( split is null || split.Length == 0 )
				return;

			file = split.FirstOrDefault();
			if ( string.IsNullOrWhiteSpace( file ) )
				return;
		}

		ev.Action = DropAction.Copy;
		Session.MakeActive();

		using var sceneScope = SceneEditorSession.Scope();

		if ( currentDrop is null )
		{
			creatingDrop ??= BaseDropObject.CreateDropFor( file );

			if ( !creatingDrop.IsCompleted )
			{
				return;
			}

			currentDrop = creatingDrop.Result;
			creatingDrop = default;

			if ( currentDrop is not null )
			{
				_ = currentDrop.StartInitialize( file );
				drops.Add( currentDrop );
			}
			else
			{
				ev.Action = DropAction.Ignore;
			}

			lastHeight = null;
		}

		if ( currentDrop is not null )
		{
			// TODO: Render meshes don't support tags, material drop doesn't need it though
			var tr = SceneEditorSession.Active.Scene.Trace
							.WithoutTags( "isdragdrop", "trigger" )
							.UseRenderMeshes( currentDrop is MaterialDropObject )
							.UsePhysicsWorld( currentDrop is not MaterialDropObject )
							.Ray( _activeCamera.ScreenPixelToRay( (ev.LocalPosition * DpiScale) - Renderer.Position ), _activeCamera.ZFar + MathF.Abs( _activeCamera.ZNear ) )
							.Run();

			if ( !tr.Hit )
			{
				var dist = 0.0f;
				if ( !State.Is2D )
					dist = State.CameraPosition.z < 0.0f ? lastHeight ?? State.CameraPosition.z - 200 : 0.0f;

				var plane = new Plane( State.Is2D ? State.CameraRotation.Backward : Vector3.Up, dist );
				if ( plane.TryTrace( new Ray( tr.StartPosition, tr.Direction ), out tr.EndPosition ) )
				{
					tr.Normal = plane.Normal;
					tr.HitPosition = tr.EndPosition;
				}
			}
			else
			{
				lastHeight = tr.HitPosition.z;
			}

			currentDrop.UpdateDrag( tr, GizmoInstance.Settings );
		}
	}

	public override void OnDragLeave()
	{
		if ( currentDrop is not null )
		{
			currentDrop.Delete();
			drops.Remove( currentDrop );

			currentDrop = null;
		}
	}
}
