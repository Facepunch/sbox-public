namespace Editor;

/// <summary>
/// Opens the compile window for the active scene.
/// </summary>
sealed class SceneCompileToolbar : Widget
{
	readonly IconButton _button;

	internal SceneCompileToolbar( Widget parent ) : base( parent )
	{
		FixedSize = Theme.ControlHeight;
		Layout = Layout.Row();
		_button = Layout.Add( new IconButton( "hardware", Compile )
		{
			FixedSize = Theme.ControlHeight,
			IconSize = HeaderBarStyle.IconSize,
			Background = Color.Transparent,
			ToolTip = $"Scene Compiler [{EditorShortcuts.GetDisplayKeys( "scene.compile" )}]"
		} );
	}

	[EditorEvent.Frame]
	void UpdateScene()
	{
		Visible = !Game.IsPlaying && GetAncestor<SceneViewWidget>().IsValid() && SceneEditorSession.Active is { IsPrefabSession: false };
		var session = SceneCompileSession.Current;
		var color = session.Running ? Theme.Blue : session.IsCompiled ? Theme.Green : Theme.Yellow;
		if ( _button.Foreground == color ) return;
		_button.Foreground = color;
		_button.Update();
	}

	/// <summary>
	/// Opens the work list before starting a compile.
	/// </summary>
	[Menu( "Editor", "Scene/Scene Compiler", "hardware", Priority = 1001 )]
	[Shortcut( "scene.compile", "F9", typeof( SceneViewWidget ) )]
	public static void Compile() => SceneCompileWindow.Open();
}
