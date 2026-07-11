namespace Editor;

partial class ViewportTools
{
	EditorToolButton PlayButton { get; set; }
	EditorToolButton PauseButton { get; set; }
	EditorToolButton EjectButton { get; set; }

	private void BuildPlayToolbar( Layout toolbar )
	{
		PlayButton = AddButton( toolbar, "Play", "play_arrow", PlayStop );
		PauseButton = AddButton( toolbar, "Pause", "pause", Pause );
		EjectButton = AddButton( toolbar, "Eject", "eject", Eject );

		UpdateState();
	}

	[Event( "keybinds.update" )]
	private void OnKeybindsUpdated()
	{
		UpdateState();
	}

	private static string WithKeys( string text, string shortcut )
	{
		var keys = EditorShortcuts.GetDisplayKeys( shortcut );
		return string.IsNullOrEmpty( keys ) ? text : $"{text} [{keys}]";
	}

	/// <summary>
	/// When the state of game changes, e.g we're playing, stopping, ejecting, pausing, this gets called.
	/// </summary>
	private void UpdateState()
	{
		// Prefabs nada
		if ( sceneViewWidget.Session.IsPrefabSession )
		{
			PlayButton.Enabled = false;
			PauseButton.Enabled = false;
			EjectButton.Enabled = false;
			return;
		}

		if ( Game.IsPlaying )
		{
			PlayButton.ToolTip = WithKeys( "Stop", "editor.toggle-play" );
			PlayButton.GetIcon = () => "stop";
			PlayButton.Color = Theme.Red;
		}
		else
		{
			PlayButton.ToolTip = WithKeys( "Play", "editor.toggle-play" );
			PlayButton.GetIcon = () => "play_arrow";
			PlayButton.Color = Theme.Green;
		}

		// We can only pause whilst we're gaming
		PauseButton.Enabled = Game.IsPlaying;
		PauseButton.ToolTip = WithKeys( "Pause", "editor.pause" );

		EjectButton.Enabled = Game.IsPlaying;
		bool isEjected = sceneViewWidget.CurrentView == SceneViewWidget.ViewMode.GameEjected;
		EjectButton.GetIcon = () => isEjected ? "sports_esports" : "eject";
		EjectButton.ToolTip = WithKeys( isEjected ? "Return to Game" : "Eject", "editor.eject" );
		EjectButton.Color = isEjected ? Theme.Green : Theme.TextLight;
	}


	private void PlayStop()
	{
		if ( !Game.IsPlaying )
		{
			EditorScene.Play( sceneViewWidget.Session );
		}
		else
		{
			EditorScene.Stop();
		}
	}

	[EditorEvent.Frame]
	private void UpdatePauseState()
	{
		if ( !PauseButton.IsValid() )
			return;

		PauseButton.Color = Game.IsPlaying && Game.IsPaused ? Theme.Blue : Theme.TextLight;
	}

	[Shortcut( "editor.pause", "F7", ShortcutType.Window )]
	private void Pause()
	{
		if ( !Game.IsPlaying )
			return;

		// What the fuck, why isnt this a method
		Game.IsPaused = !Game.IsPaused;
	}

	private void Eject()
	{
		sceneViewWidget.ToggleEject();
	}
}
