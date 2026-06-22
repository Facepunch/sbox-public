namespace Editor;

/// <summary>
/// Scale selected GameObjects.<br/> <br/>
/// <b>Ctrl</b> - toggle snap to grid<br/>
/// <b>Shift</b> - scale all 3 axis
/// </summary>
[Title( "Scale" )]
[Icon( "zoom_out_map" )]
[Alias( "tools.scale-tool" )]
[Group( "1" )]
[Order( 2 )]
public class ScaleEditorTool : EditorTool
{
	readonly Dictionary<GameObject, (Vector3 StartScale, Vector3 StartSize)> startState = [];
	Vector3 scaleDelta;
	IDisposable undoScope;

	public override void OnUpdate()
	{
		var nonSceneGos = Selection.OfType<GameObject>().Where( go => go.GetType() != typeof( Sandbox.Scene ) );
		if ( !nonSceneGos.Any() ) return;

		var handlePosition = nonSceneGos.First().WorldPosition;
		var handleRotation = nonSceneGos.First().WorldRotation;

		if ( !Gizmo.Pressed.Any && Gizmo.HasMouseFocus )
		{
			startState.Clear();
			scaleDelta = default;
			undoScope?.Dispose();
			undoScope = null;
		}

		using ( Gizmo.Scope( "Tool", new Transform( handlePosition ) ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;

			if ( Gizmo.Control.Scale( "scale", Vector3.Zero, out var delta, handleRotation ) )
			{
				scaleDelta += delta / 0.01f;

				if ( startState.Count == 0 )
				{
					undoScope ??= SceneEditorSession.Active.UndoScope( "Transform Object(s)" ).WithGameObjectChanges( nonSceneGos, GameObjectUndoFlags.All ).Push();

					foreach ( var go in nonSceneGos )
					{
						go.DispatchPreEdited( nameof( GameObject.LocalScale ) );
						go.BreakProceduralBone();
						startState[go] = (go.WorldScale, go.GetBounds().Size);
					}
				}

				foreach ( var (go, (startScale, startSize)) in startState )
				{
					if ( !go.IsValid() ) continue;

					var newSize = startSize + Gizmo.Snap( scaleDelta, scaleDelta ) * 2.0f;

					go.WorldScale = new Vector3(
						startSize.x > 0.001f ? startScale.x * newSize.x / startSize.x : startScale.x,
						startSize.y > 0.001f ? startScale.y * newSize.y / startSize.y : startScale.y,
						startSize.z > 0.001f ? startScale.z * newSize.z / startSize.z : startScale.z
					);
					go.DispatchEdited( nameof( GameObject.LocalScale ) );
				}
			}
		}
	}

	[Shortcut( "tools.scale-tool", "r", typeof( SceneViewWidget ) )]
	public static void ActivateSubTool()
	{
		if ( !(EditorToolManager.CurrentModeName == nameof( ObjectEditorTool ) || EditorToolManager.CurrentModeName == "object") ) return;
		EditorToolManager.SetSubTool( nameof( ScaleEditorTool ) );
	}
}
