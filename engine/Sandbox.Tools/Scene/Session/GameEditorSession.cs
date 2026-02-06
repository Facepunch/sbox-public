namespace Editor;

public class GameEditorSession : SceneEditorSession
{
	internal static GameEditorSession Current = null;

	public SceneEditorSession Parent { get; init; }

	public override bool IsPlaying => true;

	public GameEditorSession( SceneEditorSession parent, Scene scene ) : base( scene )
	{
		Parent = parent;

		Assert.IsNull( Current, "Attempted to create new GameEditorSession when one already exists!" );
		Current = this;
	}

	public override void Destroy()
	{
		base.Destroy();

		Current = null;
	}

	public override void StopPlaying() => Parent.StopPlaying();

	public override void FrameTo( in BBox box )
	{
		Parent.FrameTo( box );
	}
}
