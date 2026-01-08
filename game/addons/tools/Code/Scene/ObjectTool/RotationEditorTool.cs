namespace Editor;

/// <summary>
/// Rotate selected GameObjects.<br/> <br/> 
/// <b>Ctrl</b> - toggle snap to grid
/// </summary>
[Title( "Rotate" )]
[Icon( "360" )]
[Alias( "tools.rotate-tool" )]
[Group( "1" )]
[Order( 1 )]
public class RotationEditorTool : EditorTool
{
	Dictionary<GameObject, Transform> startPoints = new Dictionary<GameObject, Transform>();
	Angles moveDelta;
	Vector3 handlePosition;
	Rotation handleRotation;

	IDisposable undoScope;

	public override void OnUpdate()
	{
		var nonSceneGos = Selection.OfType<GameObject>().Where( go => go.GetType() != typeof( Sandbox.Scene ) );
		if ( nonSceneGos.Count() == 0 ) return;

		var bbox = BBox.FromPoints( nonSceneGos.Select( x => x.WorldPosition ) );

		if ( !Gizmo.Pressed.Any && Gizmo.HasMouseFocus )
		{
			startPoints.Clear();
			moveDelta = Rotation.Identity;
			handleRotation = nonSceneGos.FirstOrDefault().WorldRotation;
			handlePosition = bbox.Center;
			undoScope?.Dispose();
			undoScope = null;
		}

		var basis = Gizmo.Settings.GlobalSpace ? Rotation.Identity : handleRotation;

		// Stop updating the handle position if we're dragging

		var origin = Gizmo.Pressed.Any ? handlePosition : bbox.Center;

		using ( Gizmo.Scope( "Tool", new Transform( origin, basis ) ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;

			if ( Gizmo.Control.Rotate( "rotation", out var angleDelta ) )
			{
				StartDrag( nonSceneGos );

				moveDelta += angleDelta;

				var snapped = Gizmo.Snap( moveDelta, angleDelta );

				foreach ( var entry in startPoints )
				{
					var rot = basis * snapped * basis.Inverse;
					var position = entry.Value.Position - handlePosition;
					position *= rot;
					position += handlePosition;
					rot *= entry.Value.Rotation;
					var scale = entry.Value.Scale;

					var oldPosition = entry.Key.LocalPosition;

					entry.Key.BreakProceduralBone();
					entry.Key.WorldTransform = new Transform( position, rot, scale );

					var newPosition = entry.Key.LocalPosition;

					// Only dispatch position event if it actually changed

					if ( !newPosition.AlmostEqual( oldPosition ) )
					{
						entry.Key.DispatchEdited( nameof( GameObject.LocalPosition ) );
					}

					entry.Key.DispatchEdited( nameof( GameObject.LocalRotation ) );
				}
			}
		}
	}

	private void StartDrag( IEnumerable<GameObject> selectedGos )
	{
		if ( startPoints.Any() ) return;

		if ( Gizmo.IsShiftPressed )
		{
			undoScope ??= SceneEditorSession.Active.UndoScope( "Duplicate Object(s)" ).WithGameObjectCreations().Push();

			DuplicateSelection();
		}
		else
		{
			undoScope ??= SceneEditorSession.Active.UndoScope( "Transform Object(s)" ).WithGameObjectChanges( selectedGos, GameObjectUndoFlags.Properties ).Push();

			selectedGos.DispatchPreEdited( nameof( GameObject.LocalPosition ) );
			selectedGos.DispatchPreEdited( nameof( GameObject.LocalRotation ) );
		}

		foreach ( var entry in selectedGos )
		{
			startPoints[entry] = entry.WorldTransform;
		}
	}


	[Shortcut( "tools.rotate-tool", "e", typeof( SceneViewWidget ) )]
	public static void ActivateSubTool()
	{
		if ( !(EditorToolManager.CurrentModeName == nameof( ObjectEditorTool ) || EditorToolManager.CurrentModeName == "object") ) return;
		EditorToolManager.SetSubTool( nameof( RotationEditorTool ) );
	}
}
