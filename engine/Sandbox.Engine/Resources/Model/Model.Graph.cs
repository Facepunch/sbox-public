namespace Sandbox;

public partial class Model
{
	private Lazy<Sandbox.Animation.Graph> _animationGraph;

	/// <summary>
	/// The default animation graph assigned to this model in ModelDoc.
	/// </summary>
	public Sandbox.Animation.Graph AnimationGraph => _animationGraph.Value;

	private Sandbox.Animation.Graph LoadAnimationGraph()
	{
		var name = native.GetAnimationGraphName();
		return string.IsNullOrEmpty( name ) ? null : Sandbox.Animation.Graph.FromNative( native.GetAnimationGraph2(), name );
	}
}
