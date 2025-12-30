
namespace Editor;

/// <summary>
/// Move selected Gameobjects.<br/> <br/> 
/// <b>Ctrl</b> - toggle snap to grid<br/>
/// <b>Shift</b> - duplicate selection
/// </summary>
[Title( "Move/Position" )]
[Icon( "control_camera" )]
[Alias( "tools.position-tool" )]
[Group( "1" )]
[Order( 0 )]
public class PositionEditorTool : EditorTool
{
	readonly Dictionary<GameObject, Transform> startPoints = [];
	readonly HashSet<Rigidbody> bodies = [];

	Vector3 moveDelta;
	Vector3 handlePosition;

	IDisposable undoScope;

	public override void OnDisabled()
	{
		base.OnDisabled();

		ClearBodies();
	}

	private void ClearBodies()
	{
		foreach ( var body in bodies )
		{
			if ( !body.IsValid() )
				continue;

			body.SetTargetTransform( null );
		}

		bodies.Clear();
	}

	public override void OnUpdate()
	{
		var nonSceneGos = Selection.OfType<GameObject>().Where( go => go.GetType() != typeof( Sandbox.Scene ) );
		if ( nonSceneGos.Count() == 0 ) return;

		var bbox = BBox.FromPoints( nonSceneGos.Select( x => x.WorldPosition ) );
		var handleRotation = Gizmo.Settings.GlobalSpace ? Rotation.Identity : nonSceneGos.FirstOrDefault().WorldRotation;

		if ( !Gizmo.Pressed.Any && Gizmo.HasMouseFocus )
		{
			ClearBodies();

			startPoints.Clear();
			moveDelta = default;
			handlePosition = bbox.Center;
			undoScope?.Dispose();
			undoScope = null;
		}

		using ( Gizmo.Scope( "Tool", new Transform( bbox.Center ) ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;

			if ( Gizmo.Control.Position( "position", Vector3.Zero, out var delta, handleRotation ) )
			{
				moveDelta += delta;

				StartDrag( nonSceneGos );

				var offset = (moveDelta + handlePosition) * handleRotation.Inverse;
				offset = Gizmo.Snap( offset, moveDelta * handleRotation.Inverse );
				offset *= handleRotation;
				offset -= handlePosition;

				foreach ( var entry in startPoints )
				{
					OnMoveObject( entry.Key, entry.Value.Add( offset, true ) );
				}
			}
		}
	}

	private void StartDrag( IEnumerable<GameObject> selectedGos )
	{
		if ( startPoints.Count != 0 )
			return;

		if ( Gizmo.IsShiftPressed )
		{
			undoScope ??= SceneEditorSession.Active.UndoScope( "Duplicate Object(s)" ).WithGameObjectCreations().Push();

			DuplicateSelection();
		}
		else
		{
			undoScope ??= SceneEditorSession.Active.UndoScope( "Transform Object(s)" ).WithGameObjectChanges( selectedGos, GameObjectUndoFlags.Properties ).Push();

			selectedGos.DispatchPreEdited( nameof( GameObject.LocalPosition ) );
		}

		foreach ( var entry in selectedGos )
		{
			startPoints[entry] = entry.WorldTransform;
		}
	}

	private void OnMoveObject( GameObject gameObject, Transform transform )
	{
		if ( !gameObject.IsValid() )
			return;

		if ( !Scene.IsEditor )
		{
			var rb = gameObject.GetComponent<Rigidbody>();

			if ( rb.IsValid() && rb.MotionEnabled )
			{
				bodies.Add( rb );
				rb.SetTargetTransform( transform );

				return;
			}
		}

		gameObject.BreakProceduralBone();
		gameObject.WorldTransform = transform;

		gameObject.DispatchEdited( nameof( GameObject.LocalPosition ) );
	}

	[Shortcut( "tools.position-tool", "w", typeof( SceneDock ) )]
	public static void ActivateSubTool()
	{
		if ( !(EditorToolManager.CurrentModeName == nameof( ObjectEditorTool ) || EditorToolManager.CurrentModeName == "object") ) return;
		EditorToolManager.SetSubTool( nameof( PositionEditorTool ) );
	}
}

