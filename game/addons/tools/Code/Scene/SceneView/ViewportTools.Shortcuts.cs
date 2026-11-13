namespace Editor;

partial class ViewportTools
{
	[Shortcut( "scene.toggle-gizmos", "SHIFT+G", typeof( SceneViewportWidget ) )]
	void ToggleGizmos()
	{
		EditorScene.GizmoSettings.GizmosEnabled = !EditorScene.GizmoSettings.GizmosEnabled;
		UpdateChildren();
	}

	[Shortcut( "grid.toggle-grid-snap", "CTRL+SHIFT+Q", typeof( SceneViewportWidget ) )]
	void ToggleGridSnap()
	{
		EditorScene.GizmoSettings.SnapToGrid = !EditorScene.GizmoSettings.SnapToGrid;
		UpdateChildren();
	}

	[Shortcut( "grid.decrease-grid-size", "[", typeof( SceneViewportWidget ) )]
	void DecreaseGridSize()
	{
		var value = EditorScene.GizmoSettings.GridSpacing;
		if ( value <= 0.125f )
			return;

		EditorScene.GizmoSettings.GridSpacing = value / 2f;
		UpdateChildren();
	}

	[Shortcut( "grid.increase-grid-size", "]", typeof( SceneViewportWidget ) )]
	void IncreaseGridSize()
	{
		var value = EditorScene.GizmoSettings.GridSpacing;
		if ( value >= 128f )
			return;

		EditorScene.GizmoSettings.GridSpacing = value * 2;
		UpdateChildren();
	}

	[Shortcut( "scene.toggle-global-space", "CTRL+T", typeof( SceneViewportWidget ) )]
	void ToggleGlobalSpace()
	{
		EditorScene.GizmoSettings.GlobalSpace = !EditorScene.GizmoSettings.GlobalSpace;
		UpdateChildren();
	}
}
