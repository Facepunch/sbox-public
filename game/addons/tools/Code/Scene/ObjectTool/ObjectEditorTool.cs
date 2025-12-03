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
	/// <summary>
	/// Non null when selection is active
	/// </summary>
	SceneSelectionMode selectionMode;

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

	public override void OnDisabled()
	{
		base.OnDisabled();

		FinishSelection();
	}

	void UpdateSelectionMode()
	{
		if ( !Gizmo.HasMouseFocus )
			return;

		//
		// trigger selection if mouse is down, we're not dragging anything, and have moved the mouse since press
		//
		if ( selectionMode is null && !Gizmo.IsAltPressed && Gizmo.Pressed.IsActive && !Gizmo.Pressed.Any && Gizmo.Pressed.CursorDelta.Length > 3 )
		{
			selectionMode = new BoxSelectionMode( SceneEditorSession.Active.Scene, SceneEditorSession.Active.Selection );
		}

		//
		// Did we click nothing and aren't wanting to drag?
		//
		if ( selectionMode is null && Gizmo.WasLeftMouseReleased && !Gizmo.Pressed.Any )
		{
			using ( Scene.Editor?.UndoScope( "Deselect all" ).Push() )
			{
				EditorScene.Selection.Clear();
			}
		}

		//
		// Think the active selection mode
		//
		if ( selectionMode is not null )
		{
			selectionMode?.Think( Gizmo.CurrentRay );
		}

		//
		// Release the active selecion mode
		//
		if ( !Gizmo.Pressed.IsActive )
		{
			FinishSelection();

		}
	}

	void FinishSelection()
	{
		selectionMode?.Finish( Gizmo.CurrentRay );
		selectionMode = null;
	}

	[Shortcut( "tools.object-tool", "o", typeof( SceneViewportWidget ) )]
	public static void ActivateSubTool()
	{
		EditorToolManager.SetTool( nameof( ObjectEditorTool ) );
	}


}
