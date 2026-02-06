namespace Editor;

public static class VRToggle
{
	[Event( "editor.titlebar.buttons.build", Priority = 10 )]
	public static void OnBuildTitleBarButtons( TitleBarButtons titleBarButtons )
	{
		if ( EditorUtility.VR.HasHeadset )
		{
			var icon = Pixmap.FromFile( "common/virtual_reality.png" );
			var button = titleBarButtons.AddToggleButton( icon, ( v ) => EditorUtility.VR.Enabled = v, EditorUtility.VR.Enabled );
			button.ToolTip = "Toggle VR";
		}
	}
}
