
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

	protected override void OnBoxSelect( Frustum frustum, Rect screenRect, bool isFinal )
	{
		var selection = new HashSet<GameObject>();
		var previous = new HashSet<GameObject>();

		bool fullyInside = true;
		bool removing = Gizmo.IsCtrlPressed;

		foreach ( var mr in Scene.GetAllComponents<ModelRenderer>() )
		{
			var bounds = mr.Bounds;
			if ( !frustum.IsInside( bounds, !fullyInside ) )
			{
				previous.Add( mr.GameObject );
				continue;
			}

			selection.Add( mr.GameObject );
		}

		foreach ( var go in Scene.GetAllObjects( true ) )
		{
			if ( selection.Contains( go ) ) continue;
			if ( !go.HasGizmoHandle ) continue;
			if ( !frustum.IsInside( go.WorldPosition ) )
			{
				previous.Add( go );
				continue;
			}

			selection.Add( go );
		}

		foreach ( var selectedObj in selection )
		{
			if ( !removing )
			{
				//if ( selected.Contains( selectedObj ) ) continue;
				if ( Selection.Contains( selectedObj ) ) continue;

				Selection.Add( selectedObj );
			}
			else
			{
				if ( !Selection.Contains( selectedObj ) ) continue;

				Selection.Remove( selectedObj );
			}
		}

		foreach ( var removed in previous )
		{
			if ( removing )
			{
				//if ( !selected.Contains( removed ) ) continue;

				Selection.Add( removed );
			}
			else
			{
				//	if ( selected.Contains( removed ) ) continue;

				Selection.Remove( removed );
			}
		}
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


	[Shortcut( "tools.object-tool", "o", typeof( SceneViewportWidget ) )]
	public static void ActivateSubTool()
	{
		EditorToolManager.SetTool( nameof( ObjectEditorTool ) );
	}

	public override bool HasBoxSelectionMode() => true;


}
