
namespace Editor;

/// <summary>
/// Move, rotate and scale objects
/// </summary>
[EditorTool( "tools.object-tool" )]
[Title( "Object Select" )]
[Icon( "layers" )]
[Alias( "object" )]
[Group( "Scene" )]
[Order( -9999 )]
public class ObjectEditorTool : EditorTool
{
	public override IEnumerable<EditorTool> GetSubtools()
	{
		yield return new PositionEditorTool();
		yield return new RotationEditorTool();
		yield return new ScaleEditorTool();
	}

	public override void OnUpdate()
	{
		base.OnUpdate();

		UpdateSelectionMode();
	}

	public override Widget CreateShortcutsWidget() => new ObjectEditorToolShortcutsWidget();

	protected override void OnBoxSelect( Frustum frustum, Rect screenRect, bool isFinal )
	{
		var selection = new HashSet<GameObject>();
		var previous = new HashSet<GameObject>();

		foreach ( var go in Scene.GetAllObjects( true ) )
		{
			// GetAllObjects starts with the scene itself, which encloses everything
			if ( go == Scene ) continue;
			if ( go.Tags.Has( "hidden" ) ) continue;

			var bounds = GetDragBounds( go );

			// Nothing renderable, fall back to hitting the object's origin
			if ( bounds.Size.AlmostEqual( 0.0f ) && !go.HasGizmoHandle ) continue;

			// Partial, otherwise you'd have to fit the whole model in the box to select it
			if ( frustum.IsInside( bounds, true ) )
				selection.Add( go );
			else
				previous.Add( go );
		}

		ApplyDragSelection( selection, previous );
	}

	void UpdateSelectionMode()
	{
		if ( !Gizmo.HasMouseFocus )
			return;

		if ( Gizmo.WasLeftMouseReleased && !Gizmo.Pressed.Any && !IsBoxSelecting )
		{
			using ( Scene.Editor?.UndoScope( "Deselect all" ).Push() )
			{
				EditorScene.Selection.Clear();
			}
		}
	}


	[Shortcut( "tools.object-tool", "o", typeof( SceneViewWidget ) )]
	public static void ActivateSubTool()
	{
		EditorToolManager.SetTool( nameof( ObjectEditorTool ) );
	}

	public override bool HasBoxSelectionMode() => true;
}

file class ObjectEditorToolShortcutsWidget : Widget
{
	[Shortcut( "gameObject.duplicate-nudge-up", "SHIFT+UP", typeof( SceneViewWidget ) )]
	public void DuplicateNudgeUp() => SceneEditorMenus.DuplicateNudge( Vector2.Up );

	[Shortcut( "gameObject.duplicate-nudge-down", "SHIFT+DOWN", typeof( SceneViewWidget ) )]
	public void DuplicateNudgeDown() => SceneEditorMenus.DuplicateNudge( Vector2.Down );

	[Shortcut( "gameObject.duplicate-nudge-left", "SHIFT+LEFT", typeof( SceneViewWidget ) )]
	public void DuplicateNudgeLeft() => SceneEditorMenus.DuplicateNudge( Vector2.Left );

	[Shortcut( "gameObject.duplicate-nudge-right", "SHIFT+RIGHT", typeof( SceneViewWidget ) )]
	public void DuplicateNudgeRight() => SceneEditorMenus.DuplicateNudge( Vector2.Right );
}
