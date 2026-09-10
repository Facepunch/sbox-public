namespace Editor;

public class GameEditorSession : SceneEditorSession
{
	public SceneEditorSession Parent { get; init; }

	public override bool IsPlaying => true;

	public GameEditorSession( SceneEditorSession parent, Scene scene ) : base( scene )
	{
		Parent = parent;
	}

	public override void StopPlaying() => Parent.StopPlaying();

	public override void FrameTo( in BBox box )
	{
		Parent.FrameTo( box );
	}
}
