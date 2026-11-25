namespace Editor;

partial class ViewportTools
{
	void BuildWorkPlaneToolbar( Layout layout )
	{
		var group = layout.Add( AddGroup() );
		var pick = AddButton( group.Layout, WithKeys( "Set Workplane - pick a surface (Shift: object)", "scene.workplane" ),
			"colorize", PickWorkPlane );
		pick.IsActive = () => sceneViewWidget.Tools.WorkPlane.IsActive;

		var options = AddButton( group.Layout, "Workplane: move, rotate or reset", "arrow_drop_down", OpenWorkPlaneMenu );
		options.FixedWidth = Theme.RowHeight * 0.5f;
	}

	void OpenWorkPlaneMenu()
	{
		var menu = new ContextMenu( this );
		menu.AddOption( "Set Workplane", "colorize", PickWorkPlane, "scene.workplane" );
		menu.AddOption( "Move Workplane", "open_with", MoveWorkPlane, "scene.workplane-move" );
		menu.AddOption( "Rotate Workplane", "rotate_right", RotateWorkPlane, "scene.workplane-rotate" );
		menu.AddSeparator();
		menu.AddOption( "Reset Workplane", "restart_alt", ResetWorkPlane, "scene.workplane-reset" );
		menu.AddOption( "Done", "done", () => sceneViewWidget.Tools.WorkPlane.Finish() )
			.Enabled = sceneViewWidget.Tools.WorkPlane.IsActive;
		menu.OpenAtCursor();
	}

	void BeginWorkPlane( WorkPlaneControl.EditMode mode )
	{
		if ( sceneViewWidget.CurrentView == SceneViewWidget.ViewMode.Game ) return;
		sceneViewWidget.Tools.WorkPlane.Begin( sceneViewWidget.Session, mode );
		sceneViewWidget.LastSelectedViewportWidget?.Renderer.Focus();
	}

	[Shortcut( "scene.workplane", "Shift+Q", typeof( SceneViewWidget ) )]
	void PickWorkPlane() => BeginWorkPlane( WorkPlaneControl.EditMode.Rotate );

	[Shortcut( "scene.workplane-move", "Alt+Shift+T", typeof( SceneViewWidget ) )]
	void MoveWorkPlane() => BeginWorkPlane( WorkPlaneControl.EditMode.Move );

	[Shortcut( "scene.workplane-rotate", "Alt+Shift+R", typeof( SceneViewWidget ) )]
	void RotateWorkPlane() => BeginWorkPlane( WorkPlaneControl.EditMode.Rotate );

	[Shortcut( "scene.workplane-reset", "Alt+Shift+Q", typeof( SceneViewWidget ) )]
	void ResetWorkPlane()
	{
		if ( sceneViewWidget.CurrentView == SceneViewWidget.ViewMode.Game ) return;
		sceneViewWidget.Tools.WorkPlane.Reset( sceneViewWidget.Session );
	}

	[Shortcut( "scene.workplane-finish", "Enter", typeof( SceneViewWidget ), Priority = 100 )]
	void FinishWorkPlane()
	{
		if ( sceneViewWidget.Tools.WorkPlane.IsActive )
			sceneViewWidget.Tools.WorkPlane.Finish();
		else if ( !sceneViewWidget.Tools.WorkPlane.IsHandlingInput )
			EditorShortcuts.PassShortcut = true;
	}

	[Shortcut( "scene.workplane-drop", "Space", typeof( SceneViewWidget ), Priority = 100 )]
	void DropWorkPlane() => FinishWorkPlane();

	[Shortcut( "scene.workplane-cancel", "ESC", typeof( SceneViewWidget ), Priority = 100 )]
	void CancelWorkPlane()
	{
		if ( sceneViewWidget.Tools.WorkPlane.IsActive )
			sceneViewWidget.Tools.WorkPlane.Reset( sceneViewWidget.Session );
		else if ( !sceneViewWidget.Tools.WorkPlane.IsHandlingInput )
			EditorShortcuts.PassShortcut = true;
	}
}
