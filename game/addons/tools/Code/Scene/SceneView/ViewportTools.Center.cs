namespace Editor;

partial class ViewportTools
{
	EditorToolButton PlayButton { get; set; }
	EditorToolButton PauseButton { get; set; }
	EditorToolButton EjectButton { get; set; }
	Widget PlayToolbar { get; set; }

	enum PlayControlState
	{
		Ready,
		Playing,
		Hidden
	}

	PlayControlState _playState;

	PlayControlState CurrentPlayState => sceneViewWidget.Session switch
	{
		{ IsPrefabSession: true } => PlayControlState.Hidden,
		{ IsPlaying: true } => PlayControlState.Playing,
		_ when Game.IsPlaying => PlayControlState.Hidden,
		_ => PlayControlState.Ready
	};

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
		_playState = CurrentPlayState;
		PlayToolbar.Visible = _playState != PlayControlState.Hidden;

		if ( _playState == PlayControlState.Hidden ) return;

		var isPlaying = _playState == PlayControlState.Playing;
		PlayButton.Enabled = true;

		if ( isPlaying )
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

		PauseButton.Enabled = isPlaying;
		PauseButton.ToolTip = WithKeys( "Pause", "editor.pause" );

		EjectButton.Enabled = isPlaying;
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

		if ( _playState != CurrentPlayState )
			UpdateState();

		PauseButton.Color = _playState == PlayControlState.Playing && Game.IsPaused ? Theme.Blue : Theme.TextLight;
	}

	[Shortcut( "editor.pause", "F7", ShortcutType.Window )]
	private void Pause()
	{
		if ( !sceneViewWidget.Session.IsPlaying )
			return;

		// What the fuck, why isnt this a method
		Game.IsPaused = !Game.IsPaused;
	}

	private void Eject()
	{
		sceneViewWidget.ToggleEject();
	}
}
