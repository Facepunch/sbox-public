namespace Editor;

public partial class SceneViewWidget
{
	/// <summary>
	/// Nudge selected game objects in all scene modes
	/// fallback to MeshEditor.SelectionTool when a tool is already present
	/// </summary>
	[Shortcut( "editor.nudge-up", "UP", typeof( SceneViewWidget ) )]
	public void NudgeUp() => NudgeSelectedObjects( Vector2.Up );

	[Shortcut( "editor.nudge-down", "DOWN", typeof( SceneViewWidget ) )]
	public void NudgeDown() => NudgeSelectedObjects( Vector2.Down );

	[Shortcut( "editor.nudge-left", "LEFT", typeof( SceneViewWidget ) )]
	public void NudgeLeft() => NudgeSelectedObjects( Vector2.Left );

	[Shortcut( "editor.nudge-right", "RIGHT", typeof( SceneViewWidget ) )]
	public void NudgeRight() => NudgeSelectedObjects( Vector2.Right );

	private void NudgeSelectedObjects( Vector2 direction )
	{
		var tool = Tools?.CurrentSubTool ?? Tools?.CurrentTool;
		if ( tool is MeshEditor.SelectionTool )
			return;

		var viewport = LastSelectedViewportWidget;
		if ( !viewport.IsValid() )
			return;

		var gizmo = viewport.GizmoInstance;
		if ( gizmo is null )
			return;

		using var gizmoScope = gizmo.Push();
		if ( Gizmo.Pressed.Any )
			return;

		var selected = EditorScene.Selection.OfType<GameObject>().ToArray();
		if ( selected.Length == 0 )
			return;

		using var scope = SceneEditorSession.Scope();
		using var undoScope = SceneEditorSession.Active
			.UndoScope( "Move Objects (arrow key)" )
			.WithGameObjectChanges( selected, GameObjectUndoFlags.Properties )
			.Push();

		var delta = Gizmo.Nudge( Rotation.Identity, direction );

		foreach ( var go in selected )
		{
			go.WorldPosition -= delta;
		}
	}
}
