namespace Editor;

partial class SceneEditorSession
{
	/// <summary>
	/// The game session of this editor session, if playing.
	/// </summary>
	public GameEditorSession GameSession { get; private set; }

	public virtual bool IsPlaying => GameSession != null;

	public void SetPlaying( Scene scene )
	{
		Assert.IsNull( Playing, "Attempted to create a game session while another is active." );

		GameSession = new GameEditorSession( this, scene );

		// carry the selection over to the equivalent runtime objects
		GameSession.DeserializeSelection( SerializeSelection() );

		GameSession.MakeActive();
	}

	public virtual void StopPlaying()
	{
		GameSession?.Destroy();
		GameSession = null;

		MakeActive();
	}
}
