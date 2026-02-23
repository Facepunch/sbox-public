namespace Editor;

public partial class SceneViewportWidget : Widget
{
	[Shortcut( "grid.toggle-grid", "CTRL+G" )]
	void ToggleGrid()
	{
		State.ShowGrid = !State.ShowGrid;
	}

	// TODO: Need a better way to register these... Maybe a codegen attribute on a bool { get; } property?
	// This works well for now though since it's a non-static void on the viewport widget itself, meaning 
	// this empty function will eat the shortcut when focused so no other shortcuts with the same keys get called.

	// Only consume when right-click is held (camera navigation mode)
	// Otherwise, pass the shortcut so gizmo tool shortcuts (W/E/R) can be invoked
	[Shortcut( "scene.move-forward", "W" )]
	void shortcutMoveForward()
	{
		if ( !Application.MouseButtons.HasFlag( MouseButtons.Right ) )
			EditorShortcuts.PassShortcut = true;
	}

	[Shortcut( "scene.move-backward", "S" )] void shortcutMoveBackward() { }
	[Shortcut( "scene.move-left", "A" )] void shortcutMoveLeft() { }
	[Shortcut( "scene.move-right", "D" )] void shortcutMoveRight() { }

	[Shortcut( "scene.move-up", "E" )]
	void shortcutMoveUp()
	{
		if ( !Application.MouseButtons.HasFlag( MouseButtons.Right ) )
			EditorShortcuts.PassShortcut = true;
	}

	[Shortcut( "scene.move-down", "Q" )] void shortcutMoveDown() { }
	[Shortcut( "scene.movement-quick", "SHIFT" )] void shortcutMoveFaster() { }
	[Shortcut( "scene.movement-slow", "CTRL" )] void shortcutMoveSlower() { }
}
