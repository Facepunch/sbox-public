namespace Sandbox;

public interface IInspectorEditor
{
	public bool TrySetTarget( List<GameObject> selection );
	public string Title { get; }
}
