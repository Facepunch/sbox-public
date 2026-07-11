using Sandbox;
using System;

namespace Editor.Mcp;

[McpToolset( "play", "Play mode - start, stop and pause the game" )]
public static partial class PlayTools
{
	/// <summary>
	/// Start playing the current scene, like pressing the editor's play button. Startup errors land
	/// in the console - check read_console if things look wrong.
	/// </summary>
	[McpTool( "play_start" )]
	public static PlayState PlayStart()
	{
		if ( Game.IsPlaying )
			throw new Exception( "Already playing - play_stop first" );

		EditorScene.Play();

		return CurrentState();
	}

	/// <summary>
	/// Stop playing and return to editing.
	/// </summary>
	[McpTool( "play_stop" )]
	public static PlayState PlayStop()
	{
		if ( !Game.IsPlaying )
			throw new Exception( "Not playing" );

		EditorScene.Stop();

		return CurrentState();
	}

	/// <summary>
	/// Pause or resume the running game.
	/// </summary>
	/// <param name="paused">True to pause, false to resume.</param>
	[McpTool( "play_pause" )]
	public static PlayState PlayPause( bool paused = true )
	{
		if ( !Game.IsPlaying )
			throw new Exception( "Not playing - play_start first" );

		Game.IsPaused = paused;

		return CurrentState();
	}

	private static PlayState CurrentState() => new()
	{
		IsPlaying = Game.IsPlaying,
		IsPaused = Game.IsPaused,
		Scene = Game.ActiveScene?.Name
	};

	/// <summary>
	/// Whether the game is running, paused, and what scene it's playing.
	/// </summary>
	public class PlayState
	{
		public bool IsPlaying { get; set; }

		public bool IsPaused { get; set; }

		/// <summary>The scene being played. Null when not playing.</summary>
		public string Scene { get; set; }
	}

}
