namespace Editor;

public partial class SceneEditorSession
{
	/// <summary>The editor's construction frame. It is not saved in the scene.</summary>
	public Transform WorkPlane { get; private set; } = Transform.Zero;

	public bool IsWorkPlaneTransforming { get; private set; }

	public void SetWorkPlane( Transform transform, bool transforming = false )
	{
		WorkPlane = new Transform( transform.Position, transform.Rotation );
		IsWorkPlaneTransforming = transforming;
	}

	public void ResetWorkPlane() => SetWorkPlane( Transform.Zero );
}
