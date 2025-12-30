using static Sandbox.VertexLayout;

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
	IDisposable undoScope;

	public override void OnUpdate()
	{
		var nonSceneGos = Selection.OfType<GameObject>().Where( go => go.GetType() != typeof( Sandbox.Scene ) );
		if ( nonSceneGos.Count() == 0 ) return;

		var handleScale = Selection.OfType<GameObject>().FirstOrDefault().WorldScale;
		var handlePosition = Selection.OfType<GameObject>().FirstOrDefault().WorldPosition;
		var handleRotation = Selection.OfType<GameObject>().FirstOrDefault().WorldRotation;

		using ( Gizmo.Scope( "Tool", new Transform( handlePosition ) ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;

			if ( Gizmo.Control.Scale( "scale", handleScale, out var newScale, handleRotation ) )
			{
				var delta = newScale - handleScale;

				undoScope ??= SceneEditorSession.Active.UndoScope( "Transform Object(s)" ).WithGameObjectChanges( Selection.OfType<GameObject>(), GameObjectUndoFlags.All ).Push();

				foreach ( var go in nonSceneGos )
				{
					go.DispatchPreEdited( nameof( GameObject.LocalScale ) );

					go.BreakProceduralBone();
					go.WorldScale += delta;

					go.DispatchEdited( nameof( GameObject.LocalScale ) );
				}
			}

			if ( !Gizmo.Pressed.Any && Gizmo.HasMouseFocus )
			{
				undoScope?.Dispose();
				undoScope = null;
			}
		}
	}


	[Shortcut( "tools.scale-tool", "r", typeof( SceneDock ) )]
	public static void ActivateSubTool()
	{
		if ( !(EditorToolManager.CurrentModeName == nameof( ObjectEditorTool ) || EditorToolManager.CurrentModeName == "object") ) return;
		EditorToolManager.SetSubTool( nameof( ScaleEditorTool ) );
	}
}

